using System.IO;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using WearableDistributor;

public static class VariantChecks
{
    public static void Run(string parent)
    {
        var count = 0;
        void Check(bool value, string text) { if (!value) throw new InvalidOperationException(text); Console.WriteLine("PASS variants: " + text); count++; }
        uint id = 0x800;
        WearableRecord Piece(string name, string slots) => new() { Plugin = "RogueExample.esp", LocalId = id++, Name = name, Slots = slots };
        var pieces = new List<WearableRecord> { Piece("Rogue Top Black", "Body"), Piece("Rogue Top Red", "Body"), Piece("Rogue Boots Black", "Feet"), Piece("Rogue Boots Red", "Feet"), Piece("Rogue Pants", "524288"), Piece("Rogue Pants Torn", "524288"), Piece("Rogue Belt", "Unnamed48"), Piece("Rogue Top Lewd Black", "Body") };
        var source = new PluginEntry { Plugin = "RogueExample.esp", Pieces = pieces };
        var config = new PluginPatchOptions { Source = source, Pieces = pieces, OutfitName = "Rogue example", TargetRules = [new() { Group = "Bandits", Filters = [new() { Plugin = "Skyrim.esm", LocalId = 0x1234, EditorId = "BanditFaction", Category = "Faction", TargetKind = "Faction" }] }] };
        OutfitPartPresets.Apply(config, "Balanced");
        var branches = OutfitVariantPlan.Create(config);
        Check(OutfitPartPresets.Summary(config).Contains("1 of 2") && OutfitPartPresets.Summary(config).Contains("3 required garment groups"), "Summary counts color choices and garment groups instead of implying every alternative is equipped");
        Check(branches.Count == 2 && pieces.Count(p => p.Include) == 7, "Both complete colors remain available; lewd garment stays excluded");
        Check(branches.All(b => b.Groups.Single(g => g.Forms.Contains(pieces[4].FormId)).Forms.Count == 2), "Normal and torn pants form one required alternative group in every color");
        Check(branches.All(b => b.Groups.Single(g => g.Forms.Contains(pieces[4].FormId)).Chance == 100), "One pair of pants is guaranteed after the outfit roll");
        Check(branches.All(b => b.Groups.Single(g => g.Forms.Contains(pieces[6].FormId)) is { Mode: "Optional", Chance: 50 }), "Shared belt complements either color at its own inclusion chance");
        Check(branches.All(b => b.Groups.SelectMany(g => g.Forms).Select(f => pieces.Single(p => p.FormId == f)).All(p => p.Variant == OutfitVariantPlan.Unmarked || p.Variant == b.Color)), "A color branch cannot contain a different marked color");
        Check(OutfitPartPresets.Errors(config).Count == 0, "Known garment alternatives are valid rather than reported as slot conflicts");
        var folder = Path.Combine(parent, "matching-variants");
        var result = PatchGenerator.Generate([config], folder, "SPID");
        var manifest = PatchInventoryService.ReadManifest(folder);
        Check(manifest.SchemaVersion == 6 && manifest.Entries.Single().MatchVariants, "Matching plans survive manifest serialization");
        using (var mod = SkyrimMod.CreateFromBinaryOverlay(new Noggog.FilePath(result.PluginPath), SkyrimRelease.SkyrimSE))
        {
            var root = mod.LeveledItems.Single(l => l.FormKey == mod.Outfits.Single().Items!.Single().FormKey);
            Check(root.Flags == 0 && root.Entries!.Count == 2 && root.ChanceNone == 0, "The outfit chooses exactly one color with no empty result");
            var colorLists = root.Entries!.Select(e => mod.LeveledItems.Single(l => l.FormKey == e.Data!.Reference.FormKey)).ToList();
            Check(colorLists.All(l => l.Flags == LeveledItem.Flag.UseAll && l.Entries!.Count == 4), "Each color bundle includes all four garment/accessory groups");
            var pants = mod.LeveledItems.Where(l => l.Entries!.Any(e => e.Data!.Reference.FormKey.ID == pieces[4].LocalId && e.Data.Reference.FormKey.ModKey.FileName == source.Plugin)).ToList();
            Check(pants.Count == 2 && pants.All(l => l.Flags == 0 && l.ChanceNone == 0 && l.Entries!.Count == 2), "Pants lists choose one normal/torn alternative, never both and never none");
        }
        Check(PatchHealthService.Inspect(folder, manifest, [source]).Values.Single().Status == "Ready", "Health checks understand nested alternatives");
        var validEsp = File.ReadAllBytes(result.PluginPath);
        var corrupt = SkyrimMod.CreateFromBinary(new ModPath(new Noggog.FilePath(result.PluginPath)), SkyrimRelease.SkyrimSE);
        corrupt.LeveledItems.First(l => l.Entries!.Count == 2 && l.Flags == 0).Flags = LeveledItem.Flag.UseAll;
        corrupt.WriteToBinary(result.PluginPath);
        Check(PatchHealthService.Inspect(folder, manifest, [source]).Values.Single().Status == "Missing / unreadable ESP", "Changing a selector to Use All is detected instead of distributing all alternatives");
        File.WriteAllBytes(result.PluginPath, validEsp);
        var originalIds = ReadIds(result.PluginPath);
        PatchGenerator.Generate([config], folder, "SPID");
        Check(originalIds.SequenceEqual(ReadIds(result.PluginPath)), "Repatch reuses outfit and nested list IDs without accumulating records");
        var other = new PluginPatchOptions { Source = new() { Plugin = "Other.esp" }, OutfitName = "Other", Pieces = [new() { Plugin = "Other.esp", LocalId = 0x800, Name = "Other robe", Slots = "Body" }], TargetRules = config.TargetRules };
        other.Source.Pieces = other.Pieces;
        PatchGenerator.Generate([other], folder, "SPID");
        Check(PatchHealthService.Inspect(folder, PatchInventoryService.ReadManifest(folder), [source, other.Source]).Values.First().Status == "Ready", "Adding an unrelated outfit preserves nested variant lists");
        PatchInventoryService.Remove(folder, PatchInventoryService.ReadManifest(folder).Entries.Single(e => e.MatchVariants));
        using (var mod = SkyrimMod.CreateFromBinaryOverlay(new Noggog.FilePath(result.PluginPath), SkyrimRelease.SkyrimSE)) Check(mod.LeveledItems.Count == 0 && mod.Outfits.Count == 1, "Removing the matching outfit prunes only its nested lists");
        config.PreferredColor = "Black"; OutfitPartPresets.Apply(config, "Balanced");
        Check(OutfitVariantPlan.Create(config).Count == 1 && !pieces[1].Include && !pieces[3].Include, "Explicit color choice retains shared parts and excludes other colors");
        pieces[4].PartMode = "Optional";
        Check(OutfitPartPresets.Errors(config).Any(e => e.Contains("same state")), "Mixed Required/Optional states within one alternative group require review");
        pieces[4].PartMode = "Required"; pieces[5].PartMode = "Required";
        config.MatchVariants = false;
        Check(OutfitPartPresets.Errors(config).Any(e => e.Contains("share body slots")), "Switching alternatives off restores the strict conflict check");
        config.MatchVariants = true;
        pieces.Add(Piece("Unrelated pants", "524288"));
        Check(OutfitPartPresets.Errors(config).Any(e => e.Contains("not recognized")), "Unknown same-slot garments are never blindly grouped");
        pieces.RemoveAt(pieces.Count - 1);
        config.PreferredColor = ""; OutfitPartPresets.Apply(config, "Balanced"); pieces[3].PartMode = "Excluded";
        Check(OutfitPartPresets.Errors(config).Any(e => e.Contains("no matching")), "Incomplete required color sets are blocked rather than mixed");
        var rogue = new List<WearableRecord> { Piece("Rogue Top [Dark]", "Body"), Piece("Rogue Top", "Body"), Piece("Rogue Boots [Dark]", "Feet"), Piece("Rogue Boots", "Feet"), Piece("Rogue Pants [Black Belt]", "524288"), Piece("Rogue Pants Torn [Black Belt]", "524288"), Piece("Rogue Pants", "524288"), Piece("Rogue Pants Torn", "524288"), Piece("Rogue Poncho", "134217728"), Piece("Rogue Hood", "Amulet, Circlet") };
        var rogueConfig = new PluginPatchOptions { Source = source, Pieces = rogue };
        OutfitPartPresets.Apply(rogueConfig, "Balanced");
        var rogueBranches = OutfitVariantPlan.Create(rogueConfig);
        Check(rogueBranches.Count == 3 && rogue.All(p => p.Include), "Rogue brackets, black-belt pants, torn versions and shared pieces all remain available");
        var dark = rogueBranches.Single(b => b.Color == "Dark");
        Check(dark.Groups.Single(g => g.Forms.Contains(rogue[0].FormId)).Forms.Count == 1 && !dark.Groups.SelectMany(g => g.Forms).Contains(rogue[1].FormId), "Dark branch selects dark top/boots instead of randomly mixing plain versions");
        Check(rogueBranches.Single(b => b.Color == "Black").Groups.Single(g => g.Forms.Contains(rogue[4].FormId)).Forms.Count == 2, "Black-belt normal/torn pants remain one guaranteed pair");
        Check(Piece("Example (dark blue)", "Body").Variant == Piece("Example Dark Blue", "Body").Variant, "Bracketed and suffix multi-word colors normalize consistently");
        var linen = new List<WearableRecord> { Piece("Basic Linen Outfit", "Body"), Piece("Basic Linen Top", "Body"), Piece("Basic Linen Pants", "524288") };
        var modular = new PluginPatchOptions { Source = source, Pieces = linen };
        OutfitPartPresets.Apply(modular, "Balanced");
        Check(!linen[0].Include && linen[1].IsRequired && linen[2].IsRequired, "Automatic presets exclude combined Outfit records when separate garments exist");
        Console.WriteLine($"Variant checks passed: {count}");
    }
    private static string[] ReadIds(string path)
    {
        using var mod = SkyrimMod.CreateFromBinaryOverlay(new Noggog.FilePath(path), SkyrimRelease.SkyrimSE);
        return mod.Outfits.Select(o => o.FormKey.ToString()).Concat(mod.LeveledItems.Select(l => l.FormKey.ToString())).Order().ToArray();
    }
}
