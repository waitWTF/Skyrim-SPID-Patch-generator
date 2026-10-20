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

    public static IReadOnlyList<WearableRecord> FindCompatibleSet(IEnumerable<WearableRecord> candidates, bool preferLewd = false)
    {
        var all = candidates.DistinctBy(piece => piece.FormId, StringComparer.OrdinalIgnoreCase).ToList();
        var combined = CombinedAlternatives(all);
        var color = all.OrderBy(piece => combined.Contains(piece.FormId)).ThenByDescending(piece => BodySlots(piece.Slots).Contains("Slot 32")).Select(piece => piece.Variant).FirstOrDefault(variant => variant != "Standard / unmarked");
        var sameColor = all.Where(piece => piece.Variant == "Standard / unmarked" || piece.Variant.Equals(color, StringComparison.OrdinalIgnoreCase));
        var parsed = sameColor.Select(piece => (Piece: piece, Slots: BodySlots(piece.Slots)))
            // Prefer the central garment before optional parts that reserve one of its slots.
            .OrderByDescending(item => item.Slots.Contains("Slot 32"))
            .ThenBy(item => combined.Contains(item.Piece.FormId))
            .ThenBy(item => OutfitPartPresets.IsOptional(item.Piece))
            .ThenByDescending(item => preferLewd && item.Piece.IsLewd)
            .ThenBy(item => item.Piece.LocalId).ThenBy(item => item.Piece.Name, StringComparer.OrdinalIgnoreCase);
        var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var chosen = new List<WearableRecord>();
        foreach (var (piece, slots) in parsed)
        {
            if (occupied.Overlaps(slots)) continue;
            chosen.Add(piece); occupied.UnionWith(slots);
        }
        return chosen;
    }

    public static HashSet<string> CombinedAlternatives(IEnumerable<WearableRecord> candidates)
    {
        var all = candidates.ToList();
        string Words(WearableRecord piece) => Regex.Replace(Regex.Replace(piece.Name, "([a-z])([A-Z])", "$1 $2"), @"[^a-zA-Z0-9]+", " ").Trim();
        bool Combined(WearableRecord piece)
        {
            var words = Words(piece);
            return Regex.IsMatch(words, @"\b(outfit|full|complete|combined|all in one)\b", RegexOptions.IgnoreCase) &&
                !Regex.IsMatch(words, @"\b(top|upper|shirt|pants|shorts|trousers|skirt|bottom|lower|boots?|gloves?|hood|belt|ropes?)\b", RegexOptions.IgnoreCase);
        }
        string Family(WearableRecord piece) => Regex.Replace(Regex.Replace(Words(piece),
            @"\b(outfit|full|complete|combined|all|in|one|set|armor|armour|top|upper|shirt|pants|shorts|trousers|skirt|bottom|lower|boots?|gloves?|hood|belt|ropes?|black|white|red|blue|green|purple|pink|yellow|orange|brown|grey|gray|gold|silver|dark|light)\b", "", RegexOptions.IgnoreCase), @"\s+", "").ToLowerInvariant();
        return all.Where(piece => Combined(piece) && Family(piece).Length > 0 && all.Any(other => other != piece &&
            !Combined(other) && other.Plugin.Equals(piece.Plugin, StringComparison.OrdinalIgnoreCase) && Family(other) == Family(piece) &&
            (other.Variant == piece.Variant || other.Variant == "Standard / unmarked" || piece.Variant == "Standard / unmarked") &&
            BodySlots(other.Slots).Overlaps(BodySlots(piece.Slots))))
            .Select(piece => piece.FormId).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
