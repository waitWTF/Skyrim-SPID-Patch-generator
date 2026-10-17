using System.IO;
using System.IO.Compression;
using WearableDistributor;

public static class PackageChecks
{
    public static void Run(string root)
    {
        var destination = Path.Combine(root, "exported-mod.zip");
        var result = PatchPackageService.Export(root, destination);
        using (var archive = ZipFile.OpenRead(destination))
        {
            Check(archive.GetEntry(PatchGenerator.OutputPluginName) != null, "ESP is at the archive root for mod-manager installation");
            Check(archive.GetEntry(PatchGenerator.OutputIniName) != null, "SPID INI stays alongside ESP");
            Check(archive.GetEntry(PatchGenerator.ManifestName) != null, "patch inventory accompanies generated mod");
            Check(!archive.Entries.Any(entry => entry.FullName.EndsWith(".lock") || entry.FullName.Contains("Backups")), "working and backup files are excluded");
            using var esp = archive.GetEntry(PatchGenerator.OutputPluginName)!.Open();
            using var memory = new MemoryStream(); esp.CopyTo(memory);
            Check(memory.ToArray().SequenceEqual(File.ReadAllBytes(Path.Combine(root, PatchGenerator.OutputPluginName))), "exported ESP bytes match validated output");
        }
        Check(result.OutfitCount == PatchInventoryService.ReadManifest(root).Entries.Count, "archive reports all merged outfits");
        var invalid = false;
        try { PatchPackageService.Export(root, Path.Combine(root, "must-not-be-esp.esp")); }
        catch (InvalidDataException) { invalid = true; }
        Check(invalid && !File.Exists(Path.Combine(root, "must-not-be-esp.esp")), "invalid archive filename fails without output mutation");
        Console.WriteLine("Mod archive export checks passed.");
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Package check failed: " + message);
    }
}
