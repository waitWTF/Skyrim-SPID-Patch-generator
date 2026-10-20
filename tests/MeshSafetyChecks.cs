using System.IO;
using WearableDistributor;

public static class MeshSafetyChecks
{
    public static void Run(string parent)
    {
        var root = Path.Combine(parent, "mesh-safety"); Directory.CreateDirectory(root);
        var assets = new MeshAssetIndex(root); assets.RefreshArchives();
        var source = new PluginEntry { Plugin = "TestOutfit.esp", MeshAssets = assets };
        var piece = new WearableRecord { Plugin = source.Plugin, LocalId = 0x800, Name = "Armor Black", Slots = "Body", WornModels = [new("test\\body_1.nif", "test\\female_0.nif", false, true)] };
        source.Pieces.Add(piece);
        var cfg = new PluginPatchOptions { Source = source, Pieces = source.Pieces, Gender = "Female" };
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); Console.WriteLine("PASS mesh safety: " + message); }
        Check(OutfitMeshPolicy.Errors(cfg).Count == 1, "Missing female meshes are caught even when NPC sex is Female");
        Directory.CreateDirectory(Path.Combine(root, "meshes", "meshes", "test"));
        File.WriteAllBytes(Path.Combine(root, "meshes", "meshes", "test", "female_0.nif"), [1]);
        Check(OutfitMeshPolicy.Errors(cfg).Single().Contains("meshes\\meshes"), "Misplaced BodySlide output is diagnosed for any outfit");
        Check(OutfitMeshPolicy.Details(cfg).Single().Contains("Built copy found"), "Expected paths and misplaced built files are kept in expandable details");
        Directory.CreateDirectory(Path.Combine(root, "meshes", "test"));
        void Put(string path) => File.WriteAllBytes(Path.Combine(root, "meshes", "test", path), [1, 2, 3]);
        Put("female_0.nif");
        Check(OutfitMeshPolicy.Missing(piece, assets, "Female").Single().Contains("female_1.nif"), "Weight-slider pair must both be installed");
        Put("female_1.nif");
        Check(OutfitMeshPolicy.Errors(cfg).Count == 0, "Deploying the missing files clears the check without a stale cache");
        cfg.Gender = "Male";
        Check(OutfitMeshPolicy.Errors(cfg).Count == 1, "Male mesh availability is checked separately");
        Put("body_1.nif");
        Check(OutfitMeshPolicy.Errors(cfg).Count == 0, "Unweighted single mesh needs no invented weight partner");
        cfg.Gender = "Any";
        Check(OutfitMeshPolicy.Errors(cfg).Count == 0, "Both-sex mesh paths pass when present");
        var generatedFolder = Path.Combine(root, "output");
        cfg.OutfitName = "Test outfit";
        cfg.TargetRules = [new DistributionTargetRule { Group = "Test NPCs", Filters = [new KeywordChoice { Plugin = "Skyrim.esm", LocalId = 1, EditorId = "TestFaction", TargetKind = "Faction", Category = "Faction" }] }];
        var output = PatchGenerator.Generate([cfg], generatedFolder, "SPID");
        var originalEsp = File.ReadAllBytes(output.PluginPath);
        Check(PatchHealthService.Inspect(generatedFolder, PatchInventoryService.ReadManifest(generatedFolder), [source]).Values.Single().Status == "Ready", "Current mesh evidence is included in patch health checks");
        File.WriteAllBytes(Path.Combine(root, "meshes", "test", "female_0.nif"), []);
        Check(OutfitMeshPolicy.Errors(cfg).Count == 1, "Empty loose mesh is not treated as usable");
        Check(PatchHealthService.Inspect(generatedFolder, PatchInventoryService.ReadManifest(generatedFolder), [source]).Values.Single().Status == "Missing worn meshes", "Existing output is flagged when a worn mesh becomes unavailable");
        Check(DetectedPatchReader.Read([output.PluginPath], [source]).Single().Health == "Missing worn meshes", "Detected-patch overview carries the worn-mesh warning");
        var rejected = false;
        try { PatchGenerator.Generate([cfg], generatedFolder, "SPID"); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && originalEsp.SequenceEqual(File.ReadAllBytes(output.PluginPath)), "Unsafe repatch is rejected before the previous ESP is touched");
        Put("female_0.nif"); File.Delete(Path.Combine(root, "meshes", "test", "female_1.nif"));
        Check(OutfitPartPresets.Errors(cfg).Any(e => e.Contains("worn meshes unavailable", StringComparison.OrdinalIgnoreCase)), "Mesh errors reach the generator's part validation");
        cfg.Gender = "Male"; cfg.DistributeToNpcs = false;
        Check(OutfitMeshPolicy.Errors(cfg).Count == 1, "Merchants-only stock checks supported female meshes regardless of the unused NPC sex field");
        cfg.DistributeToNpcs = true; cfg.MerchantCategories = [MerchantStockService.GeneralGoods];
        Check(OutfitMeshPolicy.Errors(cfg).Count == 1, "Shop stock beside male NPC distribution still validates meshes for female buyers");
        cfg.MerchantCategories.Clear(); cfg.Gender = "Any";
        var staging = new MeshAssetIndex(root, false); staging.RefreshArchives();
        Check(staging.Status("absent.nif") == "Unknown", "A staging scan cannot prove files absent from a runtime virtual Data");
        Check(MeshAssetIndex.Normalize("../outside.nif") is null && MeshAssetIndex.Normalize("C:\\outside.nif") is null && MeshAssetIndex.Normalize("meshes\\test\\body_1.nif") == "meshes\\test\\body_1.nif", "Mesh paths are normalized and traversal/absolute paths rejected");
        var badArchive = Path.Combine(root, "Corrupt.bsa"); File.WriteAllBytes(badArchive, [0, 1, 2, 3]); assets.RefreshArchives();
        Check(assets.Status("absent.nif") == "Unknown", "Unreadable archives produce uncertainty rather than false missing-file claims");
        File.Delete(badArchive); assets.RefreshArchives();
        Check(assets.Status("absent.nif") == "Missing", "Archive checks recover after a damaged archive is removed");

        var pieces = new[] {
            new WearableRecord { Plugin = source.Plugin, LocalId = 1, Name = "Boots Red", Slots = "Feet" },
            new WearableRecord { Plugin = source.Plugin, LocalId = 2, Name = "Armor Black", Slots = "Body" },
            new WearableRecord { Plugin = source.Plugin, LocalId = 3, Name = "Boots Black", Slots = "Feet" },
            new WearableRecord { Plugin = source.Plugin, LocalId = 4, Name = "Armor Full Black", Slots = "Body" } };
        var selected = OutfitPartSelection.FindCompatibleSet(pieces);
        Check(selected.Count == 2 && selected.All(p => p.Variant == "Black"), "Plain color suffixes match the central garment and exclude competing full-body alternatives");
        var linen = new[] { new WearableRecord { Plugin = source.Plugin, LocalId = 10, Name = "Basic Linen Outfit", Slots = "Body" }, new WearableRecord { Plugin = source.Plugin, LocalId = 11, Name = "Basic Linen Top", Slots = "Body" }, new WearableRecord { Plugin = source.Plugin, LocalId = 12, Name = "Basic Linen Pants", Slots = "524288" } };
        Check(OutfitPartSelection.FindCompatibleSet(linen).Select(p => p.LocalId).ToHashSet().SetEquals([11u, 12u]), "Separate garments are preferred over a combined Outfit record");
        Check(OutfitPartSelection.FindCompatibleSet(linen.Take(1)).Count == 1, "A sole combined outfit remains eligible");
        var unknown = new WearableRecord { Name = "Unmarked modular part", Slots = "524288" };
        Check(unknown.Variant == "Standard / unmarked" && !OutfitPartPresets.IsOptional(unknown), "Unknown modular garments remain required");
    }
}
