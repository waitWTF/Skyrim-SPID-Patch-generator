using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace WearableDistributor;

public sealed record PatchPackageResult(string ArchivePath, int OutfitCount, IReadOnlyList<string> RequiredPlugins);

/// <summary>Exports only generated patch files, ready for installation by a mod manager.</summary>
public static class PatchPackageService
{
    public static PatchPackageResult Export(string outputDirectory, string destination)
    {
        var root = PatchOutputTransaction.NormalizeRoot(outputDirectory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Choose an existing generated mod folder first.");
        if (!Path.GetExtension(destination).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a ZIP filename for the installable mod archive.");
        using var lease = PatchOutputTransaction.Acquire(root);
        PatchOutputTransaction.EnsureNoInterruptedWrite(root);
        PatchGenerator.ValidateOutputSet(root);
        var manifest = PatchInventoryService.ReadManifest(root);
        if (manifest.Entries.Count == 0) throw new InvalidOperationException("There are no generated outfits to export in this folder.");
        var relativePaths = new List<string> { PatchGenerator.OutputPluginName };
        relativePaths.AddRange(manifest.Entries.Select(entry => entry.DistributionPath.Replace('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase));
        if (manifest.Entries.Any(entry => entry.MerchantStock.Count > 0)) relativePaths.Add(MerchantStockService.RelativePath);
        if (File.Exists(PatchGenerator.ManifestPathFor(root))) relativePaths.Add(PatchGenerator.ManifestName);
        var files = relativePaths.Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(relative => relative, relative => PatchOutputTransaction.ManagedPath(root, relative), StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.Values)
            if (!File.Exists(file)) throw new FileNotFoundException("A required generated patch file is missing. Restore a backup or regenerate before exporting.", file);
        var required = manifest.Entries.SelectMany(entry => entry.RequiredMasters.Concat(entry.MerchantStock.SelectMany(rule => new[] { rule.ContainerPlugin, rule.ItemPlugin }))).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var instructions = "Skyrim SPID Patch Generator generated mod\n\nEnable one generator output for the same recipients. Keep backups when updating an existing patch.\nOptional part percentages affect NPC outfit lists, not merchant stock; resets can re-evaluate optional pieces.\n\n" +
            "Install this ZIP using Vortex's Install From File or MO2's Install a new mod from an archive. Enable the new mod and WearableDistributor.esp.\n\n" +
            "Required source/NPC plugins (install and enable separately):\n" + string.Join("\n", required.Select(plugin => "- " + plugin)) +
            "\n\nRequired distribution engines: " + string.Join(", ", manifest.Entries.Where(entry => entry.DistributeToNpcs).Select(entry => entry.Framework)
                .Concat(manifest.Entries.Any(entry => entry.MerchantStock.Count > 0) ? new[] { "SkyPatcher (container patching)" } : []).Distinct(StringComparer.OrdinalIgnoreCase)) +
            "\nInstall the selected engine and its requirements separately. This archive contains generated patch records and configuration only; meshes and textures come from the source mods.\n" +
            "Rescan and regenerate after changing NPC mods or the active profile. SPID chance is per outfit and NPC; 100% does not prevent another mod from replacing the outfit.\n";
        if (manifest.Entries.Any(entry => entry.MerchantStock.Count > 0)) instructions += "\nMERCHANDISE REQUIRES SKYPATCHER even when the NPC outfit engine is SPID. Enable container patching (iEnableContainerPatching=1 in your installed SkyPatcher.ini). Keep the generated container INI installed. Adds one of each selected item to confirmed vendor stock; clothing traders receive no weapons. Existing saves may require a normal merchant restock.\n";
        var destinationPath = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var stagedArchive = destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var expectedHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var stream = new FileStream(stagedArchive, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    foreach (var (relative, path) in files)
                    {
                        using var source = File.OpenRead(path);
                        expectedHashes[relative] = Convert.ToHexString(SHA256.HashData(source));
                        source.Position = 0;
                        using var target = archive.CreateEntry(relative, CompressionLevel.Optimal).Open();
                        source.CopyTo(target);
                    }
                    using var writer = new StreamWriter(archive.CreateEntry("README-WearableDistributor.txt", CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                    writer.Write(instructions);
                }
                stream.Flush(true);
            }
            using (var archive = ZipFile.OpenRead(stagedArchive))
            {
                foreach (var (relative, expectedHash) in expectedHashes)
                {
                    using var content = (archive.GetEntry(relative) ?? throw new InvalidDataException("An expected file was not included in the mod archive.")).Open();
                    if (Convert.ToHexString(SHA256.HashData(content)) != expectedHash)
                        throw new InvalidDataException("Mod archive verification failed; the existing archive was preserved.");
                }
            }
            File.Move(stagedArchive, destinationPath, true);
            return new PatchPackageResult(destinationPath, manifest.Entries.Count, required);
        }
        finally { if (File.Exists(stagedArchive)) File.Delete(stagedArchive); }
    }
}
