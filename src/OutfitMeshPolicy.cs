using System.IO;
using System.Text.RegularExpressions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;

namespace WearableDistributor;

public sealed record WornModelPaths(string Male, string Female, bool MaleWeights, bool FemaleWeights);

/// <summary>Checks visible loose files and archive directories, without extracting or altering game assets.</summary>
public sealed class MeshAssetIndex
{
    private readonly string root;
    private readonly HashSet<string> archived = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (long Length, long Modified)> archiveVersions = new(StringComparer.OrdinalIgnoreCase);
    private bool complete;
    public bool CanConfirmMissing { get; }
    public MeshAssetIndex(string dataRoot, bool canConfirmMissing = true)
    {
        root = Path.GetFullPath(dataRoot);
        CanConfirmMissing = canConfirmMissing;
    }

    public static string? Normalize(string path)
    {
        var value = (path ?? "").Trim().Replace('/', '\\');
        if (value.StartsWith("meshes\\", StringComparison.OrdinalIgnoreCase)) value = value[7..];
        if (value.Length == 0 || Path.IsPathRooted(value) || value.Contains(':') || value.Split('\\').Any(part => part is ".." or "." or "")) return null;
        return "meshes\\" + value;
    }

    public void RefreshArchives(CancellationToken cancellationToken = default)
    {
        var versions = new Dictionary<string, (long, long)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var path in Directory.EnumerateFiles(root, "*.bsa", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = new FileInfo(path); versions[path] = (info.Length, info.LastWriteTimeUtc.Ticks);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { complete = false; return; }
        if (complete && versions.Count == archiveVersions.Count && versions.All(pair => archiveVersions.TryGetValue(pair.Key, out var value) && value == pair.Value)) return;
        archived.Clear(); archiveVersions.Clear(); complete = true;
        foreach (var pair in versions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var reader = Archive.CreateReader(GameRelease.SkyrimSE, new Noggog.FilePath(pair.Key));
                try
                {
                    foreach (var file in reader.Files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (file.Size > 0 && file.Path.EndsWith(".nif", StringComparison.OrdinalIgnoreCase) && Normalize(file.Path) is { } path) archived.Add(path);
                    }
                }
                finally { (reader as IDisposable)?.Dispose(); }
                archiveVersions[pair.Key] = pair.Value;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { complete = false; App.WriteDiagnostic(DiagnosticEvent.MeshArchiveReadFailed, ex, DiagnosticLevel.Warning); }
        }
    }

    public string Status(string path)
    {
        var relative = Normalize(path);
        if (relative is null) return "Invalid";
        try
        {
            var full = Path.Combine(root, relative);
            if (File.Exists(full)) return new FileInfo(full).Length > 0 ? "Loose" : "Empty";
            // Archives may be enabled through plugin names or custom INI settings. Finding a path
            // establishes a potential source; it does not prove archive activation or NIF integrity.
            if (archived.Contains(relative)) return "Archive";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "Unknown"; }
        return CanConfirmMissing && complete ? "Missing" : "Unknown";
    }

    public bool HasExtraMeshesCopy(string path)
    {
        if (Normalize(path) is not { } relative) return false;
        try
        {
            var wrong = Path.Combine(root, "meshes", relative);
            return File.Exists(wrong) && new FileInfo(wrong).Length > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}

public static class OutfitMeshPolicy
{
    public static IEnumerable<string> Paths(WearableRecord piece, string sex)
    {
        foreach (var model in piece.WornModels)
        {
            var path = sex == "Female" ? model.Female : model.Male;
            if (string.IsNullOrWhiteSpace(path)) continue;
            yield return path;
            if ((sex == "Female" ? model.FemaleWeights : model.MaleWeights) && Regex.IsMatch(path, @"_[01]\.nif$", RegexOptions.IgnoreCase))
                yield return Regex.Replace(path, @"_[01]\.nif$", path.EndsWith("_0.nif", StringComparison.OrdinalIgnoreCase) ? "_1.nif" : "_0.nif", RegexOptions.IgnoreCase);
        }
    }

    public static List<string> Missing(WearableRecord piece, MeshAssetIndex assets, string gender)
        => (gender == "Any" ? new[] { "Female", "Male" } : new[] { gender })
            .SelectMany(sex => Paths(piece, sex).Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => assets.Status(path) is "Missing" or "Empty" or "Invalid").Select(path => sex + ": " + path))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static List<string> Errors(PluginPatchOptions config, bool refresh = false)
    {
        if (config.Source.MeshAssets is not { } assets) return [];
        if (refresh) assets.RefreshArchives();
        // Shop buyers are not restricted by the NPC audience's sex choice.
        var checkedGender = config.MerchantCategories.Count > 0 || !config.DistributeToNpcs ? "Any" : config.Gender;
        var affected = config.Pieces.Where(p => p.Include).Select(p => Missing(p, assets, checkedGender)).Where(paths => paths.Count > 0).ToList();
        if (affected.Count == 0) return [];
        var misplaced = affected.Count(paths => paths.Any(path => assets.HasExtraMeshesCopy(path[(path.IndexOf(": ", StringComparison.Ordinal) + 2)..])));
        return [$"Worn meshes unavailable for {affected.Count} selected part(s). " + (misplaced > 0
            ? $"Built files for {misplaced} part(s) were found under meshes\\meshes. In BodySlide, choose the mod/Data root as Output Path, deploy that output, then rescan."
            : "Build or install the outfit's required meshes, deploy/enable that output in your mod manager, then rescan.")];
    }

    public static List<string> Details(PluginPatchOptions config)
    {
        if (config.Source.MeshAssets is not { } assets) return [];
        var gender = config.MerchantCategories.Count > 0 || !config.DistributeToNpcs ? "Any" : config.Gender;
        return config.Pieces.Where(p => p.Include).Select(piece => (piece, paths: Missing(piece, assets, gender))).Where(item => item.paths.Count > 0)
            .Select(item => item.piece.Name + "\n" + string.Join("\n", item.paths.Select(path =>
            {
                var raw = path[(path.IndexOf(": ", StringComparison.Ordinal) + 2)..];
                return "Expected: " + path + (assets.HasExtraMeshesCopy(raw) ? "\nBuilt copy found: meshes\\" + MeshAssetIndex.Normalize(raw) + " (extra meshes folder)" : "");
            }))).ToList();
    }

    public static string Summary(WearableRecord piece, MeshAssetIndex assets)
    {
        var missing = Missing(piece, assets, "Any");
        return missing.Count > 0 ? "Missing mesh files · " + string.Join(" / ", missing.Select(path => path.Split(':')[0]).Distinct())
            : piece.WornModels.Count == 0 ? "Worn mesh references unresolved"
            : "Mesh files found";
    }
}
