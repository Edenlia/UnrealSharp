using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EpicGames.Core;

namespace UnrealSharp.Automation.Utilities;

// These V1 records describe completed generated output, not a native product or
// permission to access a path. Keep their JSON property order and comparers.
public class UhtProducerStamp<TLink> where TLink : UhtProducerLink
{
    public int Version { get; set; } = 1;
    public string Context { get; set; } = "";
    public string Inputs { get; set; } = "";
    public string Transaction { get; set; } = "";
    public string CommitPath { get; set; } = "";
    public string ProjectDirectory { get; set; } = "";
    public SortedDictionary<string, string> Target { get; set; } = new(StringComparer.Ordinal);
    public string ManifestHash { get; set; } = "";
    public SortedDictionary<string, string> ResolvedPaths { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, TLink> AncestorLinks { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, string> Files { get; set; } = new(StringComparer.Ordinal);
}

public class UhtProducerLink
{
    public string RawTarget { get; set; } = "";
    public string Target { get; set; } = "";
    public string PhysicalTarget { get; set; } = "";
}

public class UhtProducerCommit
{
    public int Version { get; set; } = 1;
    public string Transaction { get; set; } = "";
    public SortedDictionary<string, string> Stamps { get; set; } = new(StringComparer.Ordinal);
}

public class UhtProducerRoots
{
    public int Version { get; set; } = 1;
    public string ProjectDirectory { get; set; } = "";
    public string[] Roots { get; set; } = [];
}

/// <summary>Pure wire-format operations. Callers own all IO, authorization and locks.</summary>
public static class UhtProducerProtocol
{
    public static bool IsGlueBuildInput(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative is "UE5Rules.Defines.props" or "UnrealSharp.EngineGlue.props" or
            "UnrealSharp.Target.json" or "UnrealSharp.Target.props" or "UnrealSharp.References.targets";
    }

    public static bool MatchesSidecarHash(UHTManifest.Module module, byte[] bytes) =>
        module.TryGetDefine("UNREALSHARP_UHT_CONTEXT_SHA256", out string? expected) &&
        expected?.Trim('"') == Convert.ToHexString(SHA256.HashData(bytes));

    public static string ComputeManifestHash(UHTManifest manifest)
    {
        // UBT's ParallelQueue worker order is incidental only for these buckets.
        // Preserve membership, duplicates, bucket identity and all other arrays.
        JsonNode projection = JsonSerializer.SerializeToNode(manifest)
            ?? throw new System.IO.InvalidDataException("Missing UnrealSharp UHT manifest projection.");
        string[] buckets = ["ClassesHeaders", "PublicHeaders", "InternalHeaders", "PrivateHeaders"];
        foreach (JsonNode? module in projection[nameof(UHTManifest.Modules)]!.AsArray())
        {
            foreach (string bucket in buckets)
            {
                if (module?[bucket] is JsonArray headers)
                {
                    module[bucket] = new JsonArray(headers.Select(header => header!.GetValue<string>())
                        .OrderBy(header => header, StringComparer.Ordinal)
                        .Select(header => (JsonNode?)JsonValue.Create(header)).ToArray());
                }
            }
        }
        return HashText(projection.ToJsonString());
    }

    public static string ComputeContext<TLink>(SortedDictionary<string, string> target, string manifestHash,
        SortedDictionary<string, string> settings, string exporterHash, SortedDictionary<string, string> paths,
        SortedDictionary<string, TLink> links) where TLink : UhtProducerLink =>
        HashText(JsonSerializer.Serialize(new {
            Target = target, Manifest = manifestHash, Settings = settings,
            Exporter = exporterHash, Paths = paths, Links = links
        }));

    public static string ComputeExporterIdentity(string uhtAssemblyHash, string automationAssemblyHash) =>
        HashText(JsonSerializer.Serialize(new { Uht = uhtAssemblyHash, Automation = automationAssemblyHash }));

    public static string ComputeTransaction(string context, string inputs,
        SortedDictionary<string, SortedDictionary<string, string>> inventories) =>
        HashText(JsonSerializer.Serialize(new { Context = context, Inputs = inputs, Files = inventories }));

    // This is the existing commit-field predicate. The caller validates the
    // stamp schema, authorizes paths and supplies actual member bytes/hashes.
    public static bool MatchesCommitMetadata<TLink>(string stampPath, UhtProducerStamp<TLink> stamp,
        UhtProducerCommit commit) where TLink : UhtProducerLink =>
        commit.Version == 1 && commit.Transaction == stamp.Transaction && commit.Stamps.ContainsKey(stampPath);

    public static bool MatchesMemberHash(string expectedHash, string? actualHash) =>
        actualHash != null && actualHash == expectedHash;

    public static bool MatchesInventory(SortedDictionary<string, string> actual,
        SortedDictionary<string, string> expected) => actual.SequenceEqual(expected);

    public static string HashText(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
