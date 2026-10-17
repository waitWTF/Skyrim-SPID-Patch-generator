using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using WearableDistributor;

public static class OutputReviewChecks
{
    public static void Run(string root)
    {
        var path = Path.Combine(root, "review-truncated-output");
        var first = Configuration("ReviewFarmer.esp", "Farmer outfit", 0x820);
        var result = PatchGenerator.Generate([first], path, "SPID");
        var paths = Paths(path);
        var validIni = File.ReadAllText(result.DistributionPath);
        File.WriteAllLines(result.DistributionPath, File.ReadAllLines(result.DistributionPath)
            .Where(line => !line.StartsWith("Outfit = ", StringComparison.Ordinal)));
        var before = Bytes(paths);
        Reject(() => PatchPackageService.Export(path, Path.Combine(root, "truncated-output.zip")), "an existing but empty outfit section cannot be exported");
        Reject(() => PatchGenerator.Generate([Configuration("ReviewGuard.esp", "Guard outfit", 0x821)], path, "SPID"), "merging cannot conceal lost existing outfit rules");
        Unchanged(paths, before, "rejecting truncated output leaves every generated file unchanged");

        File.WriteAllText(result.DistributionPath, validIni);
        var helper = File.ReadLines(result.DistributionPath).First(line => line.StartsWith("Keyword = WD_Target_", StringComparison.Ordinal));
        File.WriteAllLines(result.DistributionPath, File.ReadAllLines(result.DistributionPath).Where(line => line != helper));
        before = Bytes(paths);
        Reject(() => PatchPackageService.Export(path, Path.Combine(root, "missing-helper.zip")), "missing SPID helper dependencies cannot be exported");
        Unchanged(paths, before, "helper validation never changes the source patch");
        File.WriteAllText(result.DistributionPath, validIni);

        var second = Configuration("ReviewGuard.esp", "Guard outfit", 0x821);
        PatchGenerator.Generate([second], path, "SPID");
        using (var mod = SkyrimMod.CreateFromBinaryOverlay(new Noggog.FilePath(result.PluginPath), SkyrimRelease.SkyrimSE))
        {
            var firstId = mod.Outfits.Single(outfit => outfit.EditorID == PatchGenerator.StableEditorIdFor(first.Source.Plugin, first.OutfitName)).FormKey.ID;
            var secondId = mod.Outfits.Single(outfit => outfit.EditorID == PatchGenerator.StableEditorIdFor(second.Source.Plugin, second.OutfitName)).FormKey.ID;
            var ini = File.ReadAllText(result.DistributionPath);
            // Keep every ESP outfit referenced, but swap the two sections. Basic
            // existence checks pass; only section-to-inventory checks catch this.
            var swapped = ini.Replace($"0x{firstId:X6}~WearableDistributor.esp", "SWAP_OUTFIT_ID", StringComparison.Ordinal)
                .Replace($"0x{secondId:X6}~WearableDistributor.esp", $"0x{firstId:X6}~WearableDistributor.esp", StringComparison.Ordinal)
                .Replace("SWAP_OUTFIT_ID", $"0x{secondId:X6}~WearableDistributor.esp", StringComparison.Ordinal);
            File.WriteAllText(result.DistributionPath, swapped);
            Reject(() => PatchPackageService.Export(path, Path.Combine(root, "wrong-outfit-sections.zip")), "a valid but wrong ESP outfit in an inventory section is rejected");
            File.WriteAllText(result.DistributionPath, ini);
        }

        // A checksum proves that the snapshot bytes are intact, not that the ESP,
        // INI and inventory still describe one coherent generated patch.
        var backup = LatestBackup(path);
        var metadataPath = Path.Combine(backup, "backup.json");
        var metadata = JsonNode.Parse(File.ReadAllText(metadataPath))!;
        var savedIniEntry = metadata["Files"]!.AsArray().Single(entry => entry!["RelativePath"]!.GetValue<string>() == PatchGenerator.OutputIniName)!;
        var savedIni = Path.Combine(backup, savedIniEntry["BackupFile"]!.GetValue<string>());
        File.WriteAllLines(savedIni, File.ReadAllLines(savedIni).Where(line => !line.StartsWith("Outfit = ", StringComparison.Ordinal)));
        var changedBytes = File.ReadAllBytes(savedIni);
        savedIniEntry["Length"] = changedBytes.LongLength;
        savedIniEntry["Sha256"] = Convert.ToHexString(SHA256.HashData(changedBytes));
        File.WriteAllText(metadataPath, metadata.ToJsonString());
        before = Bytes(paths);
        Reject(() => PatchInventoryService.RestoreLastBackup(path), "semantically incomplete backups fail before any restore destination changes");
        Unchanged(paths, before, "failed semantic restore preserves the current complete patch");

        var orphanPath = Path.Combine(root, "review-orphan-record");
        var orphan = PatchGenerator.Generate([first], orphanPath, "SPID");
        File.Delete(PatchGenerator.ManifestPathFor(orphanPath));
        File.WriteAllText(orphan.DistributionPath, "; truncated old output\n");
        Reject(() => PatchGenerator.Generate([second], orphanPath, "SPID"), "an absent manifest cannot conceal outfits with lost legacy sections");

        var emptyPath = Path.Combine(root, "review-remove-last");
        var beforeRemoval = PatchGenerator.Generate([first], emptyPath, "SPID");
        uint removedId;
        using (var mod = SkyrimMod.CreateFromBinaryOverlay(new Noggog.FilePath(beforeRemoval.PluginPath), SkyrimRelease.SkyrimSE))
            removedId = mod.Outfits.Single().FormKey.ID;
        PatchInventoryService.Remove(emptyPath, PatchInventoryService.ReadManifest(emptyPath).Entries.Single());
        Check(PatchInventoryService.ReadManifest(emptyPath).Entries.Count == 0, "removing the last outfit leaves a valid empty inventory");
        var afterRemoval = PatchGenerator.Generate([second], emptyPath, "SPID");
        Check(PatchInventoryService.ReadManifest(emptyPath).Entries.Count == 1, "new generation succeeds after removing the final outfit");
        using (var mod = SkyrimMod.CreateFromBinaryOverlay(new Noggog.FilePath(afterRemoval.PluginPath), SkyrimRelease.SkyrimSE))
            Check(mod.Outfits.Single().FormKey.ID >= 0x800 && mod.Outfits.Single().FormKey.ID > removedId,
                "regeneration after removing the last outfit uses a valid new ID without reusing the removed record");
        var oldEmptyPath = Path.Combine(root, "review-old-empty-allocator");
        Directory.CreateDirectory(oldEmptyPath);
        var oldEmptyMod = new SkyrimMod(Mutagen.Bethesda.Plugins.ModKey.FromFileName(PatchGenerator.OutputPluginName), SkyrimRelease.SkyrimSE);
        ((Mutagen.Bethesda.Plugins.Records.IMod)oldEmptyMod).NextFormID = 0;
        oldEmptyMod.BeginWrite.ToPath(Path.Combine(oldEmptyPath, PatchGenerator.OutputPluginName)).WithNoLoadOrder().NoNextFormIDProcessing().Write();
        var repaired = PatchGenerator.Generate([second], oldEmptyPath, "SPID");
        using (var mod = SkyrimMod.CreateFromBinaryOverlay(new Noggog.FilePath(repaired.PluginPath), SkyrimRelease.SkyrimSE))
            Check(mod.Outfits.Single().FormKey.ID >= 0x800, "an older empty output with NextFormID zero is repaired before allocating a new outfit");
        Console.WriteLine("Whole output integrity review checks passed.");
    }

