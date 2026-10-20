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

public sealed record PatchGenerationResult(string PluginPath, string DistributionPath, int OutfitCount, int DistributionCount, IReadOnlyList<string> Masters, string Framework,
    int UpdatedOutfitCount = 0, int PreservedOutfitCount = 0, string? MerchantDistributionPath = null, int MerchantDistributionCount = 0);

public static class PatchGenerator
{
    public const string OutputPluginName = "WearableDistributor.esp";
    public const string OutputIniName = "WearableDistributor_DISTR.ini";
    public const string ManifestName = "WearableDistributor.patch.json";
    public static string StableEditorIdFor(string sourcePlugin, string? outfitName = null) => StableEditorId(sourcePlugin, outfitName);
    public static string ManifestPathFor(string outputDirectory) => Path.Combine(outputDirectory, ManifestName);

    public static PatchGenerationResult Generate(IEnumerable<PluginPatchOptions> sourceOptions, string outputDirectory, string framework,
        IEnumerable<PluginPatchOptions>? skippedOptions = null, IProgress<string>? progress = null)
    {
        if (!framework.Equals("SPID", StringComparison.OrdinalIgnoreCase) && !framework.Equals("SkyPatcher", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose SPID or SkyPatcher as the distribution framework.");
        var useSkyPatcher = framework.Equals("SkyPatcher", StringComparison.OrdinalIgnoreCase);
        var configurations = sourceOptions.ToList();
        progress?.Report($"Checking {configurations.Count} outfit setup(s) and trader selections…");
        if (configurations.Count == 0) throw new InvalidOperationException("No plugin configurations were provided.");
        if (configurations.Any(configuration => !configuration.Pieces.Any(piece => piece.Include)))
            throw new InvalidOperationException("Each selected outfit needs at least one included part. Skip an outfit to preserve its existing patch without changing it.");

        var outfits = configurations
            .Select(configuration => new
            {
                Configuration = configuration,
                Pieces = configuration.Pieces.Where(piece => piece.Include).ToList(),
                Rules = configuration.TargetRules.Select(rule => rule.Filters).ToList(),
                Chance = configuration.DistributeToNpcs ? ParseChance(configuration.Chance) : 100
            })
            .Where(item => item.Pieces.Count > 0)
            .ToList();
        if (outfits.Count == 0) throw new InvalidOperationException("Include at least one outfit piece before generating the patch.");
        var duplicateOutfitIds = outfits.GroupBy(item => StableEditorId(item.Configuration.Source.Plugin, item.Configuration.OutfitName), StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
        if (duplicateOutfitIds.Count > 0) throw new InvalidOperationException("Two named outfits resolve to the same generated record ID. Rename one outfit variant and try again.");

        foreach (var assets in configurations.Select(c => c.Source.MeshAssets).OfType<MeshAssetIndex>().Distinct()) assets.RefreshArchives();
        foreach (var item in outfits)
        {
            if (item.Configuration.DistributeToNpcs && (item.Rules.Count == 0 || item.Rules.Any(rule => rule.Count == 0))) throw new InvalidOperationException($"{item.Configuration.Source.Plugin} has an audience with no NPC target. Choose at least one group.");
            if (item.Configuration.DistributeToNpcs && useSkyPatcher && item.Chance != 100)
                throw new InvalidOperationException("SkyPatcher's NPC outfit rules do not support a per-NPC chance. Choose 100% for a full outfit assignment, or select SPID in Preferences to use the configured chance.");
            ValidateConfiguration(item.Configuration);
            var partErrors = OutfitPartPresets.Errors(item.Configuration);
            if (partErrors.Count > 0) throw new InvalidOperationException(string.Join(" ", partErrors));
            if (MerchantStockService.Build(item.Configuration).Count == 0 && !item.Configuration.DistributeToNpcs)
                throw new InvalidOperationException("Choose at least one confirmed trader category for a merchants-only outfit.");
        }

        outputDirectory = PatchOutputTransaction.NormalizeRoot(outputDirectory);
        using var outputLease = PatchOutputTransaction.Acquire(outputDirectory);
        PatchOutputTransaction.EnsureNoInterruptedWrite(outputDirectory);
        var previousManifest = PatchInventoryService.ReadManifest(outputDirectory);
        progress?.Report($"Loaded existing patch: {previousManifest.Entries.Count} saved outfit(s). Preparing a merged update…");
        var skipped = skippedOptions?.ToList() ?? [];
        var preservedIds = skipped.Select(config => StableEditorId(config.Source.Plugin, config.OutfitName))
            .Concat(previousManifest.Entries.Where(entry => skipped.Any(config =>
                config.Source.Plugin.Equals(entry.SourcePlugin, StringComparison.OrdinalIgnoreCase) &&
                config.OutfitName.Equals(entry.OutfitName, StringComparison.OrdinalIgnoreCase))).Select(entry => entry.OutfitEditorId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var replacingKeys = configurations.Select(config => GeneratedDistributionIndex.Key(config.Source.Plugin, config.OutfitName)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Replaces(PatchManifestEntry entry) => replacingKeys.Contains(GeneratedDistributionIndex.Key(entry.SourcePlugin, entry.OutfitName)) && !preservedIds.Contains(entry.OutfitEditorId);
        // A damaged entry can be repaired when it is explicitly regenerated. Untouched entries must
        // survive and pass validation in the staged complete set before any destination is changed.
        EnsureFrameworkIsolation(outputDirectory, previousManifest, useSkyPatcher, previousManifest.Entries.Where(entry => !Replaces(entry)).ToList());
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
            var highestOutfitId = outputMod.Outfits.Select(o => o.FormKey).Concat(outputMod.LeveledItems.Select(l => l.FormKey)).Where(k => k.ModKey == outputModKey).Select(k => k.ID).DefaultIfEmpty(0u).Max();
            mutableMod.NextFormID = Math.Max(Math.Max(mutableMod.NextFormID, 0x800u), checked(highestOutfitId + 1));
            if (mutableMod.NextFormID > 0xFFFFFF)
                throw new InvalidDataException("The generated plugin has exhausted its local FormID range. Choose a new output folder.");
            outputMod.ModHeader.Author = "Skyrim SPID Patch Generator";
            outputMod.ModHeader.Description = useSkyPatcher ? "Generated NPC outfits. Keep enabled with the generated Skyrim SPID Patch Generator SkyPatcher rules." :
                "Generated NPC outfits. Keep enabled with WearableDistributor_DISTR.ini.";
            foreach (var master in selectedMasters)
                if (!outputMod.ModHeader.MasterReferences.Any(reference => reference.Master == master))
                    outputMod.ModHeader.MasterReferences.Add(new MasterReference { Master = master });
            var previousSelectedIds = previousManifest.Entries.Where(Replaces).Select(entry => entry.OutfitEditorId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var replacedNumericIds = outputMod.Outfits.Where(outfit => previousSelectedIds.Contains(outfit.EditorID ?? "")).Select(outfit => outfit.FormKey.ID).ToHashSet();

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
            var distributionCount = 0;
            var generatedEntries = new List<PatchManifestEntry>();
            foreach (var item in outfits)
            {
                progress?.Report("Building " + item.Configuration.OutfitName + (item.Configuration.DistributeToNpcs ? " · NPC outfits" : " · merchants only") + (item.Configuration.MerchantCategories.Count > 0 ? " · shop stock" : ""));
                var editorId = StableEditorId(item.Configuration.Source.Plugin, item.Configuration.OutfitName);
                var outfit = outputMod.Outfits.FirstOrDefault(existing => string.Equals(existing.EditorID, editorId, StringComparison.OrdinalIgnoreCase));
                var oldEntry = previousManifest.Entries.FirstOrDefault(entry => GeneratedDistributionIndex.Key(entry.SourcePlugin, entry.OutfitName)
                    .Equals(GeneratedDistributionIndex.Key(item.Configuration.Source.Plugin, item.Configuration.OutfitName), StringComparison.OrdinalIgnoreCase));
                // Reuse a known legacy record's FormID rather than reallocating it during migration.
                if (outfit is null && !string.IsNullOrWhiteSpace(oldEntry?.OutfitEditorId))
                    outfit = outputMod.Outfits.FirstOrDefault(existing => string.Equals(existing.EditorID, oldEntry.OutfitEditorId, StringComparison.OrdinalIgnoreCase));
                if (outfit is null)
                {
                    outfit = new Outfit(outputMod, editorId);
                    outputMod.Outfits.Add(outfit);
                }
                outfit.EditorID = editorId;
                outfit.Items = new Noggog.ExtendedList<IFormLinkGetter<IOutfitTargetGetter>>();
                var outfitItems = outfit.Items;
                var variantBranches = item.Configuration.DistributeToNpcs && item.Configuration.MatchVariants ? OutfitVariantPlan.Create(item.Configuration) : [];
                if (variantBranches.Count > 0)
                    outfitItems.Add(new FormLink<IOutfitTargetGetter>(VariantPartLists.Build(outputMod, editorId, variantBranches)));
                else foreach (var piece in item.Pieces.DistinctBy(piece => piece.FormId, StringComparer.OrdinalIgnoreCase))
                {
                    var sourceKey = new FormKey(ModKey.FromFileName(piece.Plugin), piece.LocalId);
                    var itemKey = item.Configuration.DistributeToNpcs && piece.PartMode == "Optional" ? OptionalPartLists.Build(outputMod, editorId, piece) : sourceKey;
                    outfitItems.Add(new FormLink<IOutfitTargetGetter>(itemKey));
                }

                var distributionCountBefore = distributionCount;
                lines.Add($"; {item.Configuration.Source.Plugin} — {item.Pieces.Count} piece(s)");
                lines.Add($"; Outfit: {item.Configuration.OutfitName}");
                var compiled = DistributionRuleCompiler.Compile(item.Configuration, outfit.FormKey.ID, useSkyPatcher);
                lines.AddRange(compiled);
                distributionCount += compiled.Count(line => IsOutfitRule(line) || line.Contains("outfitDefault=", StringComparison.Ordinal));
                generatedEntries.Add(new PatchManifestEntry
                {
                    PartPreset = item.Configuration.PartPreset,
                    MatchVariants = variantBranches.Count > 0, VariantBranches = variantBranches,
                    PartChoices = item.Configuration.Pieces.Select(p => new SavedPartChoice(p.FormId, item.Configuration.DistributeToNpcs ? p.PartMode : p.Include ? "Required" : "Excluded", p.OptionalChance)).ToList(),
                    DistributeToNpcs = item.Configuration.DistributeToNpcs,
                    SourcePlugin = item.Configuration.Source.Plugin,
                    OutfitName = item.Configuration.OutfitName,
                    OutfitEditorId = editorId,
                    Framework = useSkyPatcher ? "SkyPatcher" : "SPID",
                    DistributionPath = relativeIniPath,
                    IncludedPieces = item.Pieces.Select(piece => $"{piece.Name} [{piece.FormId}]").ToList(),
                    ExcludedPieces = item.Configuration.Pieces.Where(piece => !piece.Include).Select(piece => $"{piece.Name} [{piece.FormId}]").ToList(),
                    RequiredMasters = item.Pieces.Select(piece => piece.Plugin).Concat(item.Configuration.DistributeToNpcs ? DistributionRuleCompiler.RequiredTargetPlugins(item.Configuration) : []).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(plugin => plugin, StringComparer.OrdinalIgnoreCase).ToList(),
                    Audiences = !item.Configuration.DistributeToNpcs ? [] : item.Configuration.TargetRules.Count > 0
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

            var activeOutfitIds = generatedEntries.Select(entry => entry.OutfitEditorId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var removedIds = previousManifest.Entries
                .Where(entry => Replaces(entry) && entry.Framework.Equals(framework, StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.OutfitEditorId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var retainedIds = previousManifest.Entries
                .Where(entry => !Replaces(entry) || !entry.Framework.Equals(framework, StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.OutfitEditorId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var staleOutfit in outputMod.Outfits.Where(existing => existing.EditorID is { } id &&
                         !activeOutfitIds.Contains(id) && !retainedIds.Contains(id) && removedIds.Contains(id)).ToList())
                outputMod.Outfits.Remove(staleOutfit);

            OptionalPartLists.Prune(outputMod);
            var existingIniLines = File.Exists(outputIni) ? File.ReadAllLines(outputIni).ToList() : [];
            replacedNumericIds.UnionWith(outputMod.Outfits.Where(outfit => activeOutfitIds.Contains(outfit.EditorID ?? "")).Select(outfit => outfit.FormKey.ID));
            var mergedLines = MergeIni(existingIniLines, lines, replacingKeys, replacedNumericIds, useSkyPatcher);
            var mergedDistributionCount = mergedLines.Count(line => IsOutfitRule(line) || line.Contains("outfitDefault=", StringComparison.Ordinal));
            var existingManifest = previousManifest;
            if (generatedEntries.Any(entry => entry.MerchantStock.Count > 0)) existingManifest.SchemaVersion = 3;
            if (generatedEntries.Concat(previousManifest.Entries).Any(entry => !entry.DistributeToNpcs)) existingManifest.SchemaVersion = 4;
            foreach (var entry in generatedEntries)
                entry.RequiredMasters = entry.RequiredMasters.Concat(entry.MerchantStock.SelectMany(rule => new[] { rule.ContainerPlugin, rule.ItemPlugin })).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
            existingManifest.SchemaVersion = generatedEntries.Concat(previousManifest.Entries).Any(e => e.MatchVariants) ? 6 : 5;
            existingManifest.Framework = useSkyPatcher ? "SkyPatcher" : "SPID";
            existingManifest.OutputPlugin = OutputPluginName;
            existingManifest.DistributionPath = relativeIniPath;
            existingManifest.GeneratedAtUtc = DateTime.UtcNow;
            existingManifest.Entries = existingManifest.Entries.Where(entry => !Replaces(entry) || !entry.Framework.Equals(existingManifest.Framework, StringComparison.OrdinalIgnoreCase)).Concat(generatedEntries).OrderBy(entry => entry.SourcePlugin, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.OutfitName, StringComparer.OrdinalIgnoreCase).ToList();
            var tempManifest = Path.Combine(tempDirectory, ManifestName);
            progress?.Report("Staging the merged ESP, NPC rules, merchant stock and inventory…");
            var tempMerchandise = MerchantStockService.Stage(tempDirectory, existingManifest);
            var merchantRuleCount = tempMerchandise is null ? 0 : File.ReadLines(tempMerchandise).Count(line => line.StartsWith("filterByContainers=", StringComparison.Ordinal));
            File.WriteAllText(tempManifest, JsonSerializer.Serialize(existingManifest, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            var expectedRecordItems = outputMod.Outfits.Where(outfit => !string.IsNullOrWhiteSpace(outfit.EditorID))
                .ToDictionary(outfit => outfit.EditorID!, outfit => (outfit.Items?.Select(item => item.FormKey) ?? []).ToHashSet(), StringComparer.OrdinalIgnoreCase);

            outputMod.BeginWrite.ToPath(tempPlugin).WithNoLoadOrder().NoNextFormIDProcessing().Write();
            File.WriteAllText(tempIni, string.Join(Environment.NewLine, mergedLines), new UTF8Encoding(false));
            progress?.Report("Validating the complete staged patch before changing existing files…");
            ValidateGeneratedPlugin(tempPlugin, expectedRecordItems, selectedMasters);
            ValidateGeneratedIni(tempIni, mergedDistributionCount, useSkyPatcher, outputMod.Outfits.Select(outfit => outfit.FormKey.ID).ToHashSet());
            ValidateOutputSet(tempDirectory);

            progress?.Report("Validation passed. Backing up and committing the patch update…");

            PatchOutputTransaction.Commit(outputDirectory, [tempPlugin, tempIni, tempManifest, tempMerchandise], [outputPlugin, outputIni, manifestPath, PatchOutputTransaction.ManagedPath(outputDirectory, MerchantStockService.RelativePath)], "Generate");
            return new PatchGenerationResult(outputPlugin, outputIni, existingManifest.Entries.Count, mergedDistributionCount, outputMod.ModHeader.MasterReferences.Select(reference => reference.Master.FileName.ToString()).ToList(), useSkyPatcher ? "SkyPatcher" : "SPID", generatedEntries.Count, existingManifest.Entries.Count - generatedEntries.Count,
                tempMerchandise is null ? null : PatchOutputTransaction.ManagedPath(outputDirectory, MerchantStockService.RelativePath), merchantRuleCount);
        }
        finally
        {
            try { Directory.Delete(tempDirectory, true); } catch (Exception cleanup) { App.WriteDiagnostic(DiagnosticEvent.TemporaryCleanupFailed, cleanup, DiagnosticLevel.Warning); }
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
        if (configuration.DistributeToNpcs && OutfitGenderPolicy.ValidationIssue(configuration) is { } sexIssue) throw new InvalidOperationException(sexIssue);
        foreach (var piece in configuration.Pieces.Where(piece => piece.Include))
        {
            DistributionRuleCompiler.ValidatePluginName(piece.Plugin);
            if (piece.LocalId is 0 or > 0xFFFFFF || piece.Plugin.Equals(OutputPluginName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Outfit piece '{piece.Name}' has an invalid source record.");
        }
        DistributionRuleCompiler.Validate(configuration);
    }

    private static void EnsureFrameworkIsolation(string outputDirectory, PatchManifest manifest, bool useSkyPatcher, IReadOnlyList<PatchManifestEntry> retainedEntries)
    {
        var framework = useSkyPatcher ? "SkyPatcher" : "SPID";
        var oppositeIni = PatchOutputTransaction.ManagedPath(outputDirectory, useSkyPatcher ? OutputIniName : PatchOutputTransaction.SkyPatcherIniPath);
        var oppositeHasRules = File.Exists(oppositeIni) && File.ReadLines(oppositeIni).Any(line => !line.TrimStart().StartsWith(';') &&
            (line.TrimStart().StartsWith("Outfit", StringComparison.OrdinalIgnoreCase) || line.Contains("outfitDefault=", StringComparison.OrdinalIgnoreCase)));
        if (manifest.Entries.Any(entry => !entry.Framework.Equals(framework, StringComparison.OrdinalIgnoreCase)) || oppositeHasRules)
            throw new InvalidOperationException("This output folder already contains a patch for the other distribution framework. Generate the alternate framework in a new output mod folder, and enable only one Skyrim SPID Patch Generator output mod at a time because both use WearableDistributor.esp.");
        if (manifest.Entries.Count > 0 && !File.Exists(PatchOutputTransaction.ManagedPath(outputDirectory, OutputPluginName)))
            throw new InvalidDataException("The existing patch inventory is missing WearableDistributor.esp. Restore a backup or choose a new output folder before generating.");
        if (retainedEntries.Any(entry => !File.Exists(PatchOutputTransaction.ManagedPath(outputDirectory, entry.DistributionPath))))
            throw new InvalidDataException("The existing patch is missing distribution rules for outfits not selected in this run. Use My patch → Repatch all, or restore a backup. All missing entries must be regenerated together before the shared INI can be rebuilt.");
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

    private static List<string> MergeIni(IReadOnlyList<string> existing, IReadOnlyList<string> generated, IReadOnlySet<string> replacingKeys, IReadOnlySet<uint> replacingIds, bool skyPatcher)
    {
        if (existing.Count == 0) return generated.ToList();
        var kept = new List<string>();
        for (var i = 0; i < existing.Count;)
        {
            var line = existing[i];
            var sourcePlugin = GeneratedDistributionIndex.TryHeader(line, out var plugin) ? plugin : null;
            var outfitName = i + 1 < existing.Count && existing[i + 1].StartsWith("; Outfit: ", StringComparison.Ordinal)
                ? existing[i + 1][10..] : sourcePlugin is null ? "" : Path.GetFileNameWithoutExtension(sourcePlugin);
            var replacedSection = sourcePlugin is not null && replacingKeys.Contains(GeneratedDistributionIndex.Key(sourcePlugin, outfitName));
            if (replacedSection)
            {
                i++;
                while (i < existing.Count && !IsSectionHeader(existing[i])) i++;
                while (i < existing.Count && string.IsNullOrWhiteSpace(existing[i])) i++;
                continue;
            }
            // Recover a selected outfit even when its comment header was lost. Do not guess ownership
            // of malformed/foreign rules: keep them and let complete staged validation reject damage.
            if (!ReferencesReplacedOutfit(line)) kept.Add(line);
            i++;
        }
        while (kept.Count > 0 && string.IsNullOrWhiteSpace(kept[^1])) kept.RemoveAt(kept.Count - 1);
        kept.Add("");
        kept.AddRange(generated.Skip(4));
        return kept;

        bool ReferencesReplacedOutfit(string line)
        {
            if (line.TrimStart().StartsWith(';')) return false;
            try
            {
                (uint Id, string Plugin) form;
                if (skyPatcher && line.Contains("outfitDefault=", StringComparison.Ordinal))
                    form = DistributionRuleCompiler.ParseSkyPatcherForm(line.Split(':').Single(field => field.StartsWith("outfitDefault=", StringComparison.Ordinal))[14..]);
                else if (!skyPatcher && IsOutfitRule(line))
                    form = DistributionRuleCompiler.ParseSpidForm(line[(line.IndexOf('=') + 1)..].Trim().Split('|')[0]);
                else return false;
                return form.Plugin.Equals(OutputPluginName, StringComparison.OrdinalIgnoreCase) && replacingIds.Contains(form.Id);
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentException or InvalidOperationException or FormatException or OverflowException)
            { App.WriteDiagnostic(DiagnosticEvent.LegacyRuleReadFailed, error, DiagnosticLevel.Warning); return false; }
        }
    }

    private static bool IsSectionHeader(string line) => line.StartsWith("; ", StringComparison.Ordinal) &&
        line.Contains(" — ", StringComparison.Ordinal) && line.EndsWith(" piece(s)", StringComparison.Ordinal);

    private static bool IsOutfitRule(string line) => System.Text.RegularExpressions.Regex.IsMatch(line,
        @"^\s*Outfit\s*=", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

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
            (skyPatcher ? line.Contains("outfitDefault=", StringComparison.Ordinal) : IsOutfitRule(line))).ToList();
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
                if (columns.Length != 7 || !columns[0].Contains('~') || (columns[1].Trim().Equals("NONE", StringComparison.OrdinalIgnoreCase) && columns[2].Trim().Equals("NONE", StringComparison.OrdinalIgnoreCase)) ||
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
        var observedRuleCounts = new Dictionary<uint, int>();
        foreach (var (path, isSky) in new[] { (spid, false), (sky, true) })
            if (File.Exists(path))
            {
                var rules = File.ReadLines(path).Where(line => !line.TrimStart().StartsWith(';') &&
                    (isSky ? line.Contains("outfitDefault=", StringComparison.Ordinal) : IsOutfitRule(line))).ToList();
                ValidateGeneratedIni(path, rules.Count, isSky, ids);
                foreach (var rule in rules)
                {
                    var id = isSky ? DistributionRuleCompiler.ParseSkyPatcherForm(rule.Split(':').Single(field => field.StartsWith("outfitDefault=", StringComparison.Ordinal))[14..]).Id :
                        DistributionRuleCompiler.ParseSpidForm(rule[(rule.IndexOf('=') + 1)..].Trim().Split('|')[0]).Id;
                    referencedIds.Add(id); observedRuleCounts[id] = observedRuleCounts.GetValueOrDefault(id) + 1;
                }
            }
        var manifest = PatchInventoryService.ReadManifest(directory);
        MerchantStockService.Validate(directory, manifest);
        OptionalPartLists.Validate(mod, manifest);
        var editorIds = mod.Outfits.Where(outfit => !string.IsNullOrWhiteSpace(outfit.EditorID))
            .ToDictionary(outfit => outfit.EditorID!, outfit => outfit.FormKey.ID, StringComparer.OrdinalIgnoreCase);
        var distributionFiles = new Dictionary<string, GeneratedDistributionIndex>(StringComparer.OrdinalIgnoreCase);
        var verifiedManifestIds = new HashSet<uint>();
        var verifiedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merchandiseOnlyIds = new HashSet<uint>();
        foreach (var entry in manifest.Entries)
        {
            if (!verifiedKeys.Add(GeneratedDistributionIndex.Key(entry.SourcePlugin, entry.OutfitName)))
                throw new InvalidDataException("The inventory contains duplicate outfit entries. Restore a backup before repatching.");
            if (!string.IsNullOrEmpty(entry.OutfitEditorId) && !editorIds.ContainsKey(entry.OutfitEditorId))
                throw new InvalidDataException("The saved inventory refers to an outfit missing from its generated ESP.");
            if (!distributionFiles.TryGetValue(entry.DistributionPath, out var index))
                distributionFiles[entry.DistributionPath] = index = new GeneratedDistributionIndex(PatchOutputTransaction.ManagedPath(directory, entry.DistributionPath));
            ValidateInventoryDistribution(directory, entry, editorIds, index);
            if (!string.IsNullOrWhiteSpace(entry.OutfitEditorId))
            {
                var id = editorIds[entry.OutfitEditorId];
                if (!entry.DistributeToNpcs) merchandiseOnlyIds.Add(id);
                if (!verifiedManifestIds.Add(id) || observedRuleCounts.GetValueOrDefault(id) != entry.DistributionRows)
                    throw new InvalidDataException($"Unexpected or duplicated distribution rows exist outside the managed section for '{entry.SourcePlugin} / {entry.OutfitName}'. Repatch this outfit or restore a backup.");
            }
        }
        referencedIds.UnionWith(merchandiseOnlyIds);
        if (!ids.SetEquals(referencedIds))
            throw new InvalidDataException("The saved ESP contains an outfit without NPC distribution or a validated merchants-only entry. Restore a backup or regenerate in a new output folder.");
    }

    internal static void ValidateInventoryDistribution(string directory, PatchManifestEntry entry, IReadOnlyDictionary<string, uint> outfitIdsByEditorId, GeneratedDistributionIndex? index = null)
    {
        var ini = PatchOutputTransaction.ManagedPath(directory, entry.DistributionPath);
        if (!File.Exists(ini))
            throw new InvalidDataException($"The distribution file for '{entry.SourcePlugin} / {entry.OutfitName}' is missing. Restore a backup or regenerate in a new output folder.");
        index ??= new GeneratedDistributionIndex(ini);
        var matchingSections = index.Find(entry.SourcePlugin, entry.OutfitName);
        var skyPatcher = entry.Framework.Equals("SkyPatcher", StringComparison.OrdinalIgnoreCase);
        var rules = matchingSections.SelectMany(section => section.Lines).Where(line => !line.TrimStart().StartsWith(';') &&
            (skyPatcher ? line.Contains("outfitDefault=", StringComparison.Ordinal) : IsOutfitRule(line))).ToList();
        // Early merchants-only builds omitted an empty NPC comment section. Shop stock can
        // remain valid without that comment, but a real NPC rule for the same outfit must never
        // be ignored. MerchantStockService validates the actual container rules separately.
        if (!entry.DistributeToNpcs && entry.DistributionRows == 0 && matchingSections.Count == 0 && entry.MerchantStock.Count > 0 &&
            outfitIdsByEditorId.TryGetValue(entry.OutfitEditorId, out var merchantOutfitId))
        {
            var hasNpcRule = index.Lines.Where(line => !line.TrimStart().StartsWith(';') &&
                (skyPatcher ? line.Contains("outfitDefault=", StringComparison.Ordinal) : IsOutfitRule(line))).Any(line =>
            {
                var form = skyPatcher
                    ? DistributionRuleCompiler.ParseSkyPatcherForm(line.Split(':').Single(field => field.StartsWith("outfitDefault=", StringComparison.Ordinal))[14..])
                    : DistributionRuleCompiler.ParseSpidForm(line[(line.IndexOf('=') + 1)..].Trim().Split('|')[0]);
                return form.Id == merchantOutfitId && form.Plugin.Equals(OutputPluginName, StringComparison.OrdinalIgnoreCase);
            });
            if (!hasNpcRule) return;
        }
        if (matchingSections.Count != 1 || rules.Count != entry.DistributionRows ||
            (entry.DistributeToNpcs ? rules.Count == 0 : rules.Count != 0 || entry.MerchantStock.Count == 0 ||
                matchingSections.SelectMany(section => section.Lines).Any(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith(';'))))
            throw new InvalidDataException($"The distribution rules for '{entry.SourcePlugin} / {entry.OutfitName}' are missing, duplicated or incomplete. Repatch this outfit in My patch; if other entries also need repair, use Repatch all or restore a backup.");
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
