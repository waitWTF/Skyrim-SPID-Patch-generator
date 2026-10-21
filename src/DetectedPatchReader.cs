using System.Globalization;
using System.IO;
using System.Text.Json;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace WearableDistributor;

public sealed record DetectedPatchOutfit(string Directory, string PatchPlugin, PatchManifestEntry Entry, bool PresentInPlugin, bool CanManage, string Health, string Explanation);

/// <summary>Read generated files wherever discovered. Experimental and recovered files remain read-only.</summary>
public static class DetectedPatchReader
{
    private static readonly string[] Names = ["SkyrimSPIDPresetTest", "WearableDistributor", "SkyrimSPIDPatchGenerator"];
    public static bool IsGeneratedPlugin(string name) => Names.Any(prefix => name.Equals(prefix + ".esp", StringComparison.OrdinalIgnoreCase));
    public static bool IsGeneratedFile(string path) => Names.Any(prefix => new[] { ".esp", ".patch.json", "_DISTR.ini" }.Any(suffix => Path.GetFileName(path).Equals(prefix + suffix, StringComparison.OrdinalIgnoreCase)));
    public static IReadOnlyList<DetectedPatchOutfit> Read(IEnumerable<string> files, IReadOnlyList<PluginEntry>? scannedPlugins = null)
    {
        var result = new List<DetectedPatchOutfit>();
        foreach (var file in files.Where(IsGeneratedFile).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase)
                     .Select(path => (Root: Path.GetDirectoryName(path)!, Prefix: Names.First(name => Path.GetFileName(path).StartsWith(name + ".", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).Equals(name + "_DISTR.ini", StringComparison.OrdinalIgnoreCase))))
                     .DistinctBy(item => item.Root + "\0" + item.Prefix, StringComparer.OrdinalIgnoreCase))
        {
            try { ReadLibrary(file.Root, file.Prefix, result, scannedPlugins); }
            catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
            { App.WriteDiagnostic(DiagnosticEvent.PatchLibraryReadFailed, failure, DiagnosticLevel.Warning); }
        }
        return result;
    }
    private static void ReadLibrary(string root, string prefix, List<DetectedPatchOutfit> result, IReadOnlyList<PluginEntry>? scannedPlugins)
    {
        var pluginName = prefix + ".esp";
        var pluginFile = Path.Combine(root, pluginName);
        var manifestFile = Path.Combine(root, prefix + ".patch.json");
        PatchManifest? manifest = null;
        var metadataIncomplete = false;
        if (File.Exists(manifestFile))
        {
            try
            {
                if (new FileInfo(manifestFile).Length > 32 * 1024 * 1024) throw new InvalidDataException("Patch inventory exceeds the read-only inspection limit.");
                manifest = JsonSerializer.Deserialize<PatchManifest>(File.ReadAllText(manifestFile), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (manifest?.Entries is null || manifest.Entries.Count > 10000 || !pluginName.Equals(manifest.OutputPlugin, StringComparison.OrdinalIgnoreCase)) manifest = null;
            }
            catch (Exception failure) when (failure is JsonException or InvalidDataException)
            { App.WriteDiagnostic(DiagnosticEvent.PatchLibraryReadFailed, failure, DiagnosticLevel.Warning); }
        }
        if (manifest is not null)
        {
            metadataIncomplete = manifest.Entries.Any(entry => entry is null || entry.IncludedPieces is null || entry.ExcludedPieces is null ||
                entry.RequiredMasters is null || entry.Audiences is null || entry.PartChoices is null || entry.MerchantStock is null ||
                entry.OutfitName is null || entry.DistributionPath is null || entry.Framework is null);
            manifest.Entries.RemoveAll(entry => entry is null);
            foreach (var entry in manifest.Entries)
            {
                entry.IncludedPieces ??= []; entry.ExcludedPieces ??= []; entry.RequiredMasters ??= []; entry.Audiences ??= [];
                entry.PartChoices ??= []; entry.MerchantStock ??= []; entry.OutfitName ??= ""; entry.DistributionPath ??= ""; entry.Framework ??= "Unknown";
            }
            if (metadataIncomplete) App.WriteDiagnostic(DiagnosticEvent.PatchLibraryReadFailed, new InvalidDataException("Incomplete inventory metadata."), DiagnosticLevel.Warning);
        }
        var outfits = new Dictionary<string, (uint Id, List<FormKey> Items)>(StringComparer.OrdinalIgnoreCase);
        var lists = new Dictionary<FormKey, List<FormKey>>();
        string? pluginIssue = null;
        if (File.Exists(pluginFile))
        {
            try
            {
                using var mod = SkyrimMod.CreateFromBinaryOverlay(new Noggog.FilePath(pluginFile), SkyrimRelease.SkyrimSE);
                foreach (var outfit in mod.Outfits.Where(item => !item.IsDeleted))
                    outfits[outfit.EditorID ?? outfit.FormKey.ID.ToString("X6")] = (outfit.FormKey.ID, (outfit.Items ?? []).Select(item => item.FormKey).ToList());
                foreach (var list in mod.LeveledItems.Where(item => !item.IsDeleted))
                    lists[list.FormKey] = (list.Entries ?? []).Where(item => item.Data is not null).Select(item => item.Data!.Reference.FormKey).ToList();
            }
            catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
            { App.WriteDiagnostic(DiagnosticEvent.PatchLibraryReadFailed, failure, DiagnosticLevel.Warning); pluginIssue = "Generated ESP could not be read."; }
        }
        else pluginIssue = "Generated ESP is missing; inventory alone is not proof of a completed patch.";
        List<FormKey> Pieces(IEnumerable<FormKey> seeds)
        {
            var found = new HashSet<FormKey>(); var seen = new HashSet<FormKey>(); var pending = new Queue<FormKey>(seeds);
            while (pending.TryDequeue(out var key))
            {
                if (key.IsNull || !seen.Add(key)) continue;
                if (lists.TryGetValue(key, out var nested)) foreach (var child in nested) pending.Enqueue(child);
                else if (!key.ModKey.FileName.String.Equals(pluginName, StringComparison.OrdinalIgnoreCase)) found.Add(key);
            }
            return found.ToList();
        }
        var iniFile = Path.Combine(root, prefix + "_DISTR.ini");
        var skyFile = Path.Combine(root, "SKSE", "Plugins", "SkyPatcher", "npc", prefix, prefix + ".ini");
        var supplied = new[] { iniFile, skyFile }.Where(File.Exists).SelectMany(path => WearableFilterService.SuppliedForms(File.ReadAllText(path))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var managed = !metadataIncomplete && prefix.Equals("WearableDistributor", StringComparison.OrdinalIgnoreCase) && manifest is not null && manifest.SchemaVersion is >= 1 and <= 7 &&
            !File.Exists(Path.Combine(root, "SkyrimSPIDPresetTest.esp")) && !File.Exists(Path.Combine(root, "SkyrimSPIDPresetTest.patch.json"));
        IReadOnlyDictionary<PatchManifestEntry, PatchEntryHealth>? managedHealth = null;
        if (managed)
        {
            try { managedHealth = PatchHealthService.Inspect(root, manifest!, scannedPlugins); }
            catch (Exception failure) when (failure is IOException or ArgumentException or InvalidOperationException)
            { App.WriteDiagnostic(DiagnosticEvent.PatchHealthFailed, failure, DiagnosticLevel.Warning); }
        }
        var entries = manifest?.Entries ?? [];
        var recorded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries.Where(entry => entry is not null && ValidPlugin(entry.SourcePlugin)))
        {
            entry.IncludedPieces ??= []; entry.ExcludedPieces ??= []; entry.RequiredMasters ??= []; entry.Audiences ??= []; entry.PartChoices ??= []; entry.MerchantStock ??= [];
            var hasOutfit = !string.IsNullOrWhiteSpace(entry.OutfitEditorId) && outfits.TryGetValue(entry.OutfitEditorId, out var outfit);
            var present = hasOutfit && Pieces(outfits[entry.OutfitEditorId].Items).Any(item => item.ModKey.FileName.String.Equals(entry.SourcePlugin, StringComparison.OrdinalIgnoreCase));
            var hasRules = hasOutfit && supplied.Contains($"0x{outfits[entry.OutfitEditorId].Id:X6}~{pluginName}");
            if (!string.IsNullOrWhiteSpace(entry.WardrobeName) && outfits.TryGetValue(WardrobePools.EditorId(entry.WardrobeName), out var pool))
            {
                hasRules = supplied.Contains($"0x{pool.Id:X6}~{pluginName}");
                recorded.Add(WardrobePools.EditorId(entry.WardrobeName));
            }
            if (!entry.DistributeToNpcs) hasRules = entry.MerchantStock.Count > 0 && File.Exists(Path.Combine(root, "SKSE", "Plugins", "SkyPatcher", "container", prefix, prefix + "_Merchandise.ini"));
            var health = pluginIssue is not null ? "Missing / unreadable ESP" : metadataIncomplete ? "Inventory needs review" : !present ? "Missing outfit / parts" : !hasRules ? "Rules need review" : "Files found";
            if (managedHealth?.TryGetValue(entry, out var checkedHealth) == true) health = checkedHealth.Status == "Ready" ? "Files found" : checkedHealth.Status;
            var checkedExplanation = managedHealth?.GetValueOrDefault(entry)?.Explanation ?? "";
            result.Add(new(root, pluginFile, entry, present, managed, health, checkedExplanation + " " + pluginIssue ?? (metadataIncomplete ? "Incomplete inventory metadata; inspection is read-only. " : "") + (present ? "Source equipment is linked by the generated ESP. " : "Inventory entry does not match source equipment in the generated ESP. ") +
                (hasRules ? "Distribution file is present. " : "A matching distribution rule could not be confirmed. ") + "Detected file contents only; profile activation and in-game behavior are not established."));
            if (hasOutfit) recorded.Add(entry.OutfitEditorId);
        }
        // Recover actual outfit contents even when the manifest is missing, outdated or incomplete.
        foreach (var (editorId, outfit) in outfits.Where(pair => !recorded.Contains(pair.Key)))
            foreach (var source in Pieces(outfit.Items).Where(item => !WearableFilterService.IsOfficialContent(item.ModKey.FileName)).GroupBy(item => item.ModKey.FileName.String, StringComparer.OrdinalIgnoreCase))
            {
                var entry = new PatchManifestEntry { SourcePlugin = source.Key, OutfitName = editorId, OutfitEditorId = editorId,
                    Framework = File.Exists(iniFile) ? "SPID" : "SkyPatcher", DistributionPath = File.Exists(iniFile) ? prefix + "_DISTR.ini" : Path.GetRelativePath(root, skyFile),
                    IncludedPieces = source.Select(key => $"Record [0x{key.ID:X6}~{key.ModKey.FileName}]").ToList(), RequiredMasters = [source.Key], GeneratedAtUtc = File.GetLastWriteTimeUtc(pluginFile) };
                result.Add(new(root, pluginFile, entry, true, false, "Recovered from ESP", "Read directly from outfit and optional leveled-item links. Complete saved settings are unavailable; read-only inspection. Profile activation is not established."));
            }
    }
    private static bool ValidPlugin(string? name) => !string.IsNullOrWhiteSpace(name) && name == Path.GetFileName(name) && Path.GetExtension(name).ToLowerInvariant() is ".esp" or ".esm" or ".esl";
}
