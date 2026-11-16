using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using EpicGames.Core;
using UnrealBuildTool;

namespace UnrealSharp.Automation.Utilities;

// Architectures is the complete, resolved ordinal-sorted set, not an optional
// command-line architecture hint. Tool paths identify the actual producer DLLs.
internal sealed record UhtProducerReadRequest(string ManifestFile, string ProjectFile, string PluginDirectory,
    string EngineDirectory, string TargetName, string Platform, UnrealTargetConfiguration Configuration,
    string Architectures, string UhtAssemblyFile, string AutomationAssemblyFile);

internal sealed class UhtProducerSnapshot : IDisposable
{
    internal UhtProducerReadRequest Request { get; }
    internal string ManifestHash { get; }
    internal string Context { get; }
    internal string Inputs { get; } // Opaque parsed-header fingerprint; not independently reconstructed.
    internal string Transaction { get; }
    internal IReadOnlyDictionary<string, string> FileHashes { get; }
    internal IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Inventories { get; }
    private readonly Action _verify;
    private readonly List<FileStream> _locks;
    private bool _disposed;

    internal UhtProducerSnapshot(UhtProducerReadRequest request, string manifestHash, string context, string inputs, string transaction, Dictionary<string, string> hashes,
        SortedDictionary<string, SortedDictionary<string, string>> inventories, Action verify, List<FileStream> locks)
    {
        Request = request;
        ManifestHash = manifestHash;
        Context = context;
        Inputs = inputs;
        Transaction = transaction;
        FileHashes = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(hashes, UhtProducerReadPaths.Comparer));
        Inventories = new ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>(inventories.ToDictionary(entry => entry.Key,
            entry => (IReadOnlyDictionary<string, string>)new ReadOnlyDictionary<string, string>(new SortedDictionary<string, string>(entry.Value, StringComparer.Ordinal)),
            UhtProducerReadPaths.Comparer));
        _verify = verify;
        _locks = locks;
    }

    internal void RequireUnchanged()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(UhtProducerSnapshot));
        _verify();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (FileStream stream in _locks) stream.Dispose();
        _locks.Clear();
    }
}

// No writer initialization, directory creation, metadata publication or product
// readiness. The returned snapshot holds existing read locks until disposed.
internal sealed class UhtProducerReader
{
    private const string StampName = "UnrealSharp.Producer.json", CommitName = "UnrealSharp.ProducerCommit.json";
    private const string IndexName = "UnrealSharp.ProducerRoots.json", LockName = "UnrealSharp.Producer.lock";
    private static readonly StringComparer Paths = UhtProducerReadPaths.Comparer;
    private readonly UhtProducerReadRequest _request;
    private readonly UhtProducerReadPaths _paths = new();
    private readonly Dictionary<string, string> _hashes = new(Paths);
    private readonly List<FileStream> _locks = [];
    private readonly HashSet<string> _physicalLocks = new(Paths);
    private readonly HashSet<string> _currentRoots = new(Paths);
    private readonly string _project;

    private UhtProducerReader(UhtProducerReadRequest request)
    {
        _request = request with {
            ManifestFile = Full(request.ManifestFile), ProjectFile = Full(request.ProjectFile), PluginDirectory = Full(request.PluginDirectory),
            EngineDirectory = Full(request.EngineDirectory), UhtAssemblyFile = Full(request.UhtAssemblyFile), AutomationAssemblyFile = Full(request.AutomationAssemblyFile)
        };
        _project = Path.GetDirectoryName(_request.ProjectFile)!;
    }

    internal static UhtProducerSnapshot Read(UhtProducerReadRequest request)
    {
        var reader = new UhtProducerReader(request);
        try { return reader.Read(); }
        catch { foreach (FileStream stream in reader._locks) stream.Dispose(); throw; }
    }

