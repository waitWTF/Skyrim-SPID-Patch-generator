using System.Diagnostics;
using System.IO;

namespace WearableDistributor;

public sealed record ScanLocation(string Label, string DataPath, string ProfilePath, string Explanation, bool Usable, bool IsInjectedMo2 = false, string ModLibraryPath = "")
{
    public override string ToString() => Label;
}

/// <summary>Bounded discovery of MO2 instances and game folders; never scans every drive.</summary>
public static class ScanLocationDiscovery
{
    public static bool IsMo2Attached() => InjectedDirectory() is not null;

    private static string? InjectedDirectory()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return process.Modules.Cast<ProcessModule>()
                .Where(module => module.ModuleName.Equals("usvfs_x64.dll", StringComparison.OrdinalIgnoreCase) || module.ModuleName.Equals("usvfs_x86.dll", StringComparison.OrdinalIgnoreCase))
                .Select(module => Path.GetDirectoryName(module.FileName)).FirstOrDefault();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { App.WriteDiagnostic(DiagnosticEvent.Mo2ModuleProbeFailed, ex, DiagnosticLevel.Warning); return null; }
    }

    public static List<ScanLocation> Discover(AppPreferences preferences, string? installedGameData)
    {
        var locations = new List<ScanLocation>();
        var configs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var injectedDirectory = InjectedDirectory();
        foreach (var origin in new[] { injectedDirectory, Path.GetDirectoryName(preferences.PluginListPath), preferences.DataPath, Environment.CurrentDirectory, Path.GetDirectoryName(Environment.ProcessPath), AppContext.BaseDirectory })
        {
            if (string.IsNullOrWhiteSpace(origin)) continue;
            try
            {
                var parent = new DirectoryInfo(Path.GetFullPath(origin));
                for (var depth = 0; parent != null && depth < 6; depth++, parent = parent.Parent)
                {
                    var config = Path.Combine(parent.FullName, "ModOrganizer.ini");
                    if (File.Exists(config)) configs.Add(config);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { App.WriteDiagnostic(DiagnosticEvent.Mo2ConfigProbeFailed, ex, DiagnosticLevel.Warning); }
        }
        var instanceRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModOrganizer");
        try
        {
            if (Directory.Exists(instanceRoot))
                foreach (var directory in Directory.EnumerateDirectories(instanceRoot).Take(100))
                {
                    var config = Path.Combine(directory, "ModOrganizer.ini");
                    if (File.Exists(config)) configs.Add(config);
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.WriteDiagnostic(DiagnosticEvent.Mo2ConfigProbeFailed, ex, DiagnosticLevel.Warning); }

        foreach (var config in configs)
        {
            try
            {
                var values = ReadIni(config);
                if (values.GetValueOrDefault("General/gameName") is { Length: > 0 } game && !game.Equals("Skyrim Special Edition", StringComparison.OrdinalIgnoreCase)) continue;
                var directory = Path.GetDirectoryName(config)!;
                var gamePath = Resolve(values.GetValueOrDefault("General/gamePath", ""), directory, directory);
                if (gamePath.Length == 0) continue;
                var data = Path.Combine(gamePath, "Data");
                if (!GameDataLocator.IsGameData(data)) continue;
                var basePath = Resolve(values.GetValueOrDefault("Settings/base_directory", directory), directory, directory);
                var profiles = Resolve(values.GetValueOrDefault("Settings/profiles_directory", Path.Combine(basePath, "profiles")), directory, basePath);
                var mods = Resolve(values.GetValueOrDefault("Settings/mod_directory", Path.Combine(basePath, "mods")), directory, basePath);
                var profileName = values.GetValueOrDefault("General/selected_profile", "");
                var profile = "";
                // Profile names are directory names, never arbitrary paths from an INI.
                if (profileName.Length > 0 && profileName == Path.GetFileName(profileName) && profileName is not "." and not "..")
                {
                    var list = Path.Combine(profiles, profileName, "plugins.txt");
                    if (File.Exists(list)) profile = list;
                }
                var attached = injectedDirectory is not null;
                var localInjection = attached && directory.Equals(injectedDirectory, StringComparison.OrdinalIgnoreCase);
                var visibility = DescribeVisibility(data, profile);
                locations.Add(new ScanLocation($"MO2 · {Path.GetFileName(directory)} · {profileName}", data, profile,
                    $"Game Data: {data}\nMO2 configuration: {config}\nConfigured profile: {(profile.Length == 0 ? "not resolved; choose plugins.txt manually" : profile)}\n{visibility}\n" +
                    (attached ? "MO2's virtual filesystem is attached. Confirm this is the instance/profile used for this launch; command-line profile overrides may differ from the saved configuration." : "Launch this app through this MO2 instance to see its installed mods. A standalone scan cannot reproduce its virtual load order."),
                    attached, localInjection, mods));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { App.WriteDiagnostic(DiagnosticEvent.Mo2ConfigProbeFailed, ex, DiagnosticLevel.Warning); }
        }
        if (GameDataLocator.IsGameData(preferences.DataPath))
            locations.Add(new ScanLocation("Saved game Data folder", preferences.DataPath, preferences.PluginListPath, "Saved location. " + DescribeVisibility(preferences.DataPath, preferences.PluginListPath), true));
        if (installedGameData is not null && GameDataLocator.IsGameData(installedGameData))
            locations.Add(new ScanLocation("Installed Skyrim · Data", installedGameData, "", "Detected game installation. Suitable for deployed Vortex mods; launch through MO2 for virtual mods. " + DescribeVisibility(installedGameData, ""), true));
        foreach (var staging in FindVortexStagingRoots(preferences))
            locations.Add(new ScanLocation("Vortex · installed mods (staging)", staging, "",
                "Installed mod files: " + staging + "\nIncludes staged/disabled mods. Deployment and profile activation are not established; use deployed Data for the effective game load order.", true, ModLibraryPath: staging));
        return locations.OrderByDescending(location => location.IsInjectedMo2)
            .DistinctBy(location => (location.DataPath.ToUpperInvariant(), location.ProfilePath.ToUpperInvariant(), location.Usable)).ToList();
    }

    public static IReadOnlyList<string> FindVortexStagingRoots(AppPreferences preferences)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady))
            foreach (var relative in new[] { @"Vortex Mods\skyrimse", @"Vortex Mods\Skyrim Special Edition", @"Games\Vortex Mods\skyrimse" })
            {
                var candidate = Path.Combine(drive.RootDirectory.FullName, relative);
                if (Directory.Exists(candidate)) roots.Add(candidate);
            }
        if (!string.IsNullOrWhiteSpace(preferences.DataPath) && Directory.Exists(preferences.DataPath) && !GameDataLocator.IsGameData(preferences.DataPath)) roots.Add(preferences.DataPath);
        return roots.ToList();
    }

    private static string DescribeVisibility(string data, string profile)
    {
        if (!File.Exists(profile)) return "Skyrim.esm is visible. No profile list selected to compare mod visibility.";
        var names = WearableScanService.ReadProfilePluginOrder(profile).Take(1024).ToList();
        var visible = names.Count(name => File.Exists(Path.Combine(data, name)));
        return $"Skyrim.esm is visible. Profile plugin visibility: {visible}/{names.Count} enabled entries (up to 1024 checked). Missing entries require checking the instance/profile or deployment.";
    }

    private static Dictionary<string, string> ReadIni(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); var section = "General";
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1]; continue; }
            if (line.StartsWith(';') || line.StartsWith('#')) continue;
            var separator = line.IndexOf('='); if (separator <= 0) continue;
            var value = line[(separator + 1)..].Trim();
            if (value.StartsWith("@ByteArray(") && value.EndsWith(')')) value = value[11..^1];
            result[section + "/" + line[..separator].Trim()] = value.Trim('"').Replace("\\\\", "\\");
        }
        return result;
    }

    private static string Resolve(string value, string directory, string basePath)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        value = value.Replace("%BASE_DIR%", basePath, StringComparison.OrdinalIgnoreCase);
        value = Environment.ExpandEnvironmentVariables(value);
        if (value.Contains('%') || value.StartsWith('@')) return "";
        return Path.IsPathRooted(value) ? Path.GetFullPath(value) : Path.GetFullPath(Path.Combine(directory, value));
    }
}
