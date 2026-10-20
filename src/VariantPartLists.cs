using System.IO;
using System.Security.Cryptography;
using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace WearableDistributor;

internal static class VariantPartLists
{
    internal const string Marker = "SSPG_Variant_";
    private sealed record Node(string Id, bool All, int Chance, List<string> Forms, List<Node> Children);
    private static Node Plan(string outfit, IReadOnlyList<OutfitColorBranch> branches)
    {
        string Id(string label) => Marker + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(outfit + "|" + label)))[..24];
        return new(Id("colors"), false, 100, [], branches.Select(branch => new Node(Id("color|" + branch.Color), true, 100, [],
            branch.Groups.Select(group => new Node(Id("part|" + branch.Color + "|" + group.Family), false, group.Chance, group.Forms, [])).ToList())).ToList());
    }
    internal static FormKey Build(SkyrimMod mod, string outfit, List<OutfitColorBranch> branches)
    {
        FormKey Write(Node node)
        {
            var links = node.Forms.Select(Form).Concat(node.Children.Select(Write)).ToList();
            if (links.Count is < 1 or > 255) throw new InvalidDataException("A variant list needs 1–255 entries. Split this collection into smaller named outfits.");
            var list = mod.LeveledItems.FirstOrDefault(l => l.EditorID == node.Id);
            if (list is null) { list = new LeveledItem(mod, node.Id); mod.LeveledItems.Add(list); }
            list.Global.Clear();
            list.Flags = node.All ? LeveledItem.Flag.UseAll : 0;
            list.ChanceNone = new Noggog.Percent((100 - node.Chance) / 100.0);
            list.Entries = new Noggog.ExtendedList<LeveledItemEntry>(links.Select(key => new LeveledItemEntry { Data = new LeveledItemEntryData { Level = 1, Count = 1, Reference = new FormLink<IItemGetter>(key) } }));
            return list.FormKey;
        }
        return Write(Plan(outfit, branches));
    }
    private static FormKey Form(string id)
    {
        var form = DistributionRuleCompiler.ParseSpidForm(id);
        return new FormKey(ModKey.FromFileName(form.Plugin), form.Id);
    }
    internal static void Validate(ISkyrimModGetter mod, PatchManifestEntry entry, HashSet<FormKey> used)
    {
        var branches = entry.VariantBranches;
        if (!entry.DistributeToNpcs || branches is null || branches.Count is < 1 or > 255 || branches.Any(b => b is null || b.Groups is null || b.Groups.Any(g => g is null)) || branches.Select(b => b.Color).Distinct(StringComparer.OrdinalIgnoreCase).Count() != branches.Count)
            throw new InvalidDataException("Invalid matching-variant inventory. Repatch this outfit.");
        var choices = entry.PartChoices.Where(p => p.Mode != "Excluded").ToDictionary(p => p.FormId, StringComparer.OrdinalIgnoreCase);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var branch in branches)
        {
            if (branch.Groups is null || branch.Groups.Count is < 1 or > 255 || !branch.Groups.Any(g => g.Mode == "Required") || branch.Groups.Select(g => g.Family).Distinct().Count() != branch.Groups.Count)
                throw new InvalidDataException("Invalid color branch. Repatch this outfit.");
            var inBranch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in branch.Groups)
            {
                if (group.Forms is null || group.Forms.Count is < 1 or > 255 || group.Mode is not ("Required" or "Optional") || group.Chance is < 1 or > 100)
                    throw new InvalidDataException("Invalid garment alternatives. Repatch this outfit.");
                foreach (var form in group.Forms)
                {
                    if (!inBranch.Add(form) || !choices.TryGetValue(form, out var choice) || choice.Mode != group.Mode || group.Chance != (choice.Mode == "Required" ? 100 : choice.Chance))
                        throw new InvalidDataException("Garment alternatives do not match saved parts/chances. Repatch this outfit.");
                    found.Add(form);
                }
            }
        }
        if (!found.SetEquals(choices.Keys)) throw new InvalidDataException("Some selected parts are absent from matching variants.");
        var lists = mod.LeveledItems.ToDictionary(l => l.EditorID ?? "", StringComparer.Ordinal);
        FormKey Check(Node node)
        {
            if (!lists.TryGetValue(node.Id, out var list) || list.Flags != (node.All ? LeveledItem.Flag.UseAll : 0) ||
                Math.Abs(100 * (1.0 - list.ChanceNone) - node.Chance) > .01 || !list.Global.IsNull)
                throw new InvalidDataException("Generated color/garment list settings differ from the saved setup.");
            used.Add(list.FormKey);
            var expected = node.Forms.Select(Form).Concat(node.Children.Select(Check)).ToList();
            if (list.Entries?.Count != expected.Count || list.Entries.Where((item, i) => item.ExtraData is not null || item.Data is not { Count: 1, Level: 1 } data || data.Reference.FormKey != expected[i]).Any())
                throw new InvalidDataException("Generated color/garment links differ from the saved setup.");
            return list.FormKey;
        }
        var root = Check(Plan(entry.OutfitEditorId, branches));
        var outfit = mod.Outfits.FirstOrDefault(o => o.EditorID == entry.OutfitEditorId);
        if (outfit?.Items?.Count != 1 || outfit.Items[0].FormKey != root) throw new InvalidDataException("Outfit does not point to its matching color selector.");
    }
}
