using System.IO;
using System.Text;
using System.Text.Json;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace WearableDistributor;

public static class PatchInventoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public static PatchManifest ReadManifest(string outputDirectory)
    {
        var path = PatchOutputTransaction.ManagedPath(outputDirectory, PatchGenerator.ManifestName);
        if (!File.Exists(path)) return ReadLegacy(outputDirectory);
        var manifest = JsonSerializer.Deserialize<PatchManifest>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("The generated patch inventory is empty or unreadable. Restore a backup before generating another patch.");
        if (manifest.SchemaVersion is < 1 or > 3 || manifest.Entries is null || manifest.Entries.Count > 10_000)
            throw new InvalidDataException("The patch inventory has an unsupported schema or invalid entries. Restore a backup or open it with the version that created it.");
        if (!manifest.OutputPlugin.Equals(PatchGenerator.OutputPluginName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The patch inventory names an unexpected generated plugin.");
        foreach (var entry in manifest.Entries)
        {
            entry.MerchantStock ??= [];
            DistributionRuleCompiler.ValidatePluginName(entry.SourcePlugin);
            var expected = entry.Framework.Equals("SPID", StringComparison.OrdinalIgnoreCase) ? PatchGenerator.OutputIniName :
                entry.Framework.Equals("SkyPatcher", StringComparison.OrdinalIgnoreCase) ? PatchOutputTransaction.SkyPatcherIniPath :
                throw new InvalidDataException("The patch inventory contains an unsupported distribution framework.");
            if (!entry.DistributionPath.Replace('\\', '/').Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The patch inventory contains an unexpected distribution file path.");
            _ = PatchOutputTransaction.ManagedPath(outputDirectory, entry.DistributionPath);
            if (entry.IncludedPieces is null || entry.ExcludedPieces is null || entry.RequiredMasters is null || entry.Audiences is null)
                throw new InvalidDataException("The patch inventory contains incomplete outfit details.");
        }
        return manifest;
    }

    private static PatchManifest ReadLegacy(string outputDirectory)
    {
        var manifest = new PatchManifest();
        var outfitEditorIds = new Dictionary<uint, string>();
        var pluginPath = PatchOutputTransaction.ManagedPath(outputDirectory, PatchGenerator.OutputPluginName);
        if (File.Exists(pluginPath))
        {
            using var mod = SkyrimMod.CreateFromBinaryOverlay(new Noggog.FilePath(pluginPath), SkyrimRelease.SkyrimSE);
            foreach (var outfit in mod.Outfits) outfitEditorIds[outfit.FormKey.ID] = outfit.EditorID ?? "";
        }
        var iniPaths = new[]
        {
            (Path: PatchOutputTransaction.ManagedPath(outputDirectory, PatchGenerator.OutputIniName), Framework: "SPID"),
            (Path: PatchOutputTransaction.ManagedPath(outputDirectory, PatchOutputTransaction.SkyPatcherIniPath), Framework: "SkyPatcher")
        };
        foreach (var (path, framework) in iniPaths.Where(item => File.Exists(item.Path)))
        {
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                var marker = lines[i].StartsWith("; ", StringComparison.Ordinal) ? lines[i][2..] : "";
                var delimiter = marker.LastIndexOf(" — ", StringComparison.Ordinal);
                if (delimiter <= 0 || !marker.EndsWith(" piece(s)", StringComparison.Ordinal)) continue;
                var plugin = marker[..delimiter];
                var pieceCountText = marker[(delimiter + 3)..].Replace(" piece(s)", "", StringComparison.Ordinal);
                _ = int.TryParse(pieceCountText, out var pieceCount);
                var sectionLines = new List<string>();
                for (var row = i + 1; row < lines.Length && !IsSectionHeader(lines[row]); row++)
                    if (!string.IsNullOrWhiteSpace(lines[row]) && !lines[row].TrimStart().StartsWith(";", StringComparison.Ordinal)) sectionLines.Add(lines[row]);
                var chance = 100;
                var gender = "Any";
                foreach (var row in sectionLines.Where(line => line.StartsWith("Outfit = ", StringComparison.Ordinal)))
                {
                    var columns = row[(row.IndexOf('=') + 1)..].Trim().Split('|');
                    if (columns.Length > 6 && int.TryParse(columns[6], out var parsedChance)) chance = parsedChance;
                    if (columns.Length > 4)
                    {
                        var traits = columns[4].Split('/');
                        gender = traits.Contains("F") ? "Female" : traits.Contains("M") ? "Male" : "Any";
                    }
                }
                var outfitName = i + 1 < lines.Length && lines[i + 1].StartsWith("; Outfit: ", StringComparison.Ordinal)
                    ? lines[i + 1][10..] : Path.GetFileNameWithoutExtension(plugin);
                var outfitEditorId = "";
                if (outfitEditorIds.Count > 0)
                {
                    var row = sectionLines.FirstOrDefault(line => framework == "SPID" ? line.StartsWith("Outfit = ", StringComparison.Ordinal) : line.Contains("outfitDefault=", StringComparison.Ordinal));
                    if (row is not null)
                    {
                        try
                        {
                            var form = framework == "SPID" ? DistributionRuleCompiler.ParseSpidForm(row[(row.IndexOf('=') + 1)..].Trim().Split('|')[0]) :
                                DistributionRuleCompiler.ParseSkyPatcherForm(row.Split(':').Single(field => field.StartsWith("outfitDefault=", StringComparison.Ordinal))[14..]);
                            if (form.Plugin.Equals(PatchGenerator.OutputPluginName, StringComparison.OrdinalIgnoreCase))
                                outfitEditorId = outfitEditorIds.GetValueOrDefault(form.Id, "");
                        }
                        catch (InvalidDataException error) { App.WriteDiagnostic(DiagnosticEvent.LegacyRuleReadFailed, error, DiagnosticLevel.Warning); }
                    }
                }
                manifest.Entries.Add(new PatchManifestEntry
                {
                    SourcePlugin = plugin, OutfitName = outfitName, Framework = framework, DistributionPath = Path.GetRelativePath(outputDirectory, path),
                    IncludedPieces = Enumerable.Range(1, Math.Max(pieceCount, 0)).Select(index => $"Legacy outfit piece {index} (record details unavailable)").ToList(),
                    Audiences = sectionLines, Chance = chance, Gender = gender, DistributionRows = sectionLines.Count(line => line.StartsWith("Outfit = ", StringComparison.Ordinal) || line.Contains("outfitDefault=", StringComparison.Ordinal)),
                    RequiredMasters = [plugin],
                    GeneratedAtUtc = File.GetLastWriteTimeUtc(path), OutfitEditorId = outfitEditorId
                });
            }
        }
        return manifest;
    }

    public static void ExportReport(string outputDirectory, string destination)
    {
        var manifest = ReadManifest(outputDirectory);
        var report = new
        {
            manifest.SchemaVersion,
            manifest.GeneratedAtUtc,
            OutputDirectory = Path.GetFullPath(outputDirectory),
            PluginPresent = File.Exists(Path.Combine(outputDirectory, PatchGenerator.OutputPluginName)),
            Entries = manifest.Entries.Select(entry => new
            {
                entry.SourcePlugin, entry.OutfitName, entry.Framework, entry.DistributionPath, entry.IncludedPieces, entry.ExcludedPieces, entry.RequiredMasters,
                entry.Audiences, entry.Chance, entry.Gender, entry.DistributionRows, entry.GeneratedAtUtc, entry.MerchantStock
            })
        };
        File.WriteAllText(destination, JsonSerializer.Serialize(report, JsonOptions), new UTF8Encoding(false));
    }

    public static void Remove(string outputDirectory, PatchManifestEntry entry)
    {
        outputDirectory = PatchOutputTransaction.NormalizeRoot(outputDirectory);
        using var outputLease = PatchOutputTransaction.Acquire(outputDirectory);
        PatchOutputTransaction.EnsureNoInterruptedWrite(outputDirectory);
        var manifest = ReadManifest(outputDirectory);
        entry = manifest.Entries.SingleOrDefault(candidate => candidate.SourcePlugin.Equals(entry.SourcePlugin, StringComparison.OrdinalIgnoreCase) &&
            candidate.Framework.Equals(entry.Framework, StringComparison.OrdinalIgnoreCase) && candidate.OutfitEditorId.Equals(entry.OutfitEditorId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("This outfit is no longer in the current patch inventory. Refresh Patch Overview and try again.");
        if (string.IsNullOrWhiteSpace(entry.OutfitEditorId))
            throw new InvalidOperationException("This legacy outfit could not be linked to its generated ESP record. Repatch it before removing it from the inventory.");
        var outputPlugin = PatchOutputTransaction.ManagedPath(outputDirectory, PatchGenerator.OutputPluginName);
        var iniPath = PatchOutputTransaction.ManagedPath(outputDirectory, entry.DistributionPath);
        if (!File.Exists(outputPlugin)) throw new FileNotFoundException("The generated Skyrim SPID Patch Generator plugin was not found.", outputPlugin);
        var mod = SkyrimMod.CreateFromBinary(new ModPath(new Noggog.FilePath(outputPlugin)), SkyrimRelease.SkyrimSE);

        var lines = File.Exists(iniPath) ? File.ReadAllLines(iniPath).ToList() : [];
        var retained = new List<string>();
        var removedSection = false;
        for (var i = 0; i < lines.Count;)
        {
            if (lines[i].StartsWith("; " + entry.SourcePlugin + " — ", StringComparison.OrdinalIgnoreCase) && i + 1 < lines.Count &&
                (lines[i + 1].Equals("; Outfit: " + entry.OutfitName, StringComparison.OrdinalIgnoreCase) || !lines[i + 1].StartsWith("; Outfit:", StringComparison.Ordinal)))
            {
                removedSection = true;
                i++;
                while (i < lines.Count && !IsSectionHeader(lines[i])) i++;
                while (i < lines.Count && string.IsNullOrWhiteSpace(lines[i])) i++;
                continue;
            }
            retained.Add(lines[i++]);
        }

        if (!removedSection) throw new InvalidDataException("The generated outfit's INI section could not be located. No files were changed. Repatch the outfit first.");
        manifest.Entries.RemoveAll(candidate => candidate.SourcePlugin.Equals(entry.SourcePlugin, StringComparison.OrdinalIgnoreCase) &&
            candidate.Framework.Equals(entry.Framework, StringComparison.OrdinalIgnoreCase) &&
            candidate.OutfitEditorId.Equals(entry.OutfitEditorId, StringComparison.OrdinalIgnoreCase));
        if (!manifest.Entries.Any(candidate => candidate.OutfitEditorId.Equals(entry.OutfitEditorId, StringComparison.OrdinalIgnoreCase)))
        {
            var outfit = mod.Outfits.FirstOrDefault(candidate => string.Equals(candidate.EditorID, entry.OutfitEditorId, StringComparison.OrdinalIgnoreCase));
            if (outfit is not null) mod.Outfits.Remove(outfit);
        }
        manifest.GeneratedAtUtc = DateTime.UtcNow;
        var tempRoot = Path.Combine(outputDirectory, ".WearableDistributor-remove-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var stagedPlugin = Path.Combine(tempRoot, PatchGenerator.OutputPluginName);
            var stagedIni = PatchOutputTransaction.ManagedPath(tempRoot, entry.DistributionPath);
            var stagedManifest = Path.Combine(tempRoot, PatchGenerator.ManifestName);
            // Preserve allocation history when deleting the final outfit; the
            // next generation must not allocate FormID zero or reuse a deleted ID.
            mod.BeginWrite.ToPath(stagedPlugin).WithNoLoadOrder().NoNextFormIDProcessing().Write();
            Directory.CreateDirectory(Path.GetDirectoryName(stagedIni)!);
            File.WriteAllLines(stagedIni, retained, new UTF8Encoding(false));
            File.WriteAllText(stagedManifest, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
            var merchandise = MerchantStockService.Stage(tempRoot, manifest);
            PatchGenerator.ValidateOutputSet(tempRoot);
            PatchOutputTransaction.Commit(outputDirectory, [stagedPlugin, stagedIni, stagedManifest, merchandise],
                [outputPlugin, iniPath, PatchGenerator.ManifestPathFor(outputDirectory), PatchOutputTransaction.ManagedPath(outputDirectory, MerchantStockService.RelativePath)], "Remove");
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch (Exception cleanup) { App.WriteDiagnostic(DiagnosticEvent.TemporaryCleanupFailed, cleanup, DiagnosticLevel.Warning); }
        }
    }

    public static void RestoreLastBackup(string outputDirectory) => PatchOutputTransaction.Restore(outputDirectory, PatchGenerator.ValidateOutputSet);

    private static bool IsSectionHeader(string line) => line.StartsWith("; ", StringComparison.Ordinal) &&
        line.Contains(" — ", StringComparison.Ordinal) && line.EndsWith(" piece(s)", StringComparison.Ordinal);
}
