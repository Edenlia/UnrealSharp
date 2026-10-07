using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace UnrealSharp.Automation.Utilities;

internal static class GlueSolutionCache
{
    // Version 1 was an unversioned array and could describe a different solution.
    private const int CurrentVersion = 2;
    private const string StampFileName = "UnrealSharpSolutionStamp.json";

    private sealed class Stamp
    {
        public int Version { get; set; }
        public string[]? Projects { get; set; }
        public string? SolutionHash { get; set; }
    }

    public static bool IsCurrent(string solutionPath, IEnumerable<string> projectPaths)
    {
        string StampPath = GetStampPath(solutionPath);
        if (!File.Exists(StampPath) || !File.Exists(solutionPath))
        {
            return false;
        }

        try
        {
            Stamp? SavedStamp = JsonSerializer.Deserialize<Stamp>(File.ReadAllText(StampPath));
            if (SavedStamp == null || SavedStamp.Version != CurrentVersion ||
                SavedStamp.Projects == null || SavedStamp.Projects.Any(string.IsNullOrWhiteSpace) ||
                string.IsNullOrEmpty(SavedStamp.SolutionHash))
            {
                return false;
            }

            HashSet<string> CurrentProjects = NormalizePaths(solutionPath, projectPaths);
            HashSet<string> SavedProjects = NormalizePaths(solutionPath, SavedStamp.Projects);
            return CurrentProjects.SetEquals(SavedProjects) &&
                   string.Equals(SavedStamp.SolutionHash, GetSolutionHash(solutionPath), StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    public static void Save(string solutionPath, IEnumerable<string> projectPaths)
    {
        Stamp NewStamp = new Stamp
        {
            Version = CurrentVersion,
            Projects = NormalizePaths(solutionPath, projectPaths).OrderBy(path => path, StringComparer.Ordinal).ToArray(),
            SolutionHash = GetSolutionHash(solutionPath),
        };

        File.WriteAllText(GetStampPath(solutionPath), JsonSerializer.Serialize(NewStamp));
    }

    public static void Invalidate(string solutionPath)
    {
        string StampPath = GetStampPath(solutionPath);
        if (File.Exists(StampPath))
        {
            File.Delete(StampPath);
        }
    }

    private static HashSet<string> NormalizePaths(string solutionPath, IEnumerable<string> projectPaths)
    {
        string SolutionDirectory = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
        StringComparer Comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        return new HashSet<string>(projectPaths.Select(path => Path.GetFullPath(path, SolutionDirectory)), Comparer);
    }

    private static string GetStampPath(string solutionPath) =>
        Path.Combine(Path.GetDirectoryName(solutionPath)!, StampFileName);

    private static string GetSolutionHash(string solutionPath)
    {
        using FileStream Stream = File.OpenRead(solutionPath);
        return Convert.ToHexString(SHA256.HashData(Stream));
    }
}
