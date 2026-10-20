using System.IO;
using System.Text.RegularExpressions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;

namespace WearableDistributor;

public sealed record PatchEntryHealth(string Status, string Explanation);

public static class PatchHealthService
{
    public static IReadOnlyDictionary<PatchManifestEntry, PatchEntryHealth> Inspect(string outputDirectory, PatchManifest manifest,
        IReadOnlyList<PluginEntry>? scannedPlugins = null)
    {
        var result = new Dictionary<PatchManifestEntry, PatchEntryHealth>();
        var pluginPath = PatchOutputTransaction.ManagedPath(outputDirectory, PatchGenerator.OutputPluginName);
        var outfitIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outfitIdsByEditorId = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        string? pluginIssue = null;
        if (!File.Exists(pluginPath)) pluginIssue = "The generated ESP is missing. Restore a backup or regenerate the patch.";
        else
        {
            try
            {
                using var mod = SkyrimMod.CreateFromBinaryOverlay(new Noggog.FilePath(pluginPath), SkyrimRelease.SkyrimSE);
                OptionalPartLists.Validate(mod, manifest);
                outfitIds.UnionWith(mod.Outfits.Select(outfit => outfit.EditorID).OfType<string>());
                foreach (var outfit in mod.Outfits.Where(outfit => !string.IsNullOrWhiteSpace(outfit.EditorID)))
                    outfitIdsByEditorId[outfit.EditorID!] = outfit.FormKey.ID;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or InvalidOperationException)
            { App.WriteDiagnostic(DiagnosticEvent.PatchHealthFailed, ex, DiagnosticLevel.Warning); pluginIssue = "The generated ESP could not be read: " + ex.Message; }
        }
        var scanned = scannedPlugins?.ToDictionary(plugin => plugin.Plugin, StringComparer.OrdinalIgnoreCase);
        string? merchandiseIssue = null;
        try { MerchantStockService.Validate(outputDirectory, manifest); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or ArgumentException) { App.WriteDiagnostic(DiagnosticEvent.PatchHealthFailed, ex, DiagnosticLevel.Warning); merchandiseIssue = ex.Message; }
        var distributionFiles = new Dictionary<string, GeneratedDistributionIndex>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifest.Entries)
        {
            PatchEntryHealth health;
            if (pluginIssue != null) health = new("Missing / unreadable ESP", pluginIssue);
            else if (merchandiseIssue is not null) health = new("Merchant rules need repair", merchandiseIssue);
            else if (!File.Exists(PatchOutputTransaction.ManagedPath(outputDirectory, entry.DistributionPath)))
                health = new("Missing INI", "The outfit is recorded in the inventory but its distribution file is missing. It will not distribute until restored or regenerated.");
            else if (string.IsNullOrWhiteSpace(entry.OutfitEditorId))
                health = new("Legacy details", "This older patch has incomplete inventory details. Repatch the source plugin to refresh its record links.");
            else if (!outfitIds.Contains(entry.OutfitEditorId))
                health = new("Missing outfit", "This inventory entry no longer has a matching outfit record in the ESP. Restore or repatch it.");
            else if (DistributionIssue(outputDirectory, entry, outfitIdsByEditorId, distributionFiles) is { } distributionIssue)
                health = distributionIssue;
            else if (scanned is not null && !scanned.ContainsKey(entry.SourcePlugin))
                health = new("Source not scanned", "The generated files exist, but the source outfit plugin is not in the current scan. Check the active profile and scan folder before using this output.");
            else if (scanned is not null && scanned[entry.SourcePlugin] is { CanPatch: false } invalid)
                health = new("Invalid source", invalid.ValidationIssue);
            else if (scanned is not null && HasRemovedPart(entry, scanned[entry.SourcePlugin]))
                health = new("Parts changed", "One or more selected armor records are absent from the current source scan. Repatch this outfit after checking the mod version.");
            else if (scanned is not null && EquipmentIssue(entry, scanned[entry.SourcePlugin]) is { } equipmentIssue)
                health = equipmentIssue;
            else health = new("Ready", "The generated ESP, distribution file and outfit record are present." +
                (scanned is null ? " Source plugins have not been compared with a current scan." : " Included parts match the current source scan.") +
                " This is a file-health check, not an in-game distribution test.");
            result[entry] = health;
            if (health.Status is "Missing / unreadable ESP" or "Missing INI" or "Missing outfit")
                App.WriteDiagnostic(DiagnosticEvent.PatchHealthFailed, level: DiagnosticLevel.Warning);
        }
        return result;
    }


    private static PatchEntryHealth? EquipmentIssue(PatchManifestEntry entry, PluginEntry plugin)
    {
        var selectedIds = entry.IncludedPieces.Select(text => Regex.Match(text, @"\[(0x[0-9a-f]+~[^\]]+)\]", RegexOptions.IgnoreCase))
            .Where(match => match.Success).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = plugin.Pieces.Where(piece => selectedIds.Contains(piece.FormId)).ToList();
        var checkedGender = entry.MerchantStock.Count > 0 || !entry.DistributeToNpcs ? "Any" : entry.Gender;
        if (plugin.MeshAssets is { } assets && selected.Any(piece => OutfitMeshPolicy.Missing(piece, assets, checkedGender).Count > 0))
            return new("Missing worn meshes", "Selected outfit parts refer to unavailable worn meshes. Build/deploy BodySlide output or install the outfit meshes, then rescan and repatch. Changing only the NPC sex cannot fix absent female meshes.");
        if (entry.DistributeToNpcs && !entry.MatchVariants)
            for (var i = 0; i < selected.Count; i++) for (var j = i + 1; j < selected.Count; j++)
                if (OutfitPartSelection.BodySlots(selected[i].Slots).Overlaps(OutfitPartSelection.BodySlots(selected[j].Slots)))
                    return new("Conflicting outfit parts", "This older outfit combines alternatives that occupy the same body slots. Edit it and choose Reset to Balanced or a matching color, review the selected parts, then regenerate.");
        return null;
    }

    private static PatchEntryHealth? DistributionIssue(string directory, PatchManifestEntry entry, IReadOnlyDictionary<string, uint> outfitIds, Dictionary<string, GeneratedDistributionIndex> distributionFiles)
    {
        try
        {
            if (!distributionFiles.TryGetValue(entry.DistributionPath, out var index))
                distributionFiles[entry.DistributionPath] = index = new GeneratedDistributionIndex(PatchOutputTransaction.ManagedPath(directory, entry.DistributionPath));
            PatchGenerator.ValidateInventoryDistribution(directory, entry, outfitIds, index); return null;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or ArgumentException)
        { App.WriteDiagnostic(DiagnosticEvent.PatchHealthFailed, ex, DiagnosticLevel.Warning); return new("Rules need repair", ex.Message); }
    }

    private static bool HasRemovedPart(PatchManifestEntry entry, PluginEntry source)
    {
        var current = source.Pieces.Concat(source.Weapons).Select(piece => piece.FormId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return entry.IncludedPieces.Select(piece => Regex.Match(piece, @"\[(0x[0-9a-f]+~[^\]]+)\]$", RegexOptions.IgnoreCase))
            .Where(match => match.Success).Any(match => !current.Contains(match.Groups[1].Value)) || entry.MerchantStock.Any(rule =>
                rule.ItemPlugin.Equals(source.Plugin, StringComparison.OrdinalIgnoreCase) && !current.Contains($"0x{rule.ItemId:X6}~{rule.ItemPlugin}"));
    }
}
