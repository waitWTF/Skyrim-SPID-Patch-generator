using System.IO;
using WearableDistributor;

public static class InventoryHealthChecks
{
    public static void Run(string root)
    {
        var manifest = PatchInventoryService.ReadManifest(root);
        var health = PatchHealthService.Inspect(root, manifest);
        Check(health.Values.All(value => value.Status is "Ready" or "Legacy details"), "valid existing merged output passes file-health inspection");
        var distributionPath = Path.Combine(root, PatchGenerator.OutputIniName);
        var saved = File.ReadAllBytes(distributionPath);
        File.Delete(distributionPath);
        try { Check(PatchHealthService.Inspect(root, manifest).Values.All(value => value.Status == "Missing INI"), "manifest does not conceal missing distribution output"); }
        finally { File.WriteAllBytes(distributionPath, saved); }
        File.WriteAllLines(distributionPath, File.ReadAllLines(distributionPath).Where(line => !line.StartsWith("Outfit = ", StringComparison.Ordinal)));
        try { Check(PatchHealthService.Inspect(root, manifest).Values.All(value => value.Status is "Rules need repair" or "Legacy details"), "existing but truncated distribution files need repair in the overview"); }
        finally { File.WriteAllBytes(distributionPath, saved); }
        Check(PatchHealthService.Inspect(root, manifest, []).Values.All(value => value.Status is "Source not scanned" or "Legacy details"), "source records must be checked against current scan before reporting them verified");
        Console.WriteLine("Patch inventory health checks passed.");
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Inventory health check failed: " + message);
    }
}
