using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using EpicGames.Core;
using UnrealSharp.Automation.Utilities;

namespace UnrealSharpManagedGlue.Utilities;

/// <summary>Ownership and completion of the shared TargetType generated-source trees.</summary>
public static class ExporterProducerContext
{
    private const string StampName = "UnrealSharp.Producer.json";
    private const string CommitName = "UnrealSharp.ProducerCommit.json";
    private const string RootsName = "UnrealSharp.ProducerRoots.json";
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static string _context = "";
    private static string _inputs = "";
    private static string _commitPath = "";
    private static string _projectDirectory = "";
    private static string _pluginDirectory = "";
    private static string _manifestHash = "";
    private static SortedDictionary<string, string> _target = new(StringComparer.Ordinal);
    private static readonly SortedSet<string> Roots = new(PathComparer);
    private static readonly HashSet<string> CurrentRoots = new(PathComparer);
    private static readonly Dictionary<string, ProducerStamp> Previous = new(PathComparer);
    private static readonly Dictionary<string, FileStream> Locks = new(PathComparer);
    private static readonly Dictionary<string, FileStream> PhysicalLocks = new(PathComparer);
    private static readonly Dictionary<string, string> LockPaths = new(PathComparer);
    private static readonly SortedDictionary<string, string> ResolvedPaths = new(StringComparer.Ordinal);
    private static readonly SortedDictionary<string, ProducerLink> AncestorLinks = new(PathComparer);
    private static readonly object CompletionLock = new();
    private static bool _completionMayExist = true;
    private static string _sidecarPath = "";
    private static byte[] _sidecarBytes = [];

    // Keep the existing public nested types, including their closed link-map
    // type, while sharing the same V1 properties and wire operations with UAT.
    public sealed class ProducerStamp : UhtProducerStamp<ProducerLink> { }
    public sealed class ProducerLink : UhtProducerLink { }
    public sealed class ProducerCommit : UhtProducerCommit { }
    public sealed class ProducerRoots : UhtProducerRoots { }

    public static void Reset()
    {
        foreach (FileStream stream in PhysicalLocks.Values)
            stream.Dispose();
        Locks.Clear();
        PhysicalLocks.Clear();
        LockPaths.Clear();
        ResolvedPaths.Clear();
        AncestorLinks.Clear();
        _completionMayExist = true;
        _sidecarPath = "";
        _sidecarBytes = [];
        Roots.Clear();
        CurrentRoots.Clear();
        Previous.Clear();
        _context = _inputs = _commitPath = _projectDirectory = _pluginDirectory = _manifestHash = "";
        _target.Clear();
    }