    private UhtProducerSnapshot Read()
    {
        Require(Path.GetExtension(_request.ProjectFile) == ".uproject" && File.Exists(_request.ProjectFile), "Missing actual producer project.");
        Require(!string.IsNullOrWhiteSpace(_request.TargetName) && !string.IsNullOrWhiteSpace(_request.Platform) &&
            _request.Configuration != UnrealTargetConfiguration.Unknown && !string.IsNullOrWhiteSpace(_request.Architectures), "Incomplete producer profile.");
        Require(string.Join("+", _request.Architectures.Split('+').Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal)) == _request.Architectures,
            "Producer architectures must be the complete sorted set.");
        Require(UhtProducerReadPaths.IsUnder(_request.ManifestFile, Path.Combine(_project, "Intermediate", "Build")) &&
            Path.GetFileName(_request.ManifestFile) == _request.TargetName + ".uhtmanifest", "Unexpected actual UHT manifest path.");
        string[] anchors = [Root(_request.PluginDirectory), Root(_project)];
        _paths.Capture(anchors.Append(Path.GetDirectoryName(_request.ManifestFile)!));
        Hash(_request.ProjectFile);
        UHTManifest manifest = ReadJson<UHTManifest>(_request.ManifestFile);
        Require(manifest.IsGameTarget && manifest.TargetName == _request.TargetName &&
            Paths.Equals(Full(Path.Combine(manifest.RootLocalPath, "Engine")), _request.EngineDirectory), "Manifest does not match the frozen Game request.");
        UHTManifest.Module[] core = manifest.Modules.Where(module => module.Name == "UnrealSharpCore").ToArray();
        Require(core.Length == 1 && Paths.Equals(Full(core[0].BaseDirectory), Path.Combine(_request.PluginDirectory, "Source", "UnrealSharpCore")), "Manifest has no unique current UnrealSharpCore.");
        string native = Full(core[0].OutputDirectory);
        Require(UhtProducerReadPaths.IsUnder(native, Path.Combine(_request.PluginDirectory, "Intermediate", "Build")), "Core UHT output is outside the current plugin.");
        foreach (string anchor in anchors) _currentRoots.Add(anchor);
        foreach (UHTManifest.Module module in manifest.Modules)
        {
            if (module.TryGetDefine("SkipGlueGeneration", out int skip) && skip != 0) continue;
            bool engine = module.ModuleType.ToString().StartsWith("Engine", StringComparison.Ordinal) ||
                (module.ModuleType == UHTModuleType.Program && UhtProducerReadPaths.IsUnder(Full(module.BaseDirectory), _request.EngineDirectory)) ||
                (module.TryGetDefine("ForceAsEngineGlue", out int forced) && forced != 0);
            if (!engine) _currentRoots.Add(Root(FindOwner(module.BaseDirectory)));
        }
        _paths.Capture(_currentRoots.Append(native));
        LockRoots(anchors);

        byte[] sidecar = Bytes(Path.Combine(native, "UnrealSharp.TargetContext.json"));
        Require(core[0].PublicDefines.Count(value => value.StartsWith("UNREALSHARP_UHT_CONTEXT_SHA256=", StringComparison.Ordinal)) == 1 &&
            UhtProducerProtocol.MatchesSidecarHash(core[0], sidecar), "Manifest does not bind actual sidecar bytes.");
        var target = new SortedDictionary<string, string>(JsonSerializer.Deserialize<Dictionary<string, string>>(sidecar)
            ?? throw new InvalidDataException("Missing producer target context."), StringComparer.Ordinal);
        var expectedTarget = new SortedDictionary<string, string>(StringComparer.Ordinal) {
            ["TargetName"] = _request.TargetName, ["TargetType"] = "Game", ["TargetTypeValue"] = ((int)TargetType.Game).ToString(),
            ["Platform"] = _request.Platform, ["Configuration"] = _request.Configuration.ToString(), ["ConfigurationValue"] = ((int)_request.Configuration).ToString(),
            ["Architectures"] = _request.Architectures, ["ProjectFile"] = _request.ProjectFile, ["PluginDirectory"] = _request.PluginDirectory, ["EngineDirectory"] = _request.EngineDirectory
        };
        Require(target.Count == expectedTarget.Count && expectedTarget.All(entry => target.TryGetValue(entry.Key, out string? value) &&
            (entry.Key.EndsWith("Directory", StringComparison.Ordinal) || entry.Key == "ProjectFile" ? value != null && Paths.Equals(Full(value), entry.Value) : value == entry.Value)), "Producer profile differs from the actual request.");
        Require(core[0].TryGetDefine("TARGET_TYPE", out int targetType) && targetType == (int)TargetType.Game &&
            core[0].TryGetDefine("TARGET_CONFIGURATION", out int configuration) && configuration == (int)_request.Configuration, "Manifest profile definitions differ.");

