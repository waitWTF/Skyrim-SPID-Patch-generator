using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace WearableDistributor;

/// <summary>One distribution roll per wardrobe; Skyrim chooses one complete, weighted item bundle.
/// An LVLI never references an OTFT. Member outfits stay as editable inventory records.</summary>
public static class WardrobePools
{
    internal const string Marker = "SSPG_Wardrobe_";
    public const int MaximumMembers = 20;
    public static string NormalizeName(string? name)
    {
        name = (name ?? "").Trim().Normalize(NormalizationForm.FormC);
        if (name.Length > 80 || name.Any(char.IsControl))
            throw new InvalidOperationException("Use a wardrobe name of at most 80 characters, without line breaks or control characters.");
        return name;
    }
    internal static string EditorId(string name) => Marker + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeName(name).ToUpperInvariant())))[..24];
    internal static List<DistributionTargetRule> CopyTargets(IEnumerable<DistributionTargetRule> rules) =>
        JsonSerializer.Deserialize<List<DistributionTargetRule>>(JsonSerializer.Serialize(rules))!;
    internal static string Scope(string gender, int chance, IEnumerable<DistributionTargetRule> rules) =>
        gender + "|" + chance.ToString(CultureInfo.InvariantCulture) + "|" + string.Join(';', rules.SelectMany(r => r.ResolvedNpcFormIds)
            .Select(id => { var form = DistributionRuleCompiler.ParseSpidForm(id); return $"{form.Id:X6}~{form.Plugin.ToUpperInvariant()}"; }).Distinct().Order(StringComparer.Ordinal));

    public static List<string> ConfigurationErrors(IEnumerable<PluginPatchOptions> configurations, string framework)
    {
        var errors = new List<string>();
        foreach (var group in configurations.Where(c => !string.IsNullOrWhiteSpace(c.WardrobeName)).GroupBy(c => NormalizeName(c.WardrobeName), StringComparer.OrdinalIgnoreCase))
        {
            var members = group.ToList();
            if (!framework.Equals("SPID", StringComparison.OrdinalIgnoreCase)) errors.Add("Random outfit pools currently require SPID. Remove pool membership before using SkyPatcher NPC assignments; trader sales still work alongside SPID.");
            if (members.Count > MaximumMembers) errors.Add($"Wardrobe ‘{group.Key}’ exceeds {MaximumMembers} outfits. Split it into smaller wardrobes.");
            if (members.Any(c => !c.DistributeToNpcs || c.WardrobeWeight is < 1 or > 10)) errors.Add($"Wardrobe ‘{group.Key}’ requires NPC distribution and weights from 1 to 10 for every member. Remove merchants-only outfits from the pool.");
            if (members.Any(c => c.TargetRules.Count == 0 || c.TargetRules.Any(r => !r.NpcResolutionComplete || r.ResolvedNpcFormIds.Count == 0)))
                errors.Add($"Wardrobe ‘{group.Key}’ needs confirmed NPC groups. Rescan and review the pool's targets.");
            else if (members.Select(c => Scope(c.Gender, int.TryParse(c.Chance.Trim().TrimEnd('%'), out var n) ? n : -1, c.TargetRules)).Distinct().Count() != 1)
                errors.Add($"Wardrobe ‘{group.Key}’ has different NPC groups, sex or overall chances. Open Random outfit pools and apply a shared audience to all its members.");
        }
        return errors;
    }

    internal static List<List<PatchManifestEntry>> Groups(PatchManifest manifest)
    {
        var result = manifest.Entries.Where(e => !string.IsNullOrWhiteSpace(e.WardrobeName))
            .GroupBy(e => NormalizeName(e.WardrobeName), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderBy(e => e.OutfitEditorId, StringComparer.Ordinal).ToList()).ToList();
        foreach (var members in result)
        {
            if (members.Count > MaximumMembers || members.Any(e => !e.DistributeToNpcs || e.Framework != "SPID" || e.WardrobeWeight is < 1 or > 10 || e.Chance is < 1 or > 100 || e.Gender is not ("Female" or "Male" or "Any") ||
                e.WardrobeTargets is null || e.WardrobeTargets.Count == 0 || e.WardrobeTargets.Any(r => r is null || r.Filters is null || !r.NpcResolutionComplete || r.ResolvedNpcFormIds is null || r.ResolvedNpcFormIds.Count == 0 || r.ResolvedNpcFormIds.Any(string.IsNullOrWhiteSpace))))
                throw new InvalidDataException("A saved random outfit pool has invalid members or missing resolved targets. Reconfigure its members together.");
            if (members.Select(e => Scope(e.Gender, e.Chance, e.WardrobeTargets)).Distinct().Count() != 1)
                throw new InvalidDataException($"Wardrobe ‘{members[0].WardrobeName}’ has inconsistent NPC groups, sex or overall chance. Select all its members, then apply the shared setup in Random outfit pools.");
            foreach (var member in members) DistributionRuleCompiler.Validate(ToConfiguration(member));
        }
        return result;
    }
    private static PluginPatchOptions ToConfiguration(PatchManifestEntry entry) => new()
    {
        Source = new() { Plugin = entry.SourcePlugin }, OutfitName = entry.OutfitName, DistributeToNpcs = true,
        Chance = entry.Chance.ToString(CultureInfo.InvariantCulture), Gender = entry.Gender, TargetRules = entry.WardrobeTargets
    };
    private static string BundleId(PatchManifestEntry entry) => EditorId(entry.WardrobeName) + "_" + entry.OutfitEditorId;

    // Rebuild every retained pool from the merged manifest. This also moves the sole rule to a
    // surviving member after removal and removes an obsolete pool when its last member leaves.
    internal static List<string> Rebuild(SkyrimMod mod, PatchManifest manifest, List<string> lines)
    {
        var groups = Groups(manifest);
        var oldPoolIds = mod.Outfits.Where(o => o.EditorID?.StartsWith(Marker, StringComparison.Ordinal) == true).Select(o => o.FormKey.ID).ToHashSet();
        var index = new GeneratedDistributionIndex(lines);
        var removeRows = manifest.Entries.Where(e => !string.IsNullOrWhiteSpace(e.WardrobeName))
            .SelectMany(e => index.Find(e.SourcePlugin, e.OutfitName)).SelectMany(s => Enumerable.Range(s.Start, s.End - s.Start)).ToHashSet();
        var kept = lines.Where((line, row) => !removeRows.Contains(row) && !ReferencesPool(line, oldPoolIds)).ToList();
        var livePoolIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var members in groups)
        {
            var first = members[0]; var id = EditorId(first.WardrobeName); livePoolIds.Add(id);
            var weightedBundles = new List<FormKey>();
            foreach (var member in members)
            {
                var source = mod.Outfits.SingleOrDefault(o => o.EditorID == member.OutfitEditorId)
                    ?? throw new InvalidDataException("A wardrobe member's outfit record is missing. Repatch the affected outfit.");
                var items = source.Items?.Select(i => i.FormKey).ToList() ?? [];
                if (items.Count == 0) throw new InvalidDataException("A wardrobe member has no items. Choose its required garments first.");
                var bundle = WriteList(mod, BundleId(member), true, items);
                weightedBundles.AddRange(Enumerable.Repeat(bundle, member.WardrobeWeight));
            }
            var root = WriteList(mod, id + "_ChooseOne", false, weightedBundles);
            var outfit = mod.Outfits.FirstOrDefault(o => o.EditorID == id);
            if (outfit is null) { outfit = new Outfit(mod, id); mod.Outfits.Add(outfit); }
            outfit.Items = new Noggog.ExtendedList<IFormLinkGetter<IOutfitTargetGetter>> { new FormLink<IOutfitTargetGetter>(root) };
            var compiled = DistributionRuleCompiler.Compile(ToConfiguration(first), outfit.FormKey.ID, false);
            foreach (var member in members)
            {
                member.WardrobeRuleOwner = ReferenceEquals(member, first);
                member.DistributionRows = member.WardrobeRuleOwner ? compiled.Count : 0;
                kept.Add($"; {member.SourcePlugin} — {member.IncludedPieces.Count} piece(s)");
                kept.Add("; Outfit: " + member.OutfitName);
                kept.Add($"; Random wardrobe: {member.WardrobeName}; weight {member.WardrobeWeight}/{members.Sum(m => m.WardrobeWeight)}; overall chance {first.Chance}%.");
                if (member.WardrobeRuleOwner) kept.AddRange(compiled);
                else kept.Add("; This member is selected by the shared wardrobe rule; no competing individual outfit roll.");
                kept.Add("");
            }
        }
        foreach (var old in mod.Outfits.Where(o => o.EditorID?.StartsWith(Marker, StringComparison.Ordinal) == true && !livePoolIds.Contains(o.EditorID)).ToList()) mod.Outfits.Remove(old);
        if (groups.Count > 0) manifest.SchemaVersion = 7;
        return kept;
    }
    private static bool ReferencesPool(string line, HashSet<uint> ids)
    {
        if (!line.TrimStart().StartsWith("Outfit", StringComparison.OrdinalIgnoreCase) || !line.Contains('=')) return false;
        var form = DistributionRuleCompiler.ParseSpidForm(line[(line.IndexOf('=') + 1)..].Trim().Split('|')[0]);
        return form.Plugin.Equals(PatchGenerator.OutputPluginName, StringComparison.OrdinalIgnoreCase) && ids.Contains(form.Id);
    }
    private static FormKey WriteList(SkyrimMod mod, string id, bool all, List<FormKey> items)
    {
        if (items.Count is < 1 or > 255) throw new InvalidDataException("A wardrobe list needs between 1 and 255 entries. Split large outfits into smaller named outfits.");
        var list = mod.LeveledItems.FirstOrDefault(l => l.EditorID == id);
        if (list is null) { list = new LeveledItem(mod, id); mod.LeveledItems.Add(list); }
        list.Flags = all ? LeveledItem.Flag.UseAll : 0; list.Global.Clear(); list.ChanceNone = new Noggog.Percent(0);
        list.Entries = new Noggog.ExtendedList<LeveledItemEntry>(items.Select(key => new LeveledItemEntry { Data = new LeveledItemEntryData { Level = 1, Count = 1, Reference = new FormLink<IItemGetter>(key) } }));
        return list.FormKey;
    }

    internal static void Validate(ISkyrimModGetter mod, PatchManifest manifest, HashSet<FormKey> used)
    {
        var groups = Groups(manifest);
        var expectedOutfits = new HashSet<string>(StringComparer.Ordinal);
        foreach (var members in groups)
        {
            var first = members[0]; var id = EditorId(first.WardrobeName); expectedOutfits.Add(id);
            var weighted = new List<FormKey>();
            foreach (var member in members)
            {
                if (member.WardrobeRuleOwner != ReferenceEquals(member, first)) throw new InvalidDataException("A wardrobe has an incorrect or duplicated rule owner.");
                var source = mod.Outfits.SingleOrDefault(o => o.EditorID == member.OutfitEditorId) ?? throw new InvalidDataException("Missing wardrobe member outfit.");
                var key = CheckList(BundleId(member), true, source.Items?.Select(i => i.FormKey).ToList() ?? []);
                weighted.AddRange(Enumerable.Repeat(key, member.WardrobeWeight));
            }
            var root = CheckList(id + "_ChooseOne", false, weighted);
            var outfit = mod.Outfits.SingleOrDefault(o => o.EditorID == id);
            if (outfit?.Items?.Count != 1 || outfit.Items[0].FormKey != root) throw new InvalidDataException("A wardrobe must point to exactly one complete-outfit selector.");
        }
        if (mod.Outfits.Any(o => o.EditorID?.StartsWith(Marker, StringComparison.Ordinal) == true && !expectedOutfits.Contains(o.EditorID)))
            throw new InvalidDataException("An obsolete wardrobe outfit remains in the patch.");
        FormKey CheckList(string id, bool all, List<FormKey> expected)
        {
            var list = mod.LeveledItems.SingleOrDefault(l => l.EditorID == id);
            if (expected.Count == 0 || list is null || list.Flags != (all ? LeveledItem.Flag.UseAll : 0) || list.ChanceNone != 0 || !list.Global.IsNull ||
                list.Entries?.Count != expected.Count || list.Entries.Where((e, i) => e.ExtraData is not null || e.Data is not { Level: 1, Count: 1 } data || data.Reference.FormKey != expected[i]).Any())
                throw new InvalidDataException("A random wardrobe has a broken selector, weight or complete-outfit bundle. Repatch its members together.");
            used.Add(list.FormKey); return list.FormKey;
        }
    }
    internal static void ValidateDistribution(string directory, PatchManifest manifest, ISkyrimModGetter mod)
    {
        var groups = Groups(manifest);
        if (groups.Count == 0) return;
        var lines = File.ReadAllLines(Path.Combine(directory, PatchGenerator.OutputIniName));
        var index = new GeneratedDistributionIndex(lines);
        var memberIds = groups.SelectMany(m => m).Select(e => mod.Outfits.Single(o => o.EditorID == e.OutfitEditorId).FormKey.ID).ToHashSet();
        if (lines.Any(line => ReferencesPool(line, memberIds))) throw new InvalidDataException("A wardrobe member still has a competing individual outfit rule. Repatch its pool together.");
        foreach (var members in groups)
        {
            var first = members[0]; var outfit = mod.Outfits.Single(o => o.EditorID == EditorId(first.WardrobeName));
            var expected = DistributionRuleCompiler.Compile(ToConfiguration(first), outfit.FormKey.ID, false);
            var actual = lines.Where(l => ReferencesPool(l, [outfit.FormKey.ID])).ToList();
            if (!actual.SequenceEqual(expected)) throw new InvalidDataException("A wardrobe's shared SPID rule is missing, duplicated or differs from its saved audience/chance.");
            foreach (var member in members)
            {
                var sections = index.Find(member.SourcePlugin, member.OutfitName);
                var rules = sections.SelectMany(s => s.Lines).Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith(';')).ToList();
                if (sections.Count != 1 || !rules.SequenceEqual(member.WardrobeRuleOwner ? expected : []) || member.DistributionRows != rules.Count)
                    throw new InvalidDataException("A wardrobe member contains competing or incomplete distribution rules.");
            }
        }
    }
}
