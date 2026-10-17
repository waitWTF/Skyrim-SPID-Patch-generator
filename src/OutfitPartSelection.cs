using System.Globalization;
using System.Text.RegularExpressions;

namespace WearableDistributor;

/// <summary>Deterministic body-slot assistance. This does not inspect meshes or guarantee visual compatibility.</summary>
public static class OutfitPartSelection
{
    private static readonly Dictionary<string, int> NamedSlots = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Head"] = 30, ["Hair"] = 31, ["Body"] = 32, ["Hands"] = 33, ["Forearms"] = 34,
        ["Amulet"] = 35, ["Ring"] = 36, ["Feet"] = 37, ["Calves"] = 38, ["Shield"] = 39,
        ["Tail"] = 40, ["LongHair"] = 41, ["Circlet"] = 42, ["Ears"] = 43,
        ["DecapitateHead"] = 50, ["Decapitate"] = 51, ["FX01"] = 61
    };

    public static HashSet<string> BodySlots(string text)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in (text ?? "").Split(['|', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (uint.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mask))
            {
                for (var bit = 0; bit < 32; bit++) if ((mask & 1U << bit) != 0) result.Add("Slot " + (bit + 30));
                continue;
            }
            var compact = Regex.Replace(token, @"\s+", "");
            if (NamedSlots.TryGetValue(compact, out var slot)) result.Add("Slot " + slot);
            else if (Regex.Match(compact, @"^(?:Slot|Unnamed)(\d+)$", RegexOptions.IgnoreCase) is { Success: true } numbered) result.Add("Slot " + numbered.Groups[1].Value);
            else if (!compact.Equals("None", StringComparison.OrdinalIgnoreCase)) result.Add(compact);
        }
        return result;
    }

    public static IReadOnlyList<WearableRecord> FindCompatibleSet(IEnumerable<WearableRecord> candidates)
    {
        var all = candidates.DistinctBy(piece => piece.FormId, StringComparer.OrdinalIgnoreCase).ToList();
        var color = all.Select(piece => piece.Variant).FirstOrDefault(variant => variant != "Standard / unmarked");
        var sameColor = all.Where(piece => piece.Variant == "Standard / unmarked" || piece.Variant.Equals(color, StringComparison.OrdinalIgnoreCase));
        var parsed = sameColor.Select(piece => (Piece: piece, Slots: BodySlots(piece.Slots)))
            // Prefer the central garment before optional parts that reserve one of its slots.
            .OrderByDescending(item => item.Slots.Contains("Slot 32"))
            .ThenBy(item => item.Piece.LocalId).ThenBy(item => item.Piece.Name, StringComparer.OrdinalIgnoreCase);
        var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var chosen = new List<WearableRecord>();
        foreach (var (piece, slots) in parsed)
        {
            if (occupied.Overlaps(slots)) continue;
            chosen.Add(piece); occupied.UnionWith(slots);
        }
        return chosen;
    }
}
