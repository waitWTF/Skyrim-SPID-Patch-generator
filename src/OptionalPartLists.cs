using System.IO;
using System.Security.Cryptography;
using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace WearableDistributor;

internal static class OptionalPartLists
{
    private const string Marker = "WD_TestOptional_";
    internal static FormKey Build(SkyrimMod mod, string outfitEditorId, WearableRecord piece)
    {
        var id = Marker + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(outfitEditorId + "|" + piece.FormId)))[..24];
        var list = mod.LeveledItems.FirstOrDefault(item => item.EditorID == id);
        if (list is null) { list = new LeveledItem(mod, id); mod.LeveledItems.Add(list); }
        list.Global.Clear();
        list.ChanceNone = new Noggog.Percent((100 - piece.OptionalChance) / 100.0);
        list.Flags = 0;
        list.Entries = new Noggog.ExtendedList<LeveledItemEntry>
        {
            new() { Data = new LeveledItemEntryData { Level = 1, Count = 1, Reference = new FormLink<IItemGetter>(new FormKey(ModKey.FromFileName(piece.Plugin), piece.LocalId)) } }
        };
        return list.FormKey;
    }
    private static bool Managed(string? id) => id?.StartsWith(Marker, StringComparison.Ordinal) == true || id?.StartsWith(VariantPartLists.Marker, StringComparison.Ordinal) == true || id?.StartsWith(WardrobePools.Marker, StringComparison.Ordinal) == true;
    internal static void Prune(SkyrimMod mod)
    {
        var referenced = new HashSet<FormKey>();
        var pending = new Queue<FormKey>(mod.Outfits.SelectMany(o => o.Items?.Select(p => p.FormKey) ?? []));
        var lists = mod.LeveledItems.ToDictionary(l => l.FormKey);
        while (pending.TryDequeue(out var key))
            if (referenced.Add(key) && lists.TryGetValue(key, out var nested))
                foreach (var item in nested.Entries ?? []) if (item.Data is { } data) pending.Enqueue(data.Reference.FormKey);
        foreach (var list in mod.LeveledItems.Where(l => Managed(l.EditorID) && !referenced.Contains(l.FormKey)).ToList()) mod.LeveledItems.Remove(list);
    }
    internal static void Validate(ISkyrimModGetter mod, PatchManifest manifest)
    {
        var lists = mod.LeveledItems.ToDictionary(l => l.FormKey);
        var used = new HashSet<FormKey>();
        WardrobePools.Validate(mod, manifest, used);
        foreach (var entry in manifest.Entries.Where(e => e.PartChoices.Count > 0))
        {
            if (entry.MatchVariants) { VariantPartLists.Validate(mod, entry, used); continue; }
            var outfit = mod.Outfits.FirstOrDefault(o => o.EditorID == entry.OutfitEditorId) ?? throw new InvalidDataException("Missing generated outfit.");
            var actual = new Dictionary<string, (string Mode, int Chance)>(StringComparer.OrdinalIgnoreCase);
            foreach (var link in outfit.Items ?? [])
            {
                if (lists.TryGetValue(link.FormKey, out var list))
                {
                    used.Add(list.FormKey);
                    if (list.EditorID?.StartsWith(Marker, StringComparison.Ordinal) != true || list.Entries?.Count != 1 || list.Entries[0].Data is not { } data || data.Count != 1 || data.Level != 1 || (int)list.Flags != 0 || !list.Global.IsNull || list.Entries[0].ExtraData is not null)
                        throw new InvalidDataException("An optional part list is malformed.");
                    var form = data.Reference.FormKey;
                    if (!actual.TryAdd($"0x{form.ID:X6}~{form.ModKey.FileName}", ("Optional", (int)Math.Round(100 * (1.0 - list.ChanceNone))))) throw new InvalidDataException("Duplicate optional part.");
                }
                else
                {
                    var form = link.FormKey;
                    if (!actual.TryAdd($"0x{form.ID:X6}~{form.ModKey.FileName}", ("Required", 100))) throw new InvalidDataException("Duplicate required part.");
                }
            }
            var expected = entry.PartChoices.Where(p => p.Mode != "Excluded").ToList();
            if (actual.Count != expected.Count || expected.Any(p => !actual.TryGetValue(p.FormId, out var a) || a.Mode != p.Mode || a.Chance != (p.Mode == "Required" ? 100 : p.Chance)))
                throw new InvalidDataException("Generated part states/chances do not match the saved part setup. Repatch this entry.");
        }
        if (lists.Values.Any(l => Managed(l.EditorID) && !used.Contains(l.FormKey)))
            throw new InvalidDataException("An optional part list has no managed outfit. Repatch or remove the affected entry.");
    }
}
