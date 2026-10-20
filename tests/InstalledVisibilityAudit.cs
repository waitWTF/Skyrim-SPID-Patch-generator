using System.Diagnostics;
using System.IO;
using System.Text.Json;
using WearableDistributor;

public static class InstalledVisibilityAudit
{
    public static void Run(string data, string report)
    {
        var timer = Stopwatch.StartNew();
        var result = new WearableScanService().Scan(data, new AppPreferences { DataPath = data }, data, new Stages(), CancellationToken.None);
        var suspect = result.Plugins.Single(p => p.Plugin == "Adventurers Armor Bubblegum.esp");
        var selected = new PluginPatchOptions { Source = suspect, Pieces = suspect.Pieces.Select(p => p).ToList(), Gender = "Female" };
        var before = OutfitMeshPolicy.Errors(selected).Count;
        OutfitPartPresets.Apply(selected, "Balanced");
        var found = DetectedPatchReader.Read(result.PatchFiles, result.Plugins).Where(p => p.Entry.SourcePlugin == suspect.Plugin).ToList();
        var summary = new {
            Mode = "Read-only worn-mesh and old-patch audit", ScanMilliseconds = timer.Elapsed.TotalMilliseconds,
            SuspectParts = suspect.PieceCount, FemalePartsWithMissingMeshes = before,
            VanillaMaleMeshStatus = suspect.MeshAssets!.Status("Armor\\Studded\\Male\\body_1.nif"),
            BalancedParts = selected.Pieces.Where(p => p.Include).Select(p => new { p.Name, p.FormId, p.Slots, p.Variant, p.MeshEvidence }).ToList(),
            BlackBriarMercenaries = result.Npcs.Where(n => n.Name.Contains("Black-Briar Mercenary", StringComparison.OrdinalIgnoreCase)).Select(n => new {n.FormId, n.EditorId, n.Gender, n.RaceFormId, n.HasUnresolvedTemplate, n.IsEncounterable, Variants = n.Variants.Select(v => v.Gender).ToList()}).ToList(),
            OldPatchStatus = found.Select(p => new {p.Entry.SourcePlugin, p.Entry.OutfitName, p.Health, p.CanManage, p.PresentInPlugin}).ToList(),
            Diagnostics = result.Diagnostics.Where(d => d.StartsWith("Worn mesh check:") || d.StartsWith("Cannot scan ")).ToList(),
            GameFilesWritten = false
        };
        File.WriteAllText(report, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(summary));
    }
    private sealed class Stages : IProgress<ScanProgress>
    {
        private string previous = "";
        public void Report(ScanProgress value) { if (previous == value.Stage) return; previous = value.Stage; Console.WriteLine("Stage: " + value.Stage); }
    }
}
