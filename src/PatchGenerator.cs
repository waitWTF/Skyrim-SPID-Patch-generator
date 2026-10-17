using System.Globalization;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace WearableDistributor;

public sealed record PatchGenerationResult(string PluginPath, string DistributionPath, int OutfitCount, int DistributionCount, IReadOnlyList<string> Masters, string Framework);

public static class PatchGenerator
{
    public const string OutputPluginName = "WearableDistributor.esp";
    public const string OutputIniName = "WearableDistributor_DISTR.ini";
    public const string ManifestName = "WearableDistributor.patch.json";
    public static string StableEditorIdFor(string sourcePlugin, string? outfitName = null) => StableEditorId(sourcePlugin, outfitName);
    public static string ManifestPathFor(string outputDirectory) => Path.Combine(outputDirectory, ManifestName);

    public static PatchGenerationResult Generate(IEnumerable<PluginPatchOptions> sourceOptions, string outputDirectory, string framework,
        IEnumerable<PluginPatchOptions>? skippedOptions = null)
    {
        if (!framework.Equals("SPID", StringComparison.OrdinalIgnoreCase) && !framework.Equals("SkyPatcher", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose SPID or SkyPatcher as the distribution framework.");
        var useSkyPatcher = framework.Equals("SkyPatcher", StringComparison.OrdinalIgnoreCase);
        var configurations = sourceOptions.ToList();
        if (configurations.Count == 0) throw new InvalidOperationException("No plugin configurations were provided.");

        var outfits = configurations
            .Select(configuration => new
            {
                Configuration = configuration,
                Pieces = configuration.Pieces.Where(piece => piece.Include).ToList(),
                Rules = configuration.TargetRules.Select(rule => rule.Filters).ToList(),
                Chance = ParseChance(configuration.Chance)
            })
            .Where(item => item.Pieces.Count > 0)
            .ToList();
        if (outfits.Count == 0) throw new InvalidOperationException("Include at least one outfit piece before generating the patch.");
        var duplicateOutfitIds = outfits.GroupBy(item => StableEditorId(item.Configuration.Source.Plugin, item.Configuration.OutfitName), StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
        if (duplicateOutfitIds.Count > 0) throw new InvalidOperationException("Two named outfits resolve to the same generated record ID. Rename one outfit variant and try again.");

        foreach (var item in outfits)
        {
            if (item.Rules.Count == 0 || item.Rules.Any(rule => rule.Count == 0)) throw new InvalidOperationException($"{item.Configuration.Source.Plugin} has an audience with no NPC target. Choose at least one group.");
            if (useSkyPatcher && item.Chance != 100)
                throw new InvalidOperationException("SkyPatcher's NPC outfit rules do not support a per-NPC chance. Choose 100% for a full outfit assignment, or select SPID in Preferences to use the configured chance.");
            ValidateConfiguration(item.Configuration);
            _ = MerchantStockService.Build(item.Configuration);
        }

        outputDirectory = PatchOutputTransaction.NormalizeRoot(outputDirectory);
        using var outputLease = PatchOutputTransaction.Acquire(outputDirectory);
        PatchOutputTransaction.EnsureNoInterruptedWrite(outputDirectory);
        var previousManifest = PatchInventoryService.ReadManifest(outputDirectory);
        var skipped = skippedOptions?.ToList() ?? [];
        var preservedIds = skipped.Select(config => StableEditorId(config.Source.Plugin, config.OutfitName))
            .Concat(previousManifest.Entries.Where(entry => skipped.Any(config =>
                config.Source.Plugin.Equals(entry.SourcePlugin, StringComparison.OrdinalIgnoreCase) &&
                config.OutfitName.Equals(entry.OutfitName, StringComparison.OrdinalIgnoreCase))).Select(entry => entry.OutfitEditorId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // A skipped variant must survive even when another variant of its plugin is repatched.
        EnsureFrameworkIsolation(outputDirectory, previousManifest, useSkyPatcher);
        // Existence alone does not prove that a previously generated outfit
        // still has a distribution rule. Do not merge into a truncated output.
        ValidateOutputSet(outputDirectory);
        var outputPlugin = PatchOutputTransaction.ManagedPath(outputDirectory, OutputPluginName);
        var relativeIniPath = useSkyPatcher
            ? PatchOutputTransaction.SkyPatcherIniPath.Replace('/', Path.DirectorySeparatorChar)
            : OutputIniName;
        var outputIni = PatchOutputTransaction.ManagedPath(outputDirectory, relativeIniPath);
        var manifestPath = PatchOutputTransaction.ManagedPath(outputDirectory, ManifestName);
        var tempDirectory = Path.Combine(outputDirectory, $".WearableDistributor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var tempPlugin = Path.Combine(tempDirectory, OutputPluginName);
        var tempIni = Path.Combine(tempDirectory, relativeIniPath);

        try
        {
            var selectedMasters = outfits.SelectMany(item => item.Pieces)
                .Select(piece => ModKey.FromFileName(piece.Plugin))
                .Where(key => !string.Equals(key.FileName, OutputPluginName, StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .OrderBy(key => key.FileName.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var outputModKey = ModKey.FromFileName(OutputPluginName);
            var outputMod = File.Exists(outputPlugin)
                ? SkyrimMod.CreateFromBinary(new ModPath(new Noggog.FilePath(outputPlugin)), SkyrimRelease.SkyrimSE)
                : new SkyrimMod(outputModKey, SkyrimRelease.SkyrimSE);
            // Empty older outputs can store NextFormID=0. The allocator trusts
            // that header, so enforce the normal Skyrim range and avoid existing
            // records even if the saved counter was stale.
            var mutableMod = (IMod)outputMod;
            var highestOutfitId = outputMod.Outfits.Where(outfit => outfit.FormKey.ModKey == outputModKey)
                .Select(outfit => outfit.FormKey.ID).DefaultIfEmpty(0u).Max();
            mutableMod.NextFormID = Math.Max(Math.Max(mutableMod.NextFormID, 0x800u), checked(highestOutfitId + 1));
            if (mutableMod.NextFormID > 0xFFFFFF)
                throw new InvalidDataException("The generated plugin has exhausted its local FormID range. Choose a new output folder.");
            outputMod.ModHeader.Author = "Skyrim SPID Patch Generator";
            outputMod.ModHeader.Description = useSkyPatcher ? "Generated NPC outfits. Keep enabled with the generated Skyrim SPID Patch Generator SkyPatcher rules." :
                "Generated NPC outfits. Keep enabled with WearableDistributor_DISTR.ini.";
            foreach (var master in selectedMasters)
                if (!outputMod.ModHeader.MasterReferences.Any(reference => reference.Master == master))
                    outputMod.ModHeader.MasterReferences.Add(new MasterReference { Master = master });

            Directory.CreateDirectory(Path.GetDirectoryName(tempIni)!);
            var lines = new List<string>
            {
                "; Generated by Skyrim SPID Patch Generator.",
                useSkyPatcher
                    ? "; SkyPatcher NPC patch. Chance is not supported; rules assign the default outfit to every matching NPC."
                    : "; SPID patch. Keep this file and WearableDistributor.esp installed and enabled together.",
                "; Audiences are combined as alternatives. Required categories within an audience use AND.",
                ""
            };
            var replacingPlugins = configurations.Select(configuration => configuration.Source.Plugin).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var distributionCount = 0;
            var generatedEntries = new List<PatchManifestEntry>();
            foreach (var item in outfits)
            {
                var editorId = StableEditorId(item.Configuration.Source.Plugin, item.Configuration.OutfitName);
                var outfit = outputMod.Outfits.FirstOrDefault(existing => string.Equals(existing.EditorID, editorId, StringComparison.OrdinalIgnoreCase));
                if (outfit is null)
                {
                    outfit = new Outfit(outputMod, editorId);
                    outputMod.Outfits.Add(outfit);
                }
                outfit.Items = new Noggog.ExtendedList<IFormLinkGetter<IOutfitTargetGetter>>();
                var outfitItems = outfit.Items;
                foreach (var piece in item.Pieces.DistinctBy(piece => piece.FormId, StringComparer.OrdinalIgnoreCase))
                {
                    var sourceKey = new FormKey(ModKey.FromFileName(piece.Plugin), piece.LocalId);
                    outfitItems.Add(new FormLink<IOutfitTargetGetter>(sourceKey));
                }

                var distributionCountBefore = distributionCount;
                lines.Add($"; {item.Configuration.Source.Plugin} — {item.Pieces.Count} piece(s)");
                lines.Add($"; Outfit: {item.Configuration.OutfitName}");
                var compiled = DistributionRuleCompiler.Compile(item.Configuration, outfit.FormKey.ID, useSkyPatcher);
                lines.AddRange(compiled);
                distributionCount += compiled.Count(line => line.StartsWith("Outfit = ", StringComparison.Ordinal) || line.Contains("outfitDefault=", StringComparison.Ordinal));
                generatedEntries.Add(new PatchManifestEntry
                {
                    SourcePlugin = item.Configuration.Source.Plugin,
                    OutfitName = item.Configuration.OutfitName,
                    OutfitEditorId = editorId,
                    Framework = useSkyPatcher ? "SkyPatcher" : "SPID",
                    DistributionPath = relativeIniPath,
                    IncludedPieces = item.Pieces.Select(piece => $"{piece.Name} [{piece.FormId}]").ToList(),
                    ExcludedPieces = item.Configuration.Pieces.Where(piece => !piece.Include).Select(piece => $"{piece.Name} [{piece.FormId}]").ToList(),
                    RequiredMasters = item.Pieces.Select(piece => piece.Plugin).Concat(DistributionRuleCompiler.RequiredTargetPlugins(item.Configuration)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(plugin => plugin, StringComparer.OrdinalIgnoreCase).ToList(),
                    Audiences = item.Configuration.TargetRules.Count > 0
                        ? item.Configuration.TargetRules.Select(rule => $"{rule.Collection} / {rule.Group}: {string.Join(", ", rule.Filters.Select(target => target.EditorId))}").ToList()
                        : [string.Join(", ", item.Configuration.Keywords.Select(target => target.EditorId))],
                    Chance = item.Chance,
                    Gender = item.Configuration.Gender,
                    MerchantStock = MerchantStockService.Build(item.Configuration),
                    DistributionRows = distributionCount - distributionCountBefore,
                    GeneratedAtUtc = DateTime.UtcNow
                });
                lines.Add("");
            }

            foreach (var legacyOutfit in outputMod.Outfits.Where(existing => !preservedIds.Contains(existing.EditorID ?? "") && replacingPlugins.Any(plugin =>
                         existing.EditorID is { } id &&
                         System.Text.RegularExpressions.Regex.IsMatch(id, $"^WD_[0-9]{{4}}_{System.Text.RegularExpressions.Regex.Escape(SanitizeEditorId(Path.GetFileNameWithoutExtension(plugin)))}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant))).ToList())
                outputMod.Outfits.Remove(legacyOutfit);
            var activeOutfitIds = generatedEntries.Select(entry => entry.OutfitEditorId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var removedIds = previousManifest.Entries
                .Where(entry => replacingPlugins.Contains(entry.SourcePlugin) && !preservedIds.Contains(entry.OutfitEditorId) && entry.Framework.Equals(framework, StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.OutfitEditorId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var retainedIds = previousManifest.Entries
                .Where(entry => !replacingPlugins.Contains(entry.SourcePlugin) || preservedIds.Contains(entry.OutfitEditorId) || !entry.Framework.Equals(framework, StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.OutfitEditorId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var staleOutfit in outputMod.Outfits.Where(existing => existing.EditorID is { } id &&
                         !activeOutfitIds.Contains(id) && !retainedIds.Contains(id) && (removedIds.Contains(id) ||
                         replacingPlugins.Any(plugin => string.Equals(id, StableEditorId(plugin), StringComparison.OrdinalIgnoreCase)))).ToList())
                outputMod.Outfits.Remove(staleOutfit);

            var existingIniLines = File.Exists(outputIni) ? File.ReadAllLines(outputIni).ToList() : [];
            var mergedLines = MergeIni(existingIniLines, lines, replacingPlugins, preservedIds);
            var mergedDistributionCount = mergedLines.Count(line => line.StartsWith("Outfit = ", StringComparison.Ordinal) || line.Contains("outfitDefault=", StringComparison.Ordinal));
            var existingManifest = previousManifest;
            if (generatedEntries.Any(entry => entry.MerchantStock.Count > 0)) existingManifest.SchemaVersion = 3;
            foreach (var entry in generatedEntries)
                entry.RequiredMasters = entry.RequiredMasters.Concat(entry.MerchantStock.SelectMany(rule => new[] { rule.ContainerPlugin, rule.ItemPlugin })).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
            existingManifest.Framework = useSkyPatcher ? "SkyPatcher" : "SPID";
            existingManifest.OutputPlugin = OutputPluginName;
            existingManifest.DistributionPath = relativeIniPath;
            existingManifest.GeneratedAtUtc = DateTime.UtcNow;
            existingManifest.Entries = existingManifest.Entries.Where(entry => !replacingPlugins.Contains(entry.SourcePlugin) || preservedIds.Contains(entry.OutfitEditorId) || !entry.Framework.Equals(existingManifest.Framework, StringComparison.OrdinalIgnoreCase)).Concat(generatedEntries).OrderBy(entry => entry.SourcePlugin, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Framework, StringComparer.OrdinalIgnoreCase).ToList();
            var tempManifest = Path.Combine(tempDirectory, ManifestName);
            var tempMerchandise = MerchantStockService.Stage(tempDirectory, existingManifest);
            File.WriteAllText(tempManifest, JsonSerializer.Serialize(existingManifest, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            var expectedRecordItems = outputMod.Outfits.Where(outfit => !string.IsNullOrWhiteSpace(outfit.EditorID))
                .ToDictionary(outfit => outfit.EditorID!, outfit => (outfit.Items?.Select(item => item.FormKey) ?? []).ToHashSet(), StringComparer.OrdinalIgnoreCase);

            outputMod.BeginWrite.ToPath(tempPlugin).WithNoLoadOrder().NoNextFormIDProcessing().Write();
            File.WriteAllText(tempIni, string.Join(Environment.NewLine, mergedLines), new UTF8Encoding(false));
            ValidateGeneratedPlugin(tempPlugin, expectedRecordItems, selectedMasters);
            ValidateGeneratedIni(tempIni, mergedDistributionCount, useSkyPatcher, outputMod.Outfits.Select(outfit => outfit.FormKey.ID).ToHashSet());
            ValidateOutputSet(tempDirectory);

            PatchOutputTransaction.Commit(outputDirectory, [tempPlugin, tempIni, tempManifest, tempMerchandise], [outputPlugin, outputIni, manifestPath, PatchOutputTransaction.ManagedPath(outputDirectory, MerchantStockService.RelativePath)], "Generate");
            return new PatchGenerationResult(outputPlugin, outputIni, existingManifest.Entries.Count, mergedDistributionCount, outputMod.ModHeader.MasterReferences.Select(reference => reference.Master.FileName.ToString()).ToList(), useSkyPatcher ? "SkyPatcher" : "SPID");
        }
        finally
        {
            try { Directory.Delete(tempDirectory, true); } catch { }
        }
    }

    private static int ParseChance(string raw)
    {
        var normalized = raw.Trim().TrimEnd('%');
        if (!int.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out var chance) || chance is < 1 or > 100)
            throw new InvalidOperationException($"Distribution chance must be a whole number from 1 to 100. Received: '{raw}'.");
        return chance;
    }

    private static void ValidateConfiguration(PluginPatchOptions configuration)
    {
        if (!configuration.Source.CanPatch)
            throw new InvalidOperationException($"{configuration.Source.Plugin} cannot be patched: {configuration.Source.ValidationIssue}");
        DistributionRuleCompiler.ValidatePluginName(configuration.Source.Plugin);
        if (configuration.Source.Plugin.Equals(OutputPluginName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The generated Skyrim SPID Patch Generator plugin cannot be used as an outfit source.");
        if (configuration.OutfitName.IndexOfAny(['\r', '\n']) >= 0)
            throw new InvalidOperationException("An outfit name cannot contain line breaks.");
        if (configuration.Gender is not ("Any" or "Male" or "Female"))
            throw new InvalidOperationException("Choose Any, Male, or Female for NPC sex.");
        foreach (var piece in configuration.Pieces.Where(piece => piece.Include))
        {
            DistributionRuleCompiler.ValidatePluginName(piece.Plugin);
            if (piece.LocalId is 0 or > 0xFFFFFF || piece.Plugin.Equals(OutputPluginName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Outfit piece '{piece.Name}' has an invalid source record.");
        }
        DistributionRuleCompiler.Validate(configuration);
    }

    private static void EnsureFrameworkIsolation(string outputDirectory, PatchManifest manifest, bool useSkyPatcher)
    {
        var framework = useSkyPatcher ? "SkyPatcher" : "SPID";
        var oppositeIni = PatchOutputTransaction.ManagedPath(outputDirectory, useSkyPatcher ? OutputIniName : PatchOutputTransaction.SkyPatcherIniPath);
        var oppositeHasRules = File.Exists(oppositeIni) && File.ReadLines(oppositeIni).Any(line => !line.TrimStart().StartsWith(';') &&
            (line.TrimStart().StartsWith("Outfit", StringComparison.OrdinalIgnoreCase) || line.Contains("outfitDefault=", StringComparison.OrdinalIgnoreCase)));
        if (manifest.Entries.Any(entry => !entry.Framework.Equals(framework, StringComparison.OrdinalIgnoreCase)) || oppositeHasRules)
            throw new InvalidOperationException("This output folder already contains a patch for the other distribution framework. Generate the alternate framework in a new output mod folder, and enable only one Skyrim SPID Patch Generator output mod at a time because both use WearableDistributor.esp.");
        if (manifest.Entries.Count > 0 && !File.Exists(PatchOutputTransaction.ManagedPath(outputDirectory, OutputPluginName)))
            throw new InvalidDataException("The existing patch inventory is missing WearableDistributor.esp. Restore a backup or choose a new output folder before generating.");
        if (manifest.Entries.Any(entry => !File.Exists(PatchOutputTransaction.ManagedPath(outputDirectory, entry.DistributionPath))))
            throw new InvalidDataException("The existing patch inventory is missing its distribution INI. Restore a backup or choose a new output folder before generating.");
    }

    private static string SanitizeEditorId(string source)
    {
        var safe = new string(source.Select(character => char.IsAsciiLetterOrDigit(character) || character == '_' ? character : '_').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "Outfit" : safe.Length > 48 ? safe[..48] : safe;
    }

    private static string StableEditorId(string sourcePlugin, string? outfitName = null)
    {
        var label = Path.GetFileNameWithoutExtension(sourcePlugin);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourcePlugin.ToUpperInvariant())))[..8];
        var isVariant = !string.IsNullOrWhiteSpace(outfitName) && !outfitName.Equals(label, StringComparison.OrdinalIgnoreCase);
        var safeLabel = SanitizeEditorId(label);
        var nameSuffix = "";
        if (isVariant)
        {
            safeLabel = safeLabel[..Math.Min(safeLabel.Length, 24)];
            var safeName = SanitizeEditorId(outfitName!);
            nameSuffix = "_" + safeName[..Math.Min(safeName.Length, 24)];
        }
        return $"WD_{safeLabel}_{hash}{nameSuffix}";
    }

    private static List<string> MergeIni(IReadOnlyList<string> existing, IReadOnlyList<string> generated, IReadOnlySet<string> replacingPlugins, IReadOnlySet<string> preservedIds)
    {
        if (existing.Count == 0) return generated.ToList();
        var kept = new List<string>();
        for (var i = 0; i < existing.Count;)
        {
            var line = existing[i];
            var marker = line.StartsWith("; ", StringComparison.Ordinal) ? line[2..] : "";
            var sourcePlugin = replacingPlugins.FirstOrDefault(plugin => marker.StartsWith(plugin + " — ", StringComparison.OrdinalIgnoreCase) && marker.EndsWith(" piece(s)", StringComparison.Ordinal));
            var outfitName = i + 1 < existing.Count && existing[i + 1].StartsWith("; Outfit: ", StringComparison.Ordinal)
                ? existing[i + 1][10..] : sourcePlugin is null ? "" : Path.GetFileNameWithoutExtension(sourcePlugin);
            var replacedSection = sourcePlugin is not null && !preservedIds.Contains(StableEditorId(sourcePlugin, outfitName));
            if (replacedSection)
            {
                i++;
                while (i < existing.Count && !IsSectionHeader(existing[i])) i++;
                while (i < existing.Count && string.IsNullOrWhiteSpace(existing[i])) i++;
                continue;
            }
            kept.Add(line);
            i++;
        }
        while (kept.Count > 0 && string.IsNullOrWhiteSpace(kept[^1])) kept.RemoveAt(kept.Count - 1);
        kept.Add("");
        kept.AddRange(generated.Skip(4));
        return kept;
    }

    private static bool IsSectionHeader(string line) => line.StartsWith("; ", StringComparison.Ordinal) &&
        line.Contains(" — ", StringComparison.Ordinal) && line.EndsWith(" piece(s)", StringComparison.Ordinal);

    private static void ValidateGeneratedPlugin(string path, IReadOnlyDictionary<string, HashSet<FormKey>> expectedOutfits, IReadOnlyCollection<ModKey> expectedMasters)
    {
        using var readBack = SkyrimMod.CreateFromBinaryOverlay(new Noggog.FilePath(path), SkyrimRelease.SkyrimSE);
        if (readBack.Outfits.Count != expectedOutfits.Count)
            throw new InvalidDataException($"Generated plugin validation failed: expected {expectedOutfits.Count} outfit records, found {readBack.Outfits.Count}.");
        foreach (var (editorId, expectedItems) in expectedOutfits)
        {
            var actual = readBack.Outfits.FirstOrDefault(outfit => string.Equals(outfit.EditorID, editorId, StringComparison.OrdinalIgnoreCase));
            if (actual is null) throw new InvalidDataException($"Generated plugin is missing outfit record '{editorId}'.");
            if (!expectedItems.SetEquals(actual.Items?.Select(item => item.FormKey) ?? []))
                throw new InvalidDataException($"Generated outfit '{editorId}' did not retain all expected armor pieces.");
        }
        var missingMasters = expectedMasters.Where(master => !readBack.ModHeader.MasterReferences.Any(reference => reference.Master == master)).ToList();
        if (missingMasters.Count > 0)
            throw new InvalidDataException($"Generated plugin is missing master references: {string.Join(", ", missingMasters.Select(master => master.FileName))}.");
    }

    internal static void ValidateGeneratedIni(string path, int expectedDistributions, bool skyPatcher, IReadOnlySet<uint> outfitIds)
    {
        var activeLines = File.ReadLines(path).Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith(";", StringComparison.Ordinal)).ToList();
        var rules = activeLines.Where(line =>
            (skyPatcher ? line.Contains("outfitDefault=", StringComparison.Ordinal) : line.StartsWith("Outfit = ", StringComparison.Ordinal))).ToList();
        if (rules.Count != expectedDistributions)
            throw new InvalidDataException($"Generated distribution validation failed: expected {expectedDistributions} outfit rules, found {rules.Count}.");
        foreach (var rule in rules)
        {
            if (skyPatcher)
            {
                var fields = rule.Split(':', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Any(field => !field.Contains('=') || string.IsNullOrWhiteSpace(field[(field.IndexOf('=') + 1)..])) ||
                    fields.Count(field => field.StartsWith("outfitDefault=", StringComparison.Ordinal)) != 1)
                    throw new InvalidDataException($"Invalid SkyPatcher outfit rule: {rule}");
                foreach (var field in fields.Where(field => field.StartsWith("filterByNpcs=", StringComparison.OrdinalIgnoreCase) || field.StartsWith("outfitDefault=", StringComparison.OrdinalIgnoreCase)))
                    foreach (var value in field[(field.IndexOf('=') + 1)..].Split(','))
                        DistributionRuleCompiler.ParseSkyPatcherForm(value);
                var outfitValue = fields.Single(field => field.StartsWith("outfitDefault=", StringComparison.Ordinal))[("outfitDefault=".Length)..];
                var outfit = DistributionRuleCompiler.ParseSkyPatcherForm(outfitValue);
                if (!outfit.Plugin.Equals(OutputPluginName, StringComparison.OrdinalIgnoreCase) || !outfitIds.Contains(outfit.Id))
                    throw new InvalidDataException("A SkyPatcher rule references a generated outfit that is missing from the ESP. Repatch all affected outfits.");
            }
            else
            {
                var columns = rule[(rule.IndexOf('=') + 1)..].Trim().Split('|');
                if (columns.Length != 7 || !columns[0].Contains('~') || (columns[1] == "NONE" && columns[2] == "NONE") ||
                    !int.TryParse(columns[6], NumberStyles.None, CultureInfo.InvariantCulture, out var chance) || chance is < 1 or > 100)
                    throw new InvalidDataException($"Invalid SPID outfit rule: {rule}");
                var outfit = DistributionRuleCompiler.ParseSpidForm(columns[0]);
                if (!outfit.Plugin.Equals(OutputPluginName, StringComparison.OrdinalIgnoreCase) || !outfitIds.Contains(outfit.Id))
                    throw new InvalidDataException("A SPID rule references a generated outfit that is missing from the ESP. Repatch all affected outfits.");
            }
        }
        if (!skyPatcher)
        {
            var helperRules = activeLines.Where(line => line.StartsWith("Keyword = ", StringComparison.Ordinal) &&
                IsGeneratedHelper(line[(line.IndexOf('=') + 1)..].Trim().Split('|')[0])).ToList();
            var defined = helperRules.Select(line => line[(line.IndexOf('=') + 1)..].Trim().Split('|')[0]).ToHashSet(StringComparer.Ordinal);
            foreach (var line in helperRules.Concat(rules))
            {
                var columns = line[(line.IndexOf('=') + 1)..].Trim().Split('|');
                if (columns.Length < 2) continue;
                foreach (var dependency in columns[1].Split([',', '+'], StringSplitOptions.RemoveEmptyEntries).Where(IsGeneratedHelper))
                    if (!defined.Contains(dependency))
                        throw new InvalidDataException($"The SPID file is missing generated helper keyword '{dependency}'. Restore a backup or regenerate the complete output.");
            }
        }
    }

    private static bool IsGeneratedHelper(string value) => System.Text.RegularExpressions.Regex.IsMatch(value,
        "^WD_(?:Target|Audience)_[0-9A-F]{24}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    internal static void ValidateOutputSet(string directory)
    {
        var plugin = PatchOutputTransaction.ManagedPath(directory, OutputPluginName);
        var spid = PatchOutputTransaction.ManagedPath(directory, OutputIniName);
        var sky = PatchOutputTransaction.ManagedPath(directory, PatchOutputTransaction.SkyPatcherIniPath);
        if (!File.Exists(plugin))
        {
            if (File.Exists(spid) || File.Exists(sky) || File.Exists(ManifestPathFor(directory)) || File.Exists(PatchOutputTransaction.ManagedPath(directory, MerchantStockService.RelativePath)))
                throw new InvalidDataException("The saved output contains distribution files without their generated ESP.");
            return;
        }
        using var mod = SkyrimMod.CreateFromBinaryOverlay(new Noggog.FilePath(plugin), SkyrimRelease.SkyrimSE);
        var ids = mod.Outfits.Select(outfit => outfit.FormKey.ID).ToHashSet();
        var referencedIds = new HashSet<uint>();
        foreach (var (path, isSky) in new[] { (spid, false), (sky, true) })
            if (File.Exists(path))
            {
                var rules = File.ReadLines(path).Where(line => !line.TrimStart().StartsWith(';') &&
                    (isSky ? line.Contains("outfitDefault=", StringComparison.Ordinal) : line.StartsWith("Outfit = ", StringComparison.Ordinal))).ToList();
                ValidateGeneratedIni(path, rules.Count, isSky, ids);
                foreach (var rule in rules)
                    referencedIds.Add(isSky ? DistributionRuleCompiler.ParseSkyPatcherForm(rule.Split(':').Single(field => field.StartsWith("outfitDefault=", StringComparison.Ordinal))[14..]).Id :
                        DistributionRuleCompiler.ParseSpidForm(rule[(rule.IndexOf('=') + 1)..].Trim().Split('|')[0]).Id);
            }
        if (!ids.SetEquals(referencedIds))
            throw new InvalidDataException("The saved ESP contains an outfit without its distribution rules. Restore a backup or regenerate in a new output folder.");
        var manifest = PatchInventoryService.ReadManifest(directory);
        MerchantStockService.Validate(directory, manifest);
        var editorIds = mod.Outfits.Where(outfit => !string.IsNullOrWhiteSpace(outfit.EditorID))
            .ToDictionary(outfit => outfit.EditorID!, outfit => outfit.FormKey.ID, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifest.Entries)
        {
            if (!string.IsNullOrEmpty(entry.OutfitEditorId) && !editorIds.ContainsKey(entry.OutfitEditorId))
                throw new InvalidDataException("The saved inventory refers to an outfit missing from its generated ESP.");
            ValidateInventoryDistribution(directory, entry, editorIds);
        }
    }

    internal static void ValidateInventoryDistribution(string directory, PatchManifestEntry entry, IReadOnlyDictionary<string, uint> outfitIdsByEditorId)
    {
        var ini = PatchOutputTransaction.ManagedPath(directory, entry.DistributionPath);
        if (!File.Exists(ini))
            throw new InvalidDataException($"The distribution file for '{entry.SourcePlugin} / {entry.OutfitName}' is missing. Restore a backup or regenerate in a new output folder.");
        var lines = File.ReadAllLines(ini);
        var rules = new List<string>();
        var sections = 0;
        var skyPatcher = entry.Framework.Equals("SkyPatcher", StringComparison.OrdinalIgnoreCase);
        for (var i = 0; i < lines.Length; i++)
        {
            if (!IsSectionHeader(lines[i]) || !lines[i].StartsWith("; " + entry.SourcePlugin + " — ", StringComparison.OrdinalIgnoreCase)) continue;
            var name = i + 1 < lines.Length && lines[i + 1].StartsWith("; Outfit: ", StringComparison.Ordinal)
                ? lines[i + 1][10..] : Path.GetFileNameWithoutExtension(entry.SourcePlugin);
            if (!name.Equals(entry.OutfitName, StringComparison.OrdinalIgnoreCase)) continue;
            sections++;
            for (var row = i + 1; row < lines.Length && !IsSectionHeader(lines[row]); row++)
                if (!lines[row].TrimStart().StartsWith(';') && (skyPatcher ? lines[row].Contains("outfitDefault=", StringComparison.Ordinal) : lines[row].StartsWith("Outfit = ", StringComparison.Ordinal)))
                    rules.Add(lines[row]);
        }
        if (sections != 1 || rules.Count == 0 || rules.Count != entry.DistributionRows)
            throw new InvalidDataException($"The distribution rules for '{entry.SourcePlugin} / {entry.OutfitName}' are missing, duplicated or incomplete. Restore a backup or regenerate in a new output folder.");
        foreach (var rule in rules)
        {
            var referenced = skyPatcher
                ? DistributionRuleCompiler.ParseSkyPatcherForm(rule.Split(':').Single(field => field.StartsWith("outfitDefault=", StringComparison.Ordinal))[14..])
                : DistributionRuleCompiler.ParseSpidForm(rule[(rule.IndexOf('=') + 1)..].Trim().Split('|')[0]);
            if (!referenced.Plugin.Equals(OutputPluginName, StringComparison.OrdinalIgnoreCase) ||
                !outfitIdsByEditorId.Values.Contains(referenced.Id) ||
                !string.IsNullOrWhiteSpace(entry.OutfitEditorId) && outfitIdsByEditorId[entry.OutfitEditorId] != referenced.Id)
                throw new InvalidDataException($"The distribution section for '{entry.SourcePlugin} / {entry.OutfitName}' references the wrong generated outfit. Restore a backup or regenerate in a new output folder.");
        }
    }
}