    public static bool Initialize()
    {
        // Only actual current UHT outputs may introduce trusted ancestor links.
        // Historical metadata can never grant a new link traversal permission.
        CaptureCurrentPaths(ModuleUtilities.PackageToModuleInfo.Values
            .Select(module => Path.GetDirectoryName(module.GlueOutputDirectory)!)
            .Append(PathUtilities.GetUhtGeneratedOutputPath(GeneratorStatics.PluginDirectory, GeneratorStatics.TargetType))
            .Append(PathUtilities.GetUhtGeneratedOutputPath(GeneratorStatics.Factory.Session.ProjectDirectory!, GeneratorStatics.TargetType))
            .Append(GeneratorStatics.PluginModule.OutputDirectory));
        string sidecar = Path.Combine(GeneratorStatics.PluginModule.OutputDirectory, "UnrealSharp.TargetContext.json");
        EnsureSafePath(sidecar);
        _sidecarPath = sidecar;
        _sidecarBytes = File.ReadAllBytes(sidecar);
        ValidateSidecarHash(GeneratorStatics.PluginModule, _sidecarBytes);
        Dictionary<string, string> target = JsonSerializer.Deserialize<Dictionary<string, string>>(_sidecarBytes)
            ?? throw new InvalidDataException("Missing UnrealSharp UHT target context.");
        var manifest = GeneratorStatics.Factory.Session.Manifest
            ?? throw new InvalidDataException("Missing UnrealSharp UHT manifest.");
        if (target["TargetName"] != manifest.TargetName ||
            target["TargetTypeValue"] != ((int)GeneratorStatics.TargetType).ToString() ||
            target["ConfigurationValue"] != ((int)GeneratorStatics.TargetConfiguration).ToString() ||
            !PathComparer.Equals(Path.GetFullPath(target["PluginDirectory"]), Path.GetFullPath(GeneratorStatics.PluginDirectory)) ||
            !PathComparer.Equals(Path.GetFullPath(target["EngineDirectory"]), Path.GetFullPath(Path.Combine(manifest.RootLocalPath, "Engine"))) ||
            !PathComparer.Equals(Path.GetDirectoryName(target["ProjectFile"]), GeneratorStatics.Factory.Session.ProjectDirectory))
        {
            throw new InvalidDataException("UnrealSharp UHT context does not match the actual manifest/project. Regenerate the target makefile.");
        }

        foreach (ModuleInfo module in ModuleUtilities.PackageToModuleInfo.Values)
            Roots.Add(Path.GetFullPath(Path.GetDirectoryName(module.GlueOutputDirectory)!));
        Roots.Add(Path.GetFullPath(PathUtilities.GetUhtGeneratedOutputPath(GeneratorStatics.PluginDirectory, GeneratorStatics.TargetType)));
        Roots.Add(Path.GetFullPath(PathUtilities.GetUhtGeneratedOutputPath(GeneratorStatics.Factory.Session.ProjectDirectory!, GeneratorStatics.TargetType)));
        _projectDirectory = Path.GetFullPath(GeneratorStatics.Factory.Session.ProjectDirectory!);
        _pluginDirectory = Path.GetFullPath(GeneratorStatics.PluginDirectory);
        CurrentRoots.UnionWith(Roots);
        AcquireRootLocks(Roots);

        // This is an ownership index, never a completion signal. Retain it across
        // failed transactions so removed modules' roots can still be cleaned.
        foreach (string root in Roots.ToArray())
        {
            string indexPath = Path.Combine(root, RootsName);
            if (!File.Exists(indexPath))
                continue;
            EnsureSafePath(indexPath);
            ProducerRoots index = JsonSerializer.Deserialize<ProducerRoots>(File.ReadAllText(indexPath))
                ?? throw new InvalidDataException("Invalid UnrealSharp generated-root index.");
            if (index.Version != 1)
                throw new InvalidDataException("Unsupported UnrealSharp generated-root index.");
            if (PathComparer.Equals(index.ProjectDirectory, _projectDirectory))
            {
                foreach (string previousRoot in index.Roots)
                {
                    string fullRoot = Path.GetFullPath(previousRoot);
                    AuthorizeRoot(fullRoot);
                    AcquireRootLocks([fullRoot]);
                    Roots.Add(fullRoot);
                }
            }
        }

        var settings = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in UnrealSharpSettingsUtilities.ConfigPaths)
            settings[Path.GetFullPath(path)] = File.Exists(path) ? HashFile(path) : "";
        _target = new SortedDictionary<string, string>(target, StringComparer.Ordinal);
        _manifestHash = GetManifestHash(manifest);
        _context = UhtProducerProtocol.ComputeContext(_target, _manifestHash, settings,
            GetExporterIdentity(GeneratorStatics.Factory.AddExternalDependency), ResolvedPaths, AncestorLinks);
        _inputs = HashText(JsonSerializer.Serialize(ModuleUtilities.PackageToModuleInfo.Values
            .SelectMany(module => module.Module.Children.Select(child => child.HeaderFile.FilePath))
            .Distinct(PathComparer).OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new { Path = path, Time = File.GetLastWriteTimeUtc(path).Ticks, Length = new FileInfo(path).Length })));
        _commitPath = Path.Combine(GeneratorStatics.PluginModule.OutputDirectory, CommitName);
        GeneratorStatics.Factory.AddExternalDependency(sidecar);
        GeneratorStatics.Factory.AddExternalDependency(_commitPath);

        // Previously emitted roots may contain a module that disappeared from this manifest.
        foreach (string root in Roots.ToArray())
        {
            string stampPath = Path.Combine(root, StampName);
            if (!File.Exists(stampPath))
                continue;
            ProducerStamp stamp = ReadStamp(stampPath);
            AcquirePreviousRoots(stampPath, stamp);
        }

        foreach (string root in Roots)
        {
            AuthorizeRoot(root);
            string stampPath = Path.Combine(root, StampName);
            GeneratorStatics.Factory.AddExternalDependency(stampPath);
            GeneratorStatics.Factory.AddExternalDependency(Path.Combine(root, RootsName));
            if (File.Exists(stampPath))
            {
                ProducerStamp stamp = ReadStamp(stampPath);
                ValidateCommit(stampPath, stamp);
                ValidateFiles(root, stamp.Files);
                Previous.Add(root, stamp);
            }
        }
        return Roots.All(root => Previous.TryGetValue(root, out ProducerStamp? stamp) && stamp.Context == _context);
    }

    private static string GetExporterIdentity(Action<string> addExternalDependency)
    {
        // The protocol implementation is a tool input even when changing its
        // internals leaves the referencing UHT assembly byte-identical.
        var uhtAssembly = Program.GetAssemblySource(Assembly.GetExecutingAssembly());
        var automationAssembly = Program.GetAssemblySource(typeof(UhtProducerProtocol).Assembly);
        addExternalDependency(uhtAssembly.Path);
        addExternalDependency(automationAssembly.Path);
        return UhtProducerProtocol.ComputeExporterIdentity(GetAssemblyHash(uhtAssembly), GetAssemblyHash(automationAssembly));
    }

    private static string GetAssemblyHash((string Path, string? LoadedHash) source)
    {
        string hash = HashFile(source.Path);
        if (source.LoadedHash != null && source.LoadedHash != hash)
            throw new InvalidDataException($"Producer assembly changed after loading: '{source.Path}'. Retry with a new UHT process.");
        return hash;
    }

    public static void InvalidateCompletion()
    {
        lock (CompletionLock)
        {
            if (!_completionMayExist)
                return;
            foreach (string root in Roots)
            {
                // A failed lock acquisition releases all handles. Never revoke an
                // exporter's producer unless this invocation still owns that root.
                if (!Locks.ContainsKey(root))
                    continue;
                AuthorizeRoot(root);
                string path = Path.Combine(root, StampName);
                EnsureSafePath(path);
                if (File.Exists(path))
                    File.Delete(path);
            }
            _completionMayExist = false;
        }
    }

    private static void AcquireRootLocks(IEnumerable<string> roots)
    {
        try
        {
            foreach (string root in roots.Select(Path.GetFullPath).Distinct(PathComparer).OrderBy(path => path, PathComparer))
            {
                AuthorizeRoot(root);
                if (Locks.ContainsKey(root))
                    continue;
                string physical = ResolvePhysicalPath(root);
                Directory.CreateDirectory(root);
                EnsureSafePath(root);
                if (!PathComparer.Equals(physical, ResolvePhysicalPath(root)))
                    throw new InvalidDataException("UnrealSharp output identity changed before locking: " + root);
                string path = Path.Combine(physical, "UnrealSharp.Producer.lock");
                EnsureSafePath(path);
                if (!PhysicalLocks.TryGetValue(physical, out FileStream? stream))
                {
                    stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    PhysicalLocks.Add(physical, stream);
                }
                LockPaths.Add(root, physical);
                Locks.Add(root, stream);
                EnsureSafePath(root);
            }
        }
        catch (Exception error)
        {
            foreach (FileStream stream in PhysicalLocks.Values)
                stream.Dispose();
            Locks.Clear();
            PhysicalLocks.Clear();
            LockPaths.Clear();
            throw new IOException("Cannot exclusively lock UnrealSharp generated roots; another exporter may be active.", error);
        }
    }

    private static void AcquirePreviousRoots(string stampPath, ProducerStamp stamp)
    {
        ProducerCommit commit = ReadCommit(stamp.CommitPath);
        if (!PathComparer.Equals(stamp.ProjectDirectory, _projectDirectory))
        {
            // A normal writer of the old transaction also needs this shared
            // anchor. Keep that lock, but never create a foreign project's lock.
            string anchor = Path.GetDirectoryName(stampPath)!;
            if (!Locks.ContainsKey(anchor) || commit.Version != 1 || commit.Transaction != stamp.Transaction ||
                !commit.Stamps.TryGetValue(stampPath, out string? hash) || HashFile(stampPath) != hash)
                throw new InvalidDataException("Cannot establish a locked shared anchor for the previous UnrealSharp producer.");
            return;
        }

        string[] previousRoots = commit.Stamps.Keys.Select(path => Path.GetFullPath(Path.GetDirectoryName(path)!)).ToArray();
        foreach (string root in previousRoots)
            AuthorizeRoot(root);
        AcquireRootLocks(previousRoots);
        Roots.UnionWith(previousRoots);
    }

    public static void PrepareIncrementalExport()
    {
        // The previous producer still owns unchanged output. Invalidate before a
        // real write, or before input changes, so failed exports cannot certify it.
        if (Previous.Values.Any(stamp => stamp.Inputs != _inputs))
            InvalidateCompletion();
    }

    public static void CleanGeneratedOutputs()
    {
        WriteRootIndexes();
        InvalidateCompletion();
        foreach (string root in Roots)
        {
            foreach (string path in GetOwnedFiles(root))
            {
                EnsureSafePath(path);
                File.Delete(path);
            }
        }
    }

    public static void Complete()
    {
        if (!string.IsNullOrEmpty(_sidecarPath))
        {
            EnsureSafePath(_sidecarPath);
            if (!File.ReadAllBytes(_sidecarPath).SequenceEqual(_sidecarBytes))
                throw new InvalidDataException("UnrealSharp UHT target context changed during export; multiple profiles sharing the native UHT directory require separate invocations.");
        }
        var inventories = new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
        var expected = new HashSet<string>(FileExporter.GetAffectedFiles().Select(Path.GetFullPath), PathComparer);
        foreach (string root in Roots)
        {
            SortedDictionary<string, string> files = Inventory(root);
            foreach (string relative in files.Keys.Where(path => path.EndsWith(".generated.cs", StringComparison.Ordinal)))
            {
                if (!expected.Contains(Path.GetFullPath(Path.Combine(root, relative))))
                    throw new InvalidDataException("Unremoved generated source: " + Path.Combine(root, relative));
            }
            inventories.Add(root, files);
        }
        string transaction = UhtProducerProtocol.ComputeTransaction(_context, _inputs, inventories);
        WriteRootIndexes();
        var commit = new ProducerCommit { Transaction = transaction };
        // A failed publication can leave new root stamps after a prior full
        // invalidation. Re-arm cleanup before the first stamp may be written.
        lock (CompletionLock)
            _completionMayExist = true;
        foreach (var root in inventories)
        {
            var stamp = new ProducerStamp {
                Context = _context, Inputs = _inputs, Transaction = transaction,
                CommitPath = _commitPath, ProjectDirectory = _projectDirectory,
                Target = _target, ManifestHash = _manifestHash, Files = root.Value,
                ResolvedPaths = new(ResolvedPaths, StringComparer.Ordinal), AncestorLinks = new(AncestorLinks, StringComparer.Ordinal)
            };
            string path = Path.Combine(root.Key, StampName);
            string json = JsonSerializer.Serialize(stamp, JsonOptions);
            WriteIfDifferent(path, json);
            commit.Stamps.Add(path, HashFile(path));
        }
        // A root stamp alone is never sufficient: publish the cross-root commit last.
        WriteIfDifferent(_commitPath, JsonSerializer.Serialize(commit, JsonOptions));
    }

    private static void ValidateSidecarHash(UHTManifest.Module module, byte[] bytes)
    {
        if (!UhtProducerProtocol.MatchesSidecarHash(module, bytes))
            throw new InvalidDataException("UnrealSharp UHT manifest/context hash mismatch. Different profiles sharing native UHT output require separate invocations.");
    }

    private static string GetManifestHash(UHTManifest manifest) => UhtProducerProtocol.ComputeManifestHash(manifest);

    private static void WriteRootIndexes()
    {
        string contents = JsonSerializer.Serialize(new ProducerRoots {
            ProjectDirectory = _projectDirectory, Roots = Roots.ToArray()
        }, JsonOptions);
        foreach (string root in Roots)
            WriteIfDifferent(Path.Combine(root, RootsName), contents);
    }

    private static void ValidateCommit(string stampPath, ProducerStamp stamp)
    {
        EnsureSafePath(stamp.CommitPath);
        ProducerCommit commit = ReadCommit(stamp.CommitPath);
        if (!UhtProducerProtocol.MatchesCommitMetadata(stampPath, stamp, commit) ||
            !UhtProducerProtocol.MatchesMemberHash(commit.Stamps[stampPath], HashFile(stampPath)))
            throw new InvalidDataException("Incomplete UnrealSharp producer transaction.");
        foreach (var entry in commit.Stamps)
        {
            ValidateRoot(Path.GetDirectoryName(entry.Key)!);
            if (Path.GetFileName(entry.Key) != StampName || !File.Exists(entry.Key) ||
                !UhtProducerProtocol.MatchesMemberHash(entry.Value, HashFile(entry.Key)))
                throw new InvalidDataException("Incomplete UnrealSharp producer transaction: " + entry.Key);
        }
    }

    private static ProducerStamp ReadStamp(string path)
    {
        EnsureSafePath(path);
        ProducerStamp stamp = JsonSerializer.Deserialize<ProducerStamp>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Invalid UnrealSharp producer stamp: " + path);
        if (stamp.Version != 1)
            throw new InvalidDataException("Unsupported UnrealSharp producer stamp: " + path);
        return stamp;
    }

    private static ProducerCommit ReadCommit(string path)
    {
        if (Path.GetFileName(path) != CommitName)
            throw new InvalidDataException("Invalid UnrealSharp producer commit path: " + path);
        EnsureSafePath(path);
        return JsonSerializer.Deserialize<ProducerCommit>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Missing UnrealSharp producer commit: " + path);
    }

    private static void ValidateFiles(string root, SortedDictionary<string, string> expected)
    {
        SortedDictionary<string, string> actual = Inventory(root);
        if (!UhtProducerProtocol.MatchesInventory(actual, expected))
            throw new InvalidDataException("UnrealSharp generated output differs from its successful producer: " + root);
    }

    private static SortedDictionary<string, string> Inventory(string root)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in GetOwnedFiles(root))
            result.Add(Path.GetRelativePath(root, path).Replace('\\', '/'), HashFile(path));
        return result;
    }

    private static IEnumerable<string> GetOwnedFiles(string root)
    {
        AuthorizeRoot(root);
        if (!Directory.Exists(root))
            yield break;
        var owned = new HashSet<string>(PathComparer);
        foreach (string path in EnumerateFiles(root))
        {
            if (path.EndsWith(".generated.cs", StringComparison.Ordinal) || PathComparer.Equals(path, Path.Combine(root, "UE5Rules.Defines.props")))
                owned.Add(path);
            else if (path.EndsWith(".csproj", StringComparison.Ordinal) && IsGeneratedProject(path))
            {
                owned.Add(path);
            }
        }
        // Never remove arbitrary authored files from a generated output tree.
        foreach (string path in EnumerateFiles(root))
        {
            if ((path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".csproj", StringComparison.Ordinal)) && !owned.Contains(path))
                throw new InvalidDataException("Unrecognized source in UnrealSharp generated output; preserved: " + path);
        }
        foreach (string path in owned.OrderBy(path => path, StringComparer.Ordinal))
            yield return path;
    }

    private static bool IsGeneratedProject(string path)
    {
        if (Path.GetFileNameWithoutExtension(path) != Path.GetFileName(Path.GetDirectoryName(path)))
            return false;
        XDocument document = XDocument.Load(path);
        return document.Descendants("SkipIncludeAnalyzers").Any(value => value.Value == "true") &&
            document.Descendants("Import").Any(import => ((string?)import.Attribute("Project"))?.Contains("UnrealSharp", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static IEnumerable<string> EnumerateFiles(string directory)
    {
        EnsureSafePath(directory);
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            EnsureSafePath(path);
            if (Directory.Exists(path))
            {
                // MSBuild's own products are not generator-owned source files.
                if (Path.GetFileName(path) is "bin" or "obj")
                    continue;
                foreach (string child in EnumerateFiles(path))
                    yield return child;
            }
            else
                yield return Path.GetFullPath(path);
        }
    }

    private static void ValidateRoot(string root)
    {
        string suffix = Path.Combine("Intermediate", "UnrealSharp", "UHT", GeneratorStatics.TargetType.ToString());
        if (!Path.GetFullPath(root).EndsWith(Path.DirectorySeparatorChar + suffix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Invalid UnrealSharp generated root: " + root);
        EnsureSafePath(root);
    }

    private static void AuthorizeRoot(string root)
    {
        ValidateRoot(root);
        string full = Path.GetFullPath(root);
        bool IsUnder(string owner) => !string.IsNullOrEmpty(owner) && full.StartsWith(
            owner.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        if (!CurrentRoots.Contains(full) && !IsUnder(_projectDirectory) && !IsUnder(_pluginDirectory))
            throw new InvalidDataException("Unverified previous UnrealSharp generated root; preserved: " + root);
    }

    private static void CaptureCurrentPaths(IEnumerable<string> paths)
    {
        foreach (string path in paths.Select(Path.GetFullPath).Distinct(PathComparer).OrderBy(path => path, PathComparer))
        {
            ResolvedPaths.Add(path, ResolvePhysicalPath(path, captureAncestors: true));
        }
        foreach (var entry in ResolvedPaths)
            if (!PathComparer.Equals(ResolvePhysicalPath(entry.Key), entry.Value))
                throw new InvalidDataException("UnrealSharp output identity changed while capturing current paths: " + entry.Key);
    }

    private static string ResolvePhysicalPath(string path, bool captureAncestors = false, bool allowFinalLink = false, HashSet<string>? chain = null,
        Dictionary<string, string>? prefixes = null)
    {
        string full = Path.GetFullPath(path);
        string current = Path.GetPathRoot(full)!;
        string logical = current;
        string[] parts = full[current.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        chain ??= new HashSet<string>(PathComparer);
        for (int index = 0; index < parts.Length; index++)
        {
            current = Path.Combine(current, parts[index]);
            logical = Path.Combine(logical, parts[index]);
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { attributes = 0; }
            catch (DirectoryNotFoundException) { attributes = 0; }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                var info = new DirectoryInfo(current);
                string raw = info.LinkTarget ?? throw new InvalidDataException("Unsupported UnrealSharp output reparse point: " + current);
                string target = info.ResolveLinkTarget(returnFinalTarget: false)?.FullName
                    ?? throw new InvalidDataException("Cannot resolve UnrealSharp output link: " + current);
                bool mayCapture = captureAncestors && (allowFinalLink || index != parts.Length - 1);
                AncestorLinks.TryGetValue(current, out ProducerLink? expected);
                if (!mayCapture && expected == null)
                    throw new InvalidDataException("Unverified reparse point in UnrealSharp generated output: " + current);
                if (!chain.Add(current))
                    throw new InvalidDataException("Cyclic UnrealSharp output link: " + current);
                string physical;
                try { physical = ResolvePhysicalPath(target, mayCapture, allowFinalLink: true, chain); }
                finally { chain.Remove(current); }
                if (expected != null && (raw != expected.RawTarget || !PathComparer.Equals(target, expected.Target) ||
                    !PathComparer.Equals(physical, expected.PhysicalTarget)))
                    throw new InvalidDataException("UnrealSharp output link changed during export: " + current);
                if (expected == null)
                    AncestorLinks.Add(current, new ProducerLink { RawTarget = raw, Target = target, PhysicalTarget = physical });
                current = physical;
            }
            if (prefixes != null)
                prefixes[logical] = current;
        }
        return current;
    }

    internal static void EnsureSafePath(string path)
    {
        string full = Path.GetFullPath(path);
        // One fresh traversal checks the entire chain and records the physical
        // identity of every logical prefix. Do not resolve each matching root
        // again: the same traversal has already checked those ancestors.
        var prefixes = new Dictionary<string, string>(PathComparer);
        ResolvePhysicalPath(full, prefixes: prefixes);
        foreach (var entry in ResolvedPaths)
        {
            if ((PathComparer.Equals(full, entry.Key) || full.StartsWith(entry.Key + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) &&
                (!prefixes.TryGetValue(entry.Key, out string? physical) || !PathComparer.Equals(physical, entry.Value)))
                throw new InvalidDataException("UnrealSharp current output identity changed: " + entry.Key);
        }
        foreach (var entry in LockPaths)
        {
            if ((PathComparer.Equals(full, entry.Key) || full.StartsWith(entry.Key + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) &&
                (!prefixes.TryGetValue(entry.Key, out string? physical) || !PathComparer.Equals(physical, entry.Value)))
                throw new InvalidDataException("UnrealSharp locked output identity changed: " + entry.Key);
        }
    }

    internal static void WriteIfDifferent(string path, string contents)
    {
        EnsureSafePath(path);
        if (File.Exists(path) && File.ReadAllText(path) == contents)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        EnsureSafePath(path);
        string temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            EnsureSafePath(temporary);
            File.WriteAllText(temporary, contents);
            EnsureSafePath(path);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            EnsureSafePath(temporary);
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static string HashFile(string path)
    {
        EnsureSafePath(path);
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string HashText(string value) => UhtProducerProtocol.HashText(value);
}
