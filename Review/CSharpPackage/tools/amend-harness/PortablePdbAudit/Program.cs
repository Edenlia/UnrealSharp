using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text.Json;

using var stream = File.OpenRead(args[0]);
using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
var r = provider.GetMetadataReader();
string Blob(BlobHandle h) => Convert.ToHexString(r.GetBlobBytes(h));
var docs = r.Documents.Select(h => { var d = r.GetDocument(h); return new { token = MetadataTokens.GetToken(h), name = r.GetString(d.Name), hash = Blob(d.Hash), hashAlgorithm = r.GetGuid(d.HashAlgorithm), language = r.GetGuid(d.Language) }; }).ToArray();
var methods = r.MethodDebugInformation.Select(h => { var d = r.GetMethodDebugInformation(h); return new { token = MetadataTokens.GetToken(h), document = MetadataTokens.GetToken(d.Document), sequencePoints = Blob(d.SequencePointsBlob), kickoff = MetadataTokens.GetToken(d.GetStateMachineKickoffMethod()) }; }).ToArray();
var custom = r.CustomDebugInformation.Select(h => { var d = r.GetCustomDebugInformation(h); return new { token = MetadataTokens.GetToken(h), parent = MetadataTokens.GetToken(d.Parent), kind = r.GetGuid(d.Kind), value = Blob(d.Value) }; }).ToArray();
var scopes = r.LocalScopes.Select(h => { var d = r.GetLocalScope(h); return new { token = MetadataTokens.GetToken(h), method = MetadataTokens.GetToken(d.Method), import = MetadataTokens.GetToken(d.ImportScope), d.StartOffset, d.Length, variables = d.GetLocalVariables().Select(x => MetadataTokens.GetToken(x)).ToArray(), constants = d.GetLocalConstants().Select(x => MetadataTokens.GetToken(x)).ToArray() }; }).ToArray();
string ImportText(ImportDefinition i)
{
    string Text(BlobHandle h) => System.Text.Encoding.UTF8.GetString(r.GetBlobBytes(h));
    return i.Kind + ":" + (i.Kind switch
    {
        ImportDefinitionKind.ImportNamespace => Text(i.TargetNamespace),
        ImportDefinitionKind.ImportAssemblyNamespace => MetadataTokens.GetToken(i.TargetAssembly) + ":" + Text(i.TargetNamespace),
        ImportDefinitionKind.ImportType => MetadataTokens.GetToken(i.TargetType).ToString(),
        ImportDefinitionKind.ImportXmlNamespace => Text(i.Alias) + ":" + Text(i.TargetNamespace),
        ImportDefinitionKind.ImportAssemblyReferenceAlias => Text(i.Alias),
        ImportDefinitionKind.AliasAssemblyReference => Text(i.Alias) + ":" + MetadataTokens.GetToken(i.TargetAssembly),
        ImportDefinitionKind.AliasNamespace => Text(i.Alias) + ":" + Text(i.TargetNamespace),
        ImportDefinitionKind.AliasAssemblyNamespace => Text(i.Alias) + ":" + MetadataTokens.GetToken(i.TargetAssembly) + ":" + Text(i.TargetNamespace),
        ImportDefinitionKind.AliasType => Text(i.Alias) + ":" + MetadataTokens.GetToken(i.TargetType),
        _ => throw new InvalidOperationException(i.Kind.ToString())
    });
}
var imports = r.ImportScopes.Select(h => { var d = r.GetImportScope(h); return new { token = MetadataTokens.GetToken(h), parent = MetadataTokens.GetToken(d.Parent), value = Blob(d.ImportsBlob), decoded = d.GetImports().Select(ImportText).ToArray() }; }).ToArray();
var locals = r.LocalVariables.Select(h => { var d = r.GetLocalVariable(h); return new { token = MetadataTokens.GetToken(h), name = r.GetString(d.Name), d.Index, d.Attributes }; }).ToArray();
var constants = r.LocalConstants.Select(h => { var d = r.GetLocalConstant(h); return new { token = MetadataTokens.GetToken(h), name = r.GetString(d.Name), signature = Blob(d.Signature) }; }).ToArray();
File.WriteAllText(args[1], JsonSerializer.Serialize(new { path = args[0], id = Convert.ToHexString(r.DebugMetadataHeader!.Id.ToArray()), docs, methods, custom, scopes, imports, locals, constants }, new JsonSerializerOptions { WriteIndented = true }));
