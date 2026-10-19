using System.IO;
using System.Text.RegularExpressions;

namespace WearableDistributor;

/// <summary>Resolve the game Data independently of a mod staging/profile folder.</summary>
public static class GameDataLocator
{
    public static string? Find(string scanRoot, string profileList, string? fallback = null)
    {
        if (IsGameData(scanRoot)) return scanRoot;
        if (!string.IsNullOrWhiteSpace(scanRoot) && IsGameData(Path.Combine(scanRoot, "Data"))) return Path.Combine(scanRoot, "Data");
        var candidates = new List<string>();
        foreach (var origin in new[] { scanRoot, Path.GetDirectoryName(profileList), Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            if (string.IsNullOrWhiteSpace(origin)) continue;
            try
            {
                var directory = new DirectoryInfo(Path.GetFullPath(origin));
                for (var depth = 0; directory != null && depth < 6; depth++, directory = directory.Parent)
                {
                    candidates.Add(directory.FullName);
                    candidates.Add(Path.Combine(directory.FullName, "Data"));
                    var ini = Path.Combine(directory.FullName, "ModOrganizer.ini");
                    if (!File.Exists(ini)) continue;
                    // MO2 stores paths as Qt @ByteArray values with doubled backslashes.
                    foreach (var line in File.ReadLines(ini))
                    {
                        var match = Regex.Match(line, @"^\s*gamePath\s*=\s*(.*)$", RegexOptions.IgnoreCase);
                        if (!match.Success) continue;
                        var value = match.Groups[1].Value.Trim();
                        if (value.StartsWith("@ByteArray(", StringComparison.Ordinal) && value.EndsWith(')')) value = value[11..^1];
                        value = value.Trim('"').Replace("\\\\", "\\");
                        if (value.Length == 0 || value.Contains('@') || value.Contains('%')) continue;
                        if (!Path.IsPathRooted(value)) value = Path.GetFullPath(Path.Combine(directory.FullName, value));
                        candidates.Add(Path.Combine(value, "Data"));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        }
        if (!string.IsNullOrWhiteSpace(fallback)) candidates.Add(fallback);
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(IsGameData);
    }

    public static bool IsGameData(string path) => Directory.Exists(path) && File.Exists(Path.Combine(path, "Skyrim.esm"));
}
