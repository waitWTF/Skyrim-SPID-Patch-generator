using System.IO;
using System.Text.Json;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using WearableDistributor;

public static class WardrobeChecks
{
    public static void Run(string parent)
    {
        var count = 0;
        void Check(bool value, string text) { if (!value) throw new InvalidOperationException("Wardrobe check: " + text); count++; Console.WriteLine("PASS wardrobes: " + text); }
        var folder = Path.Combine(parent, "random-wardrobes");
        PluginPatchOptions Config(string plugin, string pool = "Wenches wardrobe")
        {
            var pieces = new List<WearableRecord> {
                new() { Plugin = plugin, LocalId = 0x800, Name = "Robe Black", Slots = "Body", SupportedGender = "Female" },
                new() { Plugin = plugin, LocalId = 0x801, Name = "Robe Red", Slots = "Body", SupportedGender = "Female" },
                new() { Plugin = plugin, LocalId = 0x802, Name = "Belt", Slots = "Unnamed48", PartMode = "Optional", OptionalChance = 50 }
            };
            return new() { Source = new() { Plugin = plugin, Pieces = pieces }, Pieces = pieces, OutfitName = Path.GetFileNameWithoutExtension(plugin), Gender = "Female", Chance = "100", WardrobeName = pool, MatchVariants = true,
                TargetRules = [new() { Collection = "Immersive Wenches.esp", Group = "Wenches", NpcResolutionComplete = true, EstimatedNpcCount = 2,
                    ResolvedNpcFormIds = ["0x000800~Immersive Wenches.esp", "0x000801~Immersive Wenches.esp"],
                    Filters = [new() { Plugin = "Immersive Wenches.esp", LocalId = 0x1234, EditorId = "WenchesFaction", TargetKind = "Faction", Category = "Faction" }] }] };
        }
        var a = Config("WardrobeA.esp"); var b = Config("WardrobeB.esp"); var c = Config("WardrobeC.esp"); c.WardrobeWeight = 2;
        var result = PatchGenerator.Generate([a, b, c], folder, "SPID");
        PatchManifest Manifest() => PatchInventoryService.ReadManifest(folder);
        List<string> Rules() => File.ReadLines(result.DistributionPath).Where(l => l.StartsWith("Outfit = ")).ToList();
        var manifest = Manifest();
        Check(manifest.SchemaVersion == 7 && manifest.Entries.Count == 3, "Schema 7 records three real sources, without inventing a source plugin for the pool");
        Check(Rules().Count == 1 && Rules()[0].EndsWith("|100"), "Three 100% members produce one shared coverage rule, never three competing winners");
        Check(manifest.Entries.Count(e => e.WardrobeRuleOwner) == 1 && manifest.Entries.Sum(e => e.DistributionRows) == 1, "Exactly one member owns the shared rule");
        string[] Ids()
        {
            using var mod = SkyrimMod.CreateFromBinaryOverlay(new Noggog.FilePath(result.PluginPath), SkyrimRelease.SkyrimSE);
            return mod.Outfits.Select(o => o.FormKey.ToString()).Concat(mod.LeveledItems.Select(l => l.FormKey.ToString())).Order().ToArray();
        }
        using (var mod = SkyrimMod.CreateFromBinaryOverlay(new Noggog.FilePath(result.PluginPath), SkyrimRelease.SkyrimSE))
        {
            var pool = mod.Outfits.Single(o => o.EditorID!.StartsWith("SSPG_Wardrobe_"));
            var selector = mod.LeveledItems.Single(l => l.FormKey == pool.Items!.Single().FormKey);
            Check(selector.Flags == 0 && selector.ChanceNone == 0 && selector.Entries!.Count == 4, "A weight 1:1:2 selector chooses one of four tickets without an empty result");
            Check(selector.Entries!.GroupBy(e => e.Data!.Reference.FormKey).Select(g => g.Count()).Order().SequenceEqual(new[] { 1, 1, 2 }), "Weighted tickets point to exactly three distinct bundles");
            var bundles = selector.Entries!.Select(e => mod.LeveledItems.Single(l => l.FormKey == e.Data!.Reference.FormKey)).DistinctBy(l => l.FormKey).ToList();
            Check(bundles.All(l => l.Flags == LeveledItem.Flag.UseAll && l.ChanceNone == 0), "Each selected bundle includes the full member outfit, not a random single garment");
            Check(mod.LeveledItems.SelectMany(l => l.Entries ?? []).All(e => !mod.Outfits.Any(o => o.FormKey == e.Data!.Reference.FormKey)), "No leveled item links illegally to an outfit record");
            Check(bundles.All(bundle => bundle.Entries!.Count == 1 && mod.LeveledItems.Any(l => l.FormKey == bundle.Entries[0].Data!.Reference.FormKey && l.Entries!.Count == 2)), "Each member preserves its own two-color selector below the wardrobe");
            Check(mod.LeveledItems.Count(l => Math.Abs(l.ChanceNone - 0.5) < .01) == 6, "Optional shared belts keep their 50% roll in both colors of all three outfits");
            Check(mod.Outfits.Count == 4, "Three editable member records and one shared wardrobe record are retained");
        }
        var detected = DetectedPatchReader.Read([result.PluginPath], [a.Source, b.Source, c.Source]);
        Check(detected.Count == 3 && detected.All(d => d.CanManage && d.Health == "Files found"), "Patch discovery shows the three real members once and recognizes the shared distribution rule");
        Check(PatchHealthService.Inspect(folder, manifest, [a.Source, b.Source, c.Source]).Values.All(h => h.Status == "Ready"), "Health checks validate complete wardrobe graphs and distribution");
        var ids = Ids();
        PatchGenerator.Generate([b], folder, "SPID");
        Check(ids.SequenceEqual(Ids()) && Rules().Count == 1 && Manifest().Entries.Count == 3, "Partial repatching retains the other pool members, FormIDs and one shared rule");
        b.WardrobeWeight = 3; PatchGenerator.Generate([b], folder, "SPID");
        Check(ids.SequenceEqual(Ids()) && Manifest().Entries.Single(e => e.SourcePlugin == b.Source.Plugin).WardrobeWeight == 3, "A partial weight edit updates odds without reallocating records");
        var other = Config("Unrelated.esp", ""); other.Chance = "25";
        PatchGenerator.Generate([other], folder, "SPID");
        Check(Rules().Count == 2 && Manifest().Entries.Count == 4, "Adding an unrelated outfit preserves the wardrobe and its rule");
        var before = File.ReadAllBytes(result.PluginPath);
        void Reject(Action action, string text)
        {
            var rejected = false; try { action(); } catch (Exception e) when (e is InvalidOperationException or InvalidDataException) { rejected = true; }
            Check(rejected && before.SequenceEqual(File.ReadAllBytes(result.PluginPath)), text);
        }
        b.Chance = "80"; Reject(() => PatchGenerator.Generate([b], folder, "SPID"), "Changing just one member's coverage rejects inconsistent retained scopes without touching output"); b.Chance = "100";
        b.TargetRules[0].ResolvedNpcFormIds.Add("0x000802~Immersive Wenches.esp"); Reject(() => PatchGenerator.Generate([b], folder, "SPID"), "A partial target change cannot silently broaden the wardrobe"); b.TargetRules[0].ResolvedNpcFormIds.RemoveAt(2);
        b.Gender = "Any"; Reject(() => PatchGenerator.Generate([b], folder, "SPID"), "Female-only member cannot be broadened to incompatible NPC sex"); b.Gender = "Female";
        b.WardrobeWeight = 0; Reject(() => PatchGenerator.Generate([b], folder, "SPID"), "Invalid member weight is rejected"); b.WardrobeWeight = 3;
        b.DistributeToNpcs = false; Reject(() => PatchGenerator.Generate([b], folder, "SPID"), "Merchants-only member cannot silently enter an NPC wardrobe"); b.DistributeToNpcs = true;
        Reject(() => PatchGenerator.Generate([a, b, c], Path.Combine(parent, "wardrobe-sky"), "SkyPatcher"), "Unsupported SkyPatcher pool generation is rejected clearly");
        var tooMany = Enumerable.Range(0, 21).Select(i => Config("TooMany" + i + ".esp")).ToList();
        Reject(() => PatchGenerator.Generate(tooMany, Path.Combine(parent, "wardrobe-limit"), "SPID"), "More than twenty members is rejected before writing");
        a.Chance = b.Chance = c.Chance = "50";
        PatchGenerator.Generate([a, b, c], folder, "SPID");
        Check(Rules().Count == 2 && Rules().Count(l => l.EndsWith("|50")) == 1, "Editing the full pool updates one coverage roll while unrelated chance stays unchanged");
        a.MerchantCategories = [MerchantStockService.Blacksmiths, MerchantStockService.Clothing];
        a.MerchantTargets = [new("Skyrim.esm", 0x1000, "SmithStock", MerchantStockService.Blacksmiths), new("Skyrim.esm", 0x1001, "ClothesStock", MerchantStockService.Clothing)];
        a.Source.Weapons = [new() { Plugin = a.Source.Plugin, LocalId = 0x900, Name = "Set sword", Category = "Weapon" }];
        var withStock = PatchGenerator.Generate([a], folder, "SPID");
        Check(withStock.MerchantDistributionCount == 2 && Manifest().Entries.Single(m => m.SourcePlugin == a.Source.Plugin).MerchantStock.Count == 7 && Rules().Count == 2, "Pool weights/coverage leave all selected stock items intact and exclude only weapons from clothing traders");
        var goodEsp = File.ReadAllBytes(result.PluginPath); var goodIni = File.ReadAllText(result.DistributionPath);
        var corrupt = SkyrimMod.CreateFromBinary(new ModPath(new Noggog.FilePath(result.PluginPath)), SkyrimRelease.SkyrimSE);
        corrupt.LeveledItems.Single(l => l.EditorID!.EndsWith("_ChooseOne")).Flags = LeveledItem.Flag.UseAll; corrupt.WriteToBinary(result.PluginPath);
        Check(PatchHealthService.Inspect(folder, Manifest()).Values.All(h => h.Status == "Missing / unreadable ESP"), "A tampered UseAll selector is caught instead of equipping every outfit");
        File.WriteAllBytes(result.PluginPath, goodEsp);
        File.AppendAllText(result.DistributionPath, "\n" + Rules().First(l => l.EndsWith("|50")) + "\n");
        Check(PatchHealthService.Inspect(folder, Manifest()).Values.All(h => h.Status == "Missing / unreadable ESP"), "A duplicate shared rule is detected");
        File.WriteAllText(result.DistributionPath, goodIni);
        var memberForm = "";
        using (var mod = SkyrimMod.CreateFromBinaryOverlay(new Noggog.FilePath(result.PluginPath), SkyrimRelease.SkyrimSE))
            memberForm = $"0x{mod.Outfits.First(o => !o.EditorID!.StartsWith("SSPG_Wardrobe_")).FormKey.ID:X6}~{PatchGenerator.OutputPluginName}";
        File.WriteAllText(result.DistributionPath, $"Outfit = {memberForm}|NONE|0x000800~Immersive Wenches.esp|NONE|F/-C|NONE|100\n" + goodIni);
        Check(PatchHealthService.Inspect(folder, Manifest()).Values.All(h => h.Status == "Missing / unreadable ESP"), "A competing individual member rule outside all managed sections is detected");
        File.WriteAllText(result.DistributionPath, goodIni);
        var goodManifest = File.ReadAllText(PatchGenerator.ManifestPathFor(folder));
        foreach (var kind in new[] { "chance", "filters" })
        {
            var data = System.Text.Json.Nodes.JsonNode.Parse(goodManifest)!;
            foreach (var entry in data["Entries"]!.AsArray().Where(e => e!["WardrobeName"]!.GetValue<string>().Length > 0))
                if (kind == "chance") entry!["Chance"] = 0; else entry!["WardrobeTargets"]![0]!["Filters"] = null;
            File.WriteAllText(PatchGenerator.ManifestPathFor(folder), data.ToJsonString());
            Check(PatchHealthService.Inspect(folder, Manifest()).Values.All(h => h.Status == "Missing / unreadable ESP"), "Malformed wardrobe " + kind + " metadata is reported safely");
        }
        File.WriteAllText(PatchGenerator.ManifestPathFor(folder), goodManifest);
        var owner = Manifest().Entries.Single(e => e.WardrobeRuleOwner);
        PatchInventoryService.Remove(folder, owner);
        Check(Manifest().Entries.Count == 3 && Manifest().Entries.Count(e => e.WardrobeRuleOwner) == 1 && Rules().Count == 2, "Removing the rule owner transfers the one shared rule to a surviving member");
        Check(PatchHealthService.Inspect(folder, Manifest()).Values.All(h => h.Status == "Ready"), "Removal leaves every retained entry healthy");
        PatchInventoryService.RestoreLastBackup(folder);
        Check(Manifest().Entries.Count == 4 && PatchHealthService.Inspect(folder, Manifest()).Values.All(h => h.Status == "Ready"), "Backup restores wardrobe members, selectors and shared rule together");
        var pooled = Manifest().Entries.Where(e => e.WardrobeName.Length > 0).ToList();
        PatchInventoryService.Remove(folder, pooled[0]); PatchInventoryService.Remove(folder, pooled[1]);
        Check(Manifest().Entries.Count(e => e.WardrobeName.Length > 0) == 1 && Rules().Count == 2, "A pool reduced to one retained member remains valid");
        PatchInventoryService.Remove(folder, Manifest().Entries.Single(e => e.WardrobeName.Length > 0));
        using (var mod = SkyrimMod.CreateFromBinaryOverlay(new Noggog.FilePath(result.PluginPath), SkyrimRelease.SkyrimSE))
            Check(!mod.Outfits.Any(o => o.EditorID!.StartsWith("SSPG_Wardrobe_")) && !mod.LeveledItems.Any(l => l.EditorID!.StartsWith("SSPG_Wardrobe_")), "Removing the last member prunes only obsolete wardrobe records");
        Check(Manifest().Entries.Count == 1 && Rules().Count == 1, "Unrelated outfit survives complete wardrobe removal");
        a.Chance = b.Chance = "100"; PatchGenerator.Generate([a, b], folder, "SPID");
        a.WardrobeName = ""; PatchGenerator.Generate([a], folder, "SPID");
        Check(Manifest().Entries.Single(e => e.SourcePlugin == a.Source.Plugin).WardrobeName == "" && Rules().Count == 3, "Leaving a pool restores an individual rule without deleting other members");
        var savedPath = Path.Combine(parent, "wardrobe-settings.json");
        PreferencesService.Save(savedPath, new() { DistributionSettings = new() { [b.Source.Plugin] = new() { WardrobeName = b.WardrobeName, WardrobeWeight = 3 } } });
        var saved = PreferencesService.Load(savedPath);
        Check(saved.SchemaVersion == 7 && saved.DistributionSettings[b.Source.Plugin].WardrobeWeight == 3, "Pool membership and weight persist across settings sessions");
        var manifestText = File.ReadAllText(PatchGenerator.ManifestPathFor(folder)); File.Delete(PatchGenerator.ManifestPathFor(folder));
        var missingRejected = false; try { PatchGenerator.Generate([other], folder, "SPID"); } catch (InvalidDataException) { missingRejected = true; }
        Check(missingRejected, "Missing pool inventory cannot silently become an incorrect legacy patch");
        File.WriteAllText(PatchGenerator.ManifestPathFor(folder), manifestText);
        var manyPoolPath = Path.Combine(parent, "two-independent-wardrobes");
        var d = Config("WardrobeD.esp", "Other wardrobe"); var e = Config("WardrobeE.esp", "Other wardrobe");
        a.MerchantCategories.Clear(); a.WardrobeName = "Wenches wardrobe";
        var two = PatchGenerator.Generate([a, b, d, e], manyPoolPath, "SPID");
        Check(File.ReadLines(two.DistributionPath).Count(l => l.StartsWith("Outfit = ")) == 2 && PatchInventoryService.ReadManifest(manyPoolPath).Entries.Count(m => m.WardrobeRuleOwner) == 2, "Two separately named pools maintain distinct selectors and shared rules");
        Check(PatchHealthService.Inspect(manyPoolPath, PatchInventoryService.ReadManifest(manyPoolPath)).Values.All(h => h.Status == "Ready"), "Multiple pool graphs validate independently");
        Console.WriteLine($"Wardrobe checks passed: {count}");
    }
}
