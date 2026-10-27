using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
static object Read(string path)
{
    byte[] bytes = File.ReadAllBytes(path);
    using var pe = new PEReader(new MemoryStream(bytes));
    MetadataReader reader = pe.GetMetadataReader();
    byte[] metadata = pe.GetMetadata().GetContent().ToArray();
    string rawMetadata = Hash(metadata);
    // GUID heap entries are module identity / edit-and-continue IDs, not API/IL.
    // Keep raw originals and both hashes; do not discard names, tokens or ordering.
    int guidHeapBytes = reader.GetHeapSize(HeapIndex.Guid);
    if (guidHeapBytes != 16) throw new InvalidOperationException("Expected exactly one module MVID in GUID heap");
    Array.Clear(metadata, reader.GetHeapMetadataOffset(HeapIndex.Guid), guidHeapBytes);
    var methods = new Dictionary<string, string>();
    var fieldData = new Dictionary<string, string>();
    foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
    {
        TypeDefinition type = reader.GetTypeDefinition(typeHandle);
        string typeName = reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);
        foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
        {
            MethodDefinition method = reader.GetMethodDefinition(methodHandle);
            if (method.RelativeVirtualAddress == 0) continue;
            MethodBodyBlock body = pe.GetMethodBody(method.RelativeVirtualAddress);
            // The complete method block includes stack/locals/exception handlers and IL.
            byte[] content = pe.GetSectionData(method.RelativeVirtualAddress).GetContent(0, body.Size).ToArray();
            methods[MetadataTokens.GetToken(methodHandle).ToString("X8") + " " + typeName + "." + reader.GetString(method.Name)] = Hash(content);
        }
        foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
        {
            FieldDefinition field = reader.GetFieldDefinition(fieldHandle);
            int rva = field.GetRelativeVirtualAddress();
            if (rva == 0) continue;
            BlobReader signature = reader.GetBlobReader(field.Signature);
            signature.ReadByte(); // Field signature header.
            SignatureTypeCode code = signature.ReadSignatureTypeCode();
            int size;
            if (code == SignatureTypeCode.TypeHandle)
            {
                EntityHandle handle = signature.ReadTypeHandle();
                if (handle.Kind != HandleKind.TypeDefinition) throw new InvalidOperationException("Unknown field RVA type");
                size = reader.GetTypeDefinition((TypeDefinitionHandle)handle).GetLayout().Size;
            }
            else size = code switch
            {
                SignatureTypeCode.Byte or SignatureTypeCode.SByte or SignatureTypeCode.Boolean => 1,
                SignatureTypeCode.Int16 or SignatureTypeCode.UInt16 or SignatureTypeCode.Char => 2,
                SignatureTypeCode.Int32 or SignatureTypeCode.UInt32 or SignatureTypeCode.Single => 4,
                SignatureTypeCode.Int64 or SignatureTypeCode.UInt64 or SignatureTypeCode.Double => 8,
                _ => throw new InvalidOperationException("Unsupported field RVA signature " + code),
            };
            if (size <= 0) throw new InvalidOperationException("Unknown field RVA size");
            fieldData[MetadataTokens.GetToken(fieldHandle).ToString("X8")] = Hash(pe.GetSectionData(rva).GetContent(0, size).ToArray());
        }
    }
    var debug = pe.ReadDebugDirectory().Where(entry => entry.Type == DebugDirectoryEntryType.CodeView)
        .Select(entry => pe.ReadCodeViewDebugDirectoryData(entry)).Select(value => new { value.Path, value.Guid, value.Age }).ToArray();
    return new { path, bytes = bytes.Length, sha256 = Hash(bytes), rawMetadataSha256 = rawMetadata,
        metadataExceptModuleGuidsSha256 = Hash(metadata), guidHeapBytes, mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid),
        methodCount = methods.Count, methods, fieldData, debug };
}

Directory.CreateDirectory(args[2]);
var options = new JsonSerializerOptions { WriteIndented = true };
File.WriteAllText(Path.Combine(args[2], "left.json"), JsonSerializer.Serialize(Read(args[0]), options));
File.WriteAllText(Path.Combine(args[2], "right.json"), JsonSerializer.Serialize(Read(args[1]), options));
Console.WriteLine("Wrote full metadata/method-body/field-initializer receipts to " + args[2]);
