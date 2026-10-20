using System.IO;
using System.Text.Json;
using WearableDistributor;

public static class MeshRepairAudit
{
    public static void Run(string data, string report)
    {
        var result = new WearableScanService().Scan(data, new AppPreferences { DataPath = data }, data, null, CancellationToken.None);
        var patched = DetectedPatchReader.Read(result.PatchFiles, result.Plugins).Select(p => p.Entry.SourcePlugin).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var affected = result.Plugins.Where(p => p.CanPatch && (!p.IsFiltered || patched.Contains(p.Plugin))).Select(plugin => new
        {
            plugin.Plugin,
            Missing = plugin.Pieces.SelectMany(piece => new[] { "Female", "Male" }.SelectMany(sex => OutfitMeshPolicy.Paths(piece, sex)))
                .Distinct(StringComparer.OrdinalIgnoreCase).Where(path => plugin.MeshAssets!.Status(path) is "Missing" or "Empty")
                .Select(path => new { Relative = MeshAssetIndex.Normalize(path), BuiltNestedCopy = plugin.MeshAssets!.HasExtraMeshesCopy(path) }).ToList()
        }).Where(plugin => plugin.Missing.Count > 0).ToList();
        File.WriteAllText(report, JsonSerializer.Serialize(new { GameFilesWritten = false, PluginsScanned = result.Plugins.Count, Affected = affected }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Read-only audit: {affected.Count} outfit plugins; {affected.Sum(p => p.Missing.Count)} missing paths; {affected.Sum(p => p.Missing.Count(m => m.BuiltNestedCopy))} nested built copies.");
    }
}