    private static PluginPatchOptions Configuration(string plugin, string name, uint itemId)
    {
        var source = new PluginEntry { Plugin = plugin, SourcePath = plugin };
        source.Pieces.Add(new WearableRecord { Plugin = plugin, LocalId = itemId, Name = name + " body", Slots = "Body", Include = true });
        return new PluginPatchOptions { Source = source, OutfitName = name, Chance = "25", Pieces = source.Pieces,
            TargetRules = [new DistributionTargetRule { Group = "Farmers", Filters = [new KeywordChoice { Plugin = "Skyrim.esm", LocalId = 1, EditorId = "JobFarmerFaction", Category = "Faction", TargetKind = "Faction" }] }] };
    }
    private static string[] Paths(string root) => [Path.Combine(root, PatchGenerator.OutputPluginName), Path.Combine(root, PatchGenerator.OutputIniName), PatchGenerator.ManifestPathFor(root)];
    private static byte[][] Bytes(string[] paths) => paths.Select(File.ReadAllBytes).ToArray();
    private static void Unchanged(string[] paths, byte[][] expected, string message) => Check(paths.Select((path, index) => File.ReadAllBytes(path).SequenceEqual(expected[index])).All(value => value), message);
    private static string LatestBackup(string path)
    {
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).ToUpperInvariant())))[..16];
        var backupRoot = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA")!, "WearableDistributor", "Backups", hash);
        return Directory.GetDirectories(backupRoot).OrderByDescending(Path.GetFileName, StringComparer.Ordinal).First();
    }
    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or IOException) { return; }
        throw new InvalidOperationException("Output review check failed: " + message);
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Output review check failed: " + message);
    }
}
