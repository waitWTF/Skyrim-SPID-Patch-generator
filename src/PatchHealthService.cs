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
                outfitIds.UnionWith(mod.Outfits.Select(outfit => outfit.EditorID).OfType<string>());
                foreach (var outfit in mod.Outfits.Where(outfit => !string.IsNullOrWhiteSpace(outfit.EditorID)))
                    outfitIdsByEditorId[outfit.EditorID!] = outfit.FormKey.ID;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
            { pluginIssue = "The generated ESP could not be read: " + ex.Message; }
        }
        var scanned = scannedPlugins?.ToDictionary(plugin => plugin.Plugin, StringComparer.OrdinalIgnoreCase);
        string? merchandiseIssue = null;
        try { MerchantStockService.Validate(outputDirectory, manifest); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException) { merchandiseIssue = ex.Message; }
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
            else if (DistributionIssue(outputDirectory, entry, outfitIdsByEditorId) is { } distributionIssue)
                health = distributionIssue;
            else if (scanned is not null && !scanned.ContainsKey(entry.SourcePlugin))
                health = new("Source not scanned", "The generated files exist, but the source outfit plugin is not in the current scan. Check the active profile and scan folder before using this output.");
            else if (scanned is not null && scanned[entry.SourcePlugin] is { CanPatch: false } invalid)
                health = new("Invalid source", invalid.ValidationIssue);
            else if (scanned is not null && HasRemovedPart(entry, scanned[entry.SourcePlugin]))
                health = new("Parts changed", "One or more selected armor records are absent from the current source scan. Repatch this outfit after checking the mod version.");
            else health = new("Ready", "The generated ESP, distribution file and outfit record are present." +
                (scanned is null ? " Source plugins have not been compared with a current scan." : " Included parts match the current source scan.") +
                " This is a file-health check, not an in-game distribution test.");
            result[entry] = health;
        }
        return result;
    }

    private static PatchEntryHealth? DistributionIssue(string directory, PatchManifestEntry entry, IReadOnlyDictionary<string, uint> outfitIds)
    {
        try { PatchGenerator.ValidateInventoryDistribution(directory, entry, outfitIds); return null; }
        catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or ArgumentException)
        { return new("Rules need repair", ex.Message); }
    }

    private static bool HasRemovedPart(PatchManifestEntry entry, PluginEntry source)
    {
        var current = source.Pieces.Concat(source.Weapons).Select(piece => piece.FormId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return entry.IncludedPieces.Select(piece => Regex.Match(piece, @"\[(0x[0-9a-f]+~[^\]]+)\]$", RegexOptions.IgnoreCase))
            .Where(match => match.Success).Any(match => !current.Contains(match.Groups[1].Value)) || entry.MerchantStock.Any(rule =>
                rule.ItemPlugin.Equals(source.Plugin, StringComparison.OrdinalIgnoreCase) && !current.Contains($"0x{rule.ItemId:X6}~{rule.ItemPlugin}"));
    }
}
