using System.Text.RegularExpressions;

namespace WearableDistributor;

public sealed record VariantPartGroup(string Family, string Mode, int Chance, List<string> Forms);
public sealed record OutfitColorBranch(string Color, List<VariantPartGroup> Groups);

/// <summary>Conservative name + exact slot matching; unknown overlaps still require a manual choice.</summary>
public static class OutfitVariantPlan
{
    public const string Unmarked = "Standard / unmarked";
    private const string Colors = "black|white|red|blue|green|purple|pink|yellow|orange|brown|grey|gray|gold|silver|dark|light";
    public static string Family(WearableRecord piece)
    {
        var words = Regex.Replace(piece.Name, "([a-z])([A-Z])", "$1 $2");
        // Bracketed color annotations can describe a belt attached to a garment, e.g. Pants [Black Belt].
        words = Regex.Replace(words, @"[\[(][^\])]*\b(?:" + Colors + @")\b[^\])]*[\])]", " ", RegexOptions.IgnoreCase);
        words = Regex.Replace(words, @"\b(?:" + Colors + @"|torn|ripped|tattered|damaged|worn)\b", " ", RegexOptions.IgnoreCase);
        words = Regex.Replace(words, @"[^a-zA-Z0-9]+", " ").Trim().ToLowerInvariant();
        return piece.Plugin.ToLowerInvariant() + "|" + words + "|" + string.Join(",", OutfitPartSelection.BodySlots(piece.Slots).Order());
    }

    public static List<OutfitColorBranch> Create(PluginPatchOptions config)
    {
        var selected = config.Pieces.Where(p => p.Include).DistinctBy(p => p.FormId, StringComparer.OrdinalIgnoreCase).ToList();
        var families = selected.GroupBy(Family).OrderBy(g => g.Key).ToList();
        var colors = selected.Select(p => p.Variant).Where(c => c != Unmarked).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (colors.Count == 0) colors.Add(Unmarked);
        else if (families.Where(g => g.Any(p => p.PartMode == "Required")).All(g => g.Any(p => p.Variant == Unmarked))) colors.Insert(0, Unmarked);
        if (!string.IsNullOrWhiteSpace(config.PreferredColor)) colors = colors.Where(c => c.Equals(config.PreferredColor, StringComparison.OrdinalIgnoreCase)).ToList();
        var branches = new List<OutfitColorBranch>();
        foreach (var color in colors)
        {
            var groups = new List<VariantPartGroup>();
            foreach (var family in families)
            {
                var specific = family.Where(p => p.Variant.Equals(color, StringComparison.OrdinalIgnoreCase)).ToList();
                // A plain version of a colored garment is a fallback, not an extra color mixed into that branch.
                var members = specific.Count > 0 ? specific : family.Where(p => p.Variant == Unmarked).ToList();
                if (members.Count == 0)
                {
                    if (family.Any(p => p.PartMode == "Required")) throw new InvalidOperationException($"{color} has no matching {family.First().Name}. Exclude that incomplete color or choose a single color before generating.");
                    continue;
                }
                if (members.Select(p => p.PartMode).Distinct().Count() != 1 || members.Where(p => p.PartMode == "Optional").Select(p => p.OptionalChance).Distinct().Count() > 1)
                    throw new InvalidOperationException($"Alternatives for {members[0].Name} need the same state and optional chance. Required means one of these versions is guaranteed.");
                groups.Add(new(family.Key, members[0].PartMode, members[0].PartMode == "Required" ? 100 : members[0].OptionalChance, members.Select(p => p.FormId).Order(StringComparer.OrdinalIgnoreCase).ToList()));
            }
            // Separate garment families may never occupy the same slot in a branch.
            for (var i = 0; i < groups.Count; i++) for (var j = i + 1; j < groups.Count; j++)
                if (OutfitPartSelection.BodySlots(selected.First(p => p.FormId == groups[i].Forms[0]).Slots).Overlaps(OutfitPartSelection.BodySlots(selected.First(p => p.FormId == groups[j].Forms[0]).Slots)))
                    throw new InvalidOperationException($"{color}: {selected.First(p => p.FormId == groups[i].Forms[0]).Name} overlaps {selected.First(p => p.FormId == groups[j].Forms[0]).Name}. These are not recognized versions of the same garment; exclude one or use a separate named outfit.");
            if (groups.Count == 0 || !groups.Any(g => g.Mode == "Required")) throw new InvalidOperationException("Each color needs at least one required garment.");
            branches.Add(new(color, groups));
        }
        if (branches.Count == 0) throw new InvalidOperationException("No matching color set is included. Choose another color or include its parts.");
        if (selected.Any(p => !branches.Any(b => b.Groups.Any(g => g.Forms.Contains(p.FormId))))) throw new InvalidOperationException("Some included parts do not belong to the chosen color. Exclude them or select all matching colors.");
        return branches;
    }
}
