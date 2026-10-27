using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

object Read(string path)
{
    using FileStream stream = File.OpenRead(path);
    using PEReader pe = new(stream);
    MetadataReader r = pe.GetMetadataReader();
    List<string> records = new();
    string Blob(BlobHandle handle) => Convert.ToHexString(r.GetBlobBytes(handle));
    foreach (AssemblyReferenceHandle handle in r.AssemblyReferences)
    {
        var a = r.GetAssemblyReference(handle);
        records.Add($"AssemblyRef {r.GetString(a.Name)} {a.Version} {Blob(a.PublicKeyOrToken)}");
    }
    foreach (TypeReferenceHandle handle in r.TypeReferences)
    {
        var t = r.GetTypeReference(handle);
        records.Add($"TypeRef {r.GetString(t.Namespace)} {r.GetString(t.Name)} {MetadataTokens.GetToken(t.ResolutionScope)}");
    }
    foreach (MemberReferenceHandle handle in r.MemberReferences)
    {
        var m = r.GetMemberReference(handle);
        records.Add($"MemberRef {r.GetString(m.Name)} {MetadataTokens.GetToken(m.Parent)} {Blob(m.Signature)}");
    }
    foreach (TypeDefinitionHandle handle in r.TypeDefinitions)
    {
        var t = r.GetTypeDefinition(handle);
        var layout = t.GetLayout();
        records.Add($"Type {r.GetString(t.Namespace)} {r.GetString(t.Name)} {t.Attributes} {MetadataTokens.GetToken(t.BaseType)} {layout.Size} {layout.PackingSize}");
        foreach (var i in t.GetInterfaceImplementations()) records.Add($"Interface {MetadataTokens.GetToken(r.GetInterfaceImplementation(i).Interface)}");
    }
    foreach (FieldDefinitionHandle handle in r.FieldDefinitions)
    {
        var f = r.GetFieldDefinition(handle);
        records.Add($"Field {r.GetString(f.Name)} {f.Attributes} {Blob(f.Signature)} {f.GetOffset()}");
    }
    foreach (MethodDefinitionHandle handle in r.MethodDefinitions)
    {
        var m = r.GetMethodDefinition(handle);
        records.Add($"Method {r.GetString(m.Name)} {m.Attributes} {m.ImplAttributes} {Blob(m.Signature)}");
        if (m.RelativeVirtualAddress == 0) continue;
        MethodBodyBlock body = pe.GetMethodBody(m.RelativeVirtualAddress);
        records.Add($"Body {Convert.ToHexString(body.GetILBytes()!)} {body.MaxStack} {body.LocalVariablesInitialized}");
        if (!body.LocalSignature.IsNil) records.Add($"Locals {Blob(r.GetStandaloneSignature(body.LocalSignature).Signature)}");
        foreach (var e in body.ExceptionRegions) records.Add($"Exception {e.Kind} {e.TryOffset} {e.TryLength} {e.HandlerOffset} {e.HandlerLength} {e.FilterOffset} {MetadataTokens.GetToken(e.CatchType)}");
    }
    foreach (CustomAttributeHandle handle in r.CustomAttributes)
    {
        var a = r.GetCustomAttribute(handle);
        records.Add($"Attribute {MetadataTokens.GetToken(a.Parent)} {MetadataTokens.GetToken(a.Constructor)} {Blob(a.Value)}");
    }
    for (UserStringHandle h = MetadataTokens.UserStringHandle(1); !h.IsNil && MetadataTokens.GetHeapOffset(h) < r.GetHeapSize(HeapIndex.UserString); h = r.GetNextHandle(h))
        records.Add($"UserString {r.GetUserString(h)}");
    string side = path.StartsWith(args[0], StringComparison.OrdinalIgnoreCase) ? "A" : "B";
    File.WriteAllLines(Path.Combine(args[2], side + "-" + Path.GetFileName(path) + ".records.txt"), records);
    List<object> debug = new();
    foreach (var entry in pe.ReadDebugDirectory())
        if (entry.Type == DebugDirectoryEntryType.CodeView)
        {
            var data = pe.ReadCodeViewDebugDirectoryData(entry);
            debug.Add(new { data.Path, data.Guid, data.Age });
        }
    return new { name = r.GetString(r.GetAssemblyDefinition().Name), types = r.TypeDefinitions.Count, methods = r.MethodDefinitions.Count,
        api_il_hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', records)))),
        mvid = r.GetGuid(r.GetModuleDefinition().Mvid), debug };
}

string rootA = args[0], rootB = args[1], report = args[2];
using JsonDocument a = JsonDocument.Parse(File.ReadAllText(Path.Combine(report, "published-A.json")));
using JsonDocument b = JsonDocument.Parse(File.ReadAllText(Path.Combine(report, "published-B.json")));
List<object> results = new();
foreach (JsonProperty property in a.RootElement.EnumerateObject())
{
    if (!property.Name.EndsWith(".dll") || !b.RootElement.TryGetProperty(property.Name, out var other) ||
        property.Value.GetProperty("sha256").GetString() == other.GetProperty("sha256").GetString()) continue;
    var left = Read(Path.Combine(rootA, property.Name));
    var right = Read(Path.Combine(rootB, property.Name));
    results.Add(new { file = property.Name, A = left, B = right });
}
results.Add(new { file = "project-specific RuntimeGlue.dll", A = Read(Directory.GetFiles(rootA, "*.RuntimeGlue.dll").Single()), B = Read(Directory.GetFiles(rootB, "*.RuntimeGlue.dll").Single()) });
File.WriteAllText(Path.Combine(report, "assembly-api-il.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Audited {results.Count} changed managed assemblies using PEReader/MetadataReader.");
