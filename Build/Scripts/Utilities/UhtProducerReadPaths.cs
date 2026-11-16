using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UnrealSharp.Automation.Utilities;

// Per-read identity only. Current request/manifest paths may establish ancestor
// links; historical metadata never grants permission to traverse another link.
internal sealed class UhtProducerReadPaths
{
    internal static readonly StringComparer Comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly Dictionary<string, string> _roots = new(Comparer);
    private readonly Dictionary<string, UhtProducerLink> _links = new(Comparer);

    internal void Capture(IEnumerable<string> paths)
    {
        foreach (string path in paths.Select(Path.GetFullPath).Distinct(Comparer).OrderBy(path => path, Comparer))
        {
            string physical = Resolve(path, capture: true);
            if (_roots.TryGetValue(path, out string? previous) && !Comparer.Equals(previous, physical))
                throw new InvalidDataException("Producer path changed between capture phases: " + path);
            _roots.TryAdd(path, physical);
        }
        VerifyAll();
    }

    internal string Track(string path)
    {
        string full = Path.GetFullPath(path);
        string physical = Ensure(full);
        _roots.TryAdd(full, physical);
        return physical;
    }

    internal string Ensure(string path)
    {
        string full = Path.GetFullPath(path);
        var prefixes = new Dictionary<string, string>(Comparer);
        string physical = Resolve(full, prefixes: prefixes);
        foreach (var root in _roots)
        {
            if (IsUnder(full, root.Key) && (!prefixes.TryGetValue(root.Key, out string? value) || !Comparer.Equals(value, root.Value)))
                throw new InvalidDataException("UnrealSharp producer input path changed: " + root.Key);
        }
        return physical;
    }

    internal void VerifyAll()
    {
        foreach (var root in _roots)
            if (!Comparer.Equals(Resolve(root.Key), root.Value))
                throw new InvalidDataException("UnrealSharp producer input identity changed: " + root.Key);
    }

    internal bool MatchesPublished(SortedDictionary<string, string> paths, SortedDictionary<string, UhtProducerLink> links)
    {
        if (paths.Count != _roots.Count || links.Count != _links.Count)
            return false;
        return paths.All(entry => _roots.TryGetValue(entry.Key, out string? value) && Comparer.Equals(value, entry.Value)) &&
            links.All(entry => _links.TryGetValue(entry.Key, out UhtProducerLink? value) &&
                value.RawTarget == entry.Value.RawTarget && Comparer.Equals(value.Target, entry.Value.Target) &&
                Comparer.Equals(value.PhysicalTarget, entry.Value.PhysicalTarget));
    }

    internal static bool IsUnder(string path, string parent) => Comparer.Equals(path, parent) || path.StartsWith(
        parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private string Resolve(string path, bool capture = false, bool allowFinalLink = false, HashSet<string>? chain = null,
        Dictionary<string, string>? prefixes = null)
    {
        string full = Path.GetFullPath(path);
        string current = Path.GetPathRoot(full)!;
        string logical = current;
        string[] parts = full[current.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        chain ??= new HashSet<string>(Comparer);
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
                string raw = info.LinkTarget ?? throw new InvalidDataException("Unsupported producer input link: " + current);
                string target = info.ResolveLinkTarget(returnFinalTarget: false)?.FullName
                    ?? throw new InvalidDataException("Cannot resolve producer input link: " + current);
                bool mayCapture = capture && (allowFinalLink || index != parts.Length - 1);
                _links.TryGetValue(current, out UhtProducerLink? expected);
                if (!mayCapture && expected == null)
                    throw new InvalidDataException("Unverified link in producer input: " + current);
                if (!chain.Add(current))
                    throw new InvalidDataException("Cyclic producer input link: " + current);
                string physical;
                try { physical = Resolve(target, mayCapture, allowFinalLink: true, chain); }
                finally { chain.Remove(current); }
                if (expected != null && (raw != expected.RawTarget || !Comparer.Equals(target, expected.Target) || !Comparer.Equals(physical, expected.PhysicalTarget)))
                    throw new InvalidDataException("Producer input link changed: " + current);
                _links.TryAdd(current, new UhtProducerLink { RawTarget = raw, Target = target, PhysicalTarget = physical });
                current = physical;
            }
            if (prefixes != null)
                prefixes[logical] = current;
        }
        return current;
    }
}