        string commitPath = Path.Combine(native, CommitName);
        UhtProducerCommit commit = ReadJson<UhtProducerCommit>(commitPath);
        Require(commit.Version == 1 && IsHash(commit.Transaction) && commit.Stamps.Count >= anchors.Length, "Incomplete producer commit.");
        string[] roots = commit.Stamps.Keys.Select(path => {
            Require(Path.GetFileName(path) == StampName && Paths.Equals(Full(path), path), "Unexpected producer member path.");
            string root = Path.GetDirectoryName(path)!; AuthorizeRoot(root); return root;
        }).Distinct(Paths).OrderBy(path => path, Paths).ToArray();
        Require(roots.Length == commit.Stamps.Count && anchors.All(anchor => roots.Contains(anchor, Paths)), "Producer commit is missing current anchors.");
        LockRoots(roots);
        string[] settingsFiles = SettingsFiles();
        var settings = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in settingsFiles) settings.Add(file, Hash(file));
        string depsPath = Full(manifest.ExternalDependenciesFile);
        Require(Paths.Equals(depsPath, Path.ChangeExtension(_request.ManifestFile, ".deps")), "Unexpected actual UHT dependency file.");
        string[] dependencies = Encoding.UTF8.GetString(Bytes(depsPath)).TrimStart('\uFEFF').Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(path => Full(path.Trim())).ToArray();
        Require(Path.GetFileName(_request.UhtAssemblyFile) == "UnrealSharpManagedGlue.dll" &&
            Path.GetFileName(_request.AutomationAssemblyFile) == "UnrealSharp.Automation.dll" &&
            manifest.UhtPlugins.Any(path => Paths.Equals(Full(path), _request.UhtAssemblyFile)) &&
            dependencies.Where(path => Path.GetFileName(path) == "UnrealSharpManagedGlue.dll").Distinct(Paths).SequenceEqual([_request.UhtAssemblyFile], Paths) &&
            dependencies.Where(path => Path.GetFileName(path) == "UnrealSharp.Automation.dll").Distinct(Paths).SequenceEqual([_request.AutomationAssemblyFile], Paths),
            "Producer tool paths do not match the actual manifest/UHT dependencies.");
        string[] requiredDependencies = roots.SelectMany(root => new[] { Path.Combine(root, StampName), Path.Combine(root, IndexName) })
            .Concat(settingsFiles).Append(Path.Combine(native, "UnrealSharp.TargetContext.json")).Append(commitPath).ToArray();
        Require(requiredDependencies.All(path => dependencies.Contains(path, Paths)), "UHT dependencies omit producer inputs.");
        string exporter = UhtProducerProtocol.ComputeExporterIdentity(Hash(_request.UhtAssemblyFile), Hash(_request.AutomationAssemblyFile));
        string manifestHash = UhtProducerProtocol.ComputeManifestHash(manifest);
        string? context = null, inputs = null;
        var inventories = new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
        foreach (string root in roots)
        {
            string stampPath = Path.Combine(root, StampName);
            var stamp = ReadJson<UhtProducerStamp<UhtProducerLink>>(stampPath);
            Require(stamp.Version == 1 && UhtProducerProtocol.MatchesCommitMetadata(stampPath, stamp, commit) &&
                UhtProducerProtocol.MatchesMemberHash(commit.Stamps[stampPath], _hashes[stampPath]) &&
                Paths.Equals(Full(stamp.ProjectDirectory), _project) && Paths.Equals(Full(stamp.CommitPath), commitPath), "Incomplete or foreign producer stamp.");
            Require(stamp.Target.SequenceEqual(target) && stamp.ManifestHash == manifestHash && IsHash(stamp.Inputs), "Producer stamp profile/manifest differs.");
            Require(stamp.ResolvedPaths.ContainsKey(native) && anchors.All(anchor => stamp.ResolvedPaths.ContainsKey(anchor)) &&
                stamp.ResolvedPaths.Keys.All(path => _currentRoots.Contains(Full(path)) || Paths.Equals(Full(path), native)), "Unverified published output root.");
            var publishedPaths = new UhtProducerReadPaths();
            publishedPaths.Capture(stamp.ResolvedPaths.Keys);
            Require(publishedPaths.MatchesPublished(stamp.ResolvedPaths, stamp.AncestorLinks), "Published physical paths/ancestor chain changed.");
            string computed = UhtProducerProtocol.ComputeContext(target, manifestHash, settings, exporter,
                new SortedDictionary<string, string>(stamp.ResolvedPaths, StringComparer.Ordinal),
                new SortedDictionary<string, UhtProducerLink>(stamp.AncestorLinks, Paths));
            Require(computed == stamp.Context && (context == null || context == stamp.Context) && (inputs == null || inputs == stamp.Inputs), "Producer context/tool identity differs.");
            context = stamp.Context; inputs = stamp.Inputs;
            UhtProducerRoots index = ReadJson<UhtProducerRoots>(Path.Combine(root, IndexName));
            Require(index.Version == 1 && Paths.Equals(Full(index.ProjectDirectory), _project) && index.Roots.SequenceEqual(roots, Paths), "Producer root index differs from completion.");
            var inventory = Inventory(root);
            Require(UhtProducerProtocol.MatchesInventory(inventory, new SortedDictionary<string, string>(stamp.Files, StringComparer.Ordinal)), "Generated bytes differ from producer inventory.");
            inventories.Add(root, inventory);
        }
        Require(UhtProducerProtocol.ComputeTransaction(context!, inputs!, inventories) == commit.Transaction, "Producer transaction does not bind all generated inventories.");
        void Verify()
        {
            _paths.VerifyAll();
            Require(settingsFiles.SequenceEqual(SettingsFiles(), Paths), "Producer configuration file set changed.");
            foreach (var file in _hashes.ToArray())
                Require(Hash(file.Key) == file.Value, "Producer input changed: " + file.Key);
            foreach (var root in inventories)
                Require(UhtProducerProtocol.MatchesInventory(Inventory(root.Key), root.Value), "Producer generated file set changed: " + root.Key);
            _paths.VerifyAll();
        }
        Verify();
        return new UhtProducerSnapshot(_request, manifestHash, context!, inputs!, commit.Transaction, _hashes, inventories, Verify, _locks);
    }

    private static string Full(string path)
    {
        Require(!string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path), "Producer paths must be explicit absolute paths.");
        return Path.GetFullPath(path);
    }

    private static string Root(string owner) => Path.Combine(Full(owner), "Intermediate", "UnrealSharp", "UHT", "Game");
    private static bool IsHash(string value) => value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidDataException(reason); }

    private string FindOwner(string moduleDirectory)
    {
        string full = Full(moduleDirectory);
        _paths.Capture([full]);
        for (DirectoryInfo? directory = new(full); directory != null; directory = directory.Parent)
        {
            _paths.Ensure(directory.FullName);
            string[] descriptors = Directory.EnumerateFiles(directory.FullName, "*", SearchOption.TopDirectoryOnly)
                .Where(path => Path.GetExtension(path) is ".uproject" or ".uplugin").ToArray();
            if (descriptors.Length != 0)
            {
                foreach (string descriptor in descriptors) Hash(descriptor);
                return directory.FullName;
            }
        }
        throw new InvalidDataException("No current module project/plugin owner: " + full);
    }

    private void AuthorizeRoot(string root)
    {
        string full = Full(root);
        Require(full.EndsWith(Path.DirectorySeparatorChar + Path.Combine("Intermediate", "UnrealSharp", "UHT", "Game"),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
            (_currentRoots.Contains(full) || UhtProducerReadPaths.IsUnder(full, _project) || UhtProducerReadPaths.IsUnder(full, _request.PluginDirectory)), "Unverified producer member root: " + root);
        _paths.Ensure(full);
    }

    private void LockRoots(IEnumerable<string> roots)
    {
        foreach (string root in roots.Distinct(Paths).OrderBy(path => path, Paths))
        {
            AuthorizeRoot(root);
            string physical = _paths.Track(root);
            if (!_physicalLocks.Add(physical)) continue;
            string lockPath = Path.Combine(physical, LockName);
            _paths.Ensure(lockPath);
            _locks.Add(new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.Read));
            Require(Paths.Equals(_paths.Ensure(root), physical), "Producer path changed while opening its existing lock.");
        }
    }

    private byte[] Bytes(string path)
    {
        path = Full(path); _paths.Ensure(path);
        byte[] bytes = File.ReadAllBytes(path);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        Require(!_hashes.TryGetValue(path, out string? previous) || previous == hash, "Producer input changed while reading: " + path);
        _hashes[path] = hash;
        return bytes;
    }

    private string Hash(string path) { Bytes(path); return _hashes[Full(path)]; }
    private T ReadJson<T>(string path) => JsonSerializer.Deserialize<T>(Bytes(path)) ?? throw new InvalidDataException("Invalid producer metadata: " + path);

    private IEnumerable<string> Files(string directory, bool skipBuildOutputs = true)
    {
        _paths.Ensure(directory);
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            _paths.Ensure(path);
            if (!Directory.Exists(path)) yield return Full(path);
            else if (!skipBuildOutputs || Path.GetFileName(path) is not ("bin" or "obj"))
                foreach (string child in Files(path, skipBuildOutputs)) yield return child;
        }
    }

    private SortedDictionary<string, string> Inventory(string root)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in Files(root))
        {
            bool owned = path.EndsWith(".generated.cs", StringComparison.Ordinal) || Paths.Equals(path, Path.Combine(root, "UE5Rules.Defines.props"));
            if (path.EndsWith(".csproj", StringComparison.Ordinal))
            {
                using var bytes = new MemoryStream(Bytes(path), writable: false);
                XDocument project = XDocument.Load(bytes);
                owned = Path.GetFileNameWithoutExtension(path) == Path.GetFileName(Path.GetDirectoryName(path)) &&
                    project.Descendants("SkipIncludeAnalyzers").Any(value => value.Value == "true") && project.Descendants("Import")
                        .Any(import => ((string?)import.Attribute("Project"))?.Contains("UnrealSharp", StringComparison.OrdinalIgnoreCase) == true);
            }
            Require(owned || (!path.EndsWith(".cs", StringComparison.Ordinal) && !path.EndsWith(".csproj", StringComparison.Ordinal)), "Unrecognized source in producer output: " + path);
            if (owned) result.Add(Path.GetRelativePath(root, path).Replace('\\', '/'), Hash(path));
        }
        return result;
    }

    private string[] SettingsFiles()
    {
        var result = new List<string>();
        foreach (string owner in new[] { _request.PluginDirectory, _project })
        {
            string directory = Path.Combine(owner, "Config"); _paths.Ensure(directory);
            string[] found = Directory.Exists(directory) ? Files(directory, skipBuildOutputs: false).Where(path => Path.GetFileName(path) == "UnrealSharp.Settings.json").ToArray() : [];
            Require(found.Length <= 1 && (owner != _request.PluginDirectory || found.Length == 1), "Missing/ambiguous producer configuration.");
            result.AddRange(found);
        }
        return result.ToArray();
    }
}
