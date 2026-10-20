using System.IO;
using System.Text.Json;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using WearableDistributor;

public static class InstalledVariantAudit
{
    public static void Run(string data, string report, string[] names)
    {
        var results = new List<object>();
        foreach (var name in names)
        {
            using var mod = SkyrimMod.CreateFromBinaryOverlay(new Noggog.FilePath(Path.Combine(data, name)), SkyrimRelease.SkyrimSE);
            var plugin = new PluginEntry { Plugin = name, Pieces = mod.Armors.Where(a => !a.IsDeleted && a.FormKey.ModKey.FileName.String.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(a => new WearableRecord { Plugin = name, LocalId = a.FormKey.ID, Name = a.Name?.String ?? a.EditorID ?? "Unnamed", EditorId = a.EditorID ?? "", Slots = a.BodyTemplate?.FirstPersonFlags.ToString() ?? "None" }).ToList() };
            var config = new PluginPatchOptions { Source = plugin, Pieces = plugin.Pieces };
            OutfitPartPresets.Apply(config, "Balanced");
            var errors = OutfitPartPresets.Errors(config);
            var branches = errors.Count == 0 ? OutfitVariantPlan.Create(config) : [];
            results.Add(new { Plugin = name, Errors = errors, Parts = config.Pieces.Select(p => new { p.Name, p.Slots, p.Variant, p.PartMode, p.OptionalChance }), Branches = branches });
            Console.WriteLine($"{name}: {config.Pieces.Count(p => p.Include)}/{config.Pieces.Count} included, {branches.Count} matching color branch(es), {errors.Count} issue(s).");
            foreach (var error in errors) Console.WriteLine(error);
        }
        File.WriteAllText(report, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }
}
