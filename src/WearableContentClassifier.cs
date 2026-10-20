namespace WearableDistributor;

public sealed record WearableContentRecord(string Id, string Kind, IReadOnlyList<string> References);

/// <summary>Classify record contents, allowing records which implement the equipment itself.</summary>
public static class WearableContentClassifier
{
    private static readonly HashSet<string> Equipment = new(["Armor", "ArmorAddon", "Weapon", "Ammunition", "Keyword", "TextureSet", "Outfit"], StringComparer.Ordinal);
    private static readonly HashSet<string> Support = new(["ConstructibleObject", "LeveledItem", "FormList", "ObjectEffect", "MagicEffect", "Spell", "EffectShader", "ArtObject", "Static", "MovableStatic", "SoundDescriptor", "SoundMarker", "Impact", "ImpactDataSet", "Projectile", "Explosion", "Light", "MaterialObject", "Global"], StringComparer.Ordinal);
    public static bool NeedsReferences(string kind) => Equipment.Contains(kind) || Support.Contains(kind);
    public static string Classify(IReadOnlyList<WearableContentRecord> records, IEnumerable<string> equipmentIds)
    {
        var byId = records.DistinctBy(record => record.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(record => record.Id, StringComparer.OrdinalIgnoreCase);
        var connected = equipmentIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Recipes/outfits/leveled lists that supply this equipment are legitimate set support.
        bool added;
        do
        {
            added = false;
            foreach (var record in records.Where(record => record.Kind is "ConstructibleObject" or "Outfit" or "LeveledItem" or "FormList"))
                if (record.References.Any(connected.Contains)) added |= connected.Add(record.Id);
            foreach (var id in connected.ToArray())
                if (byId.TryGetValue(id, out var record)) foreach (var reference in record.References) added |= connected.Add(reference);
        } while (added);
        var unrelated = records.Where(record => !Equipment.Contains(record.Kind) && !(Support.Contains(record.Kind) && connected.Contains(record.Id)))
            .GroupBy(record => record.Kind).OrderBy(group => group.Key).Select(group => $"{group.Key} ({group.Count()})").ToList();
        return unrelated.Count == 0 ? "" : "Mixed content: " + string.Join(", ", unrelated.Take(8)) + (unrelated.Count > 8 ? ", …" : "");
    }
}
