using System.Text.RegularExpressions;

namespace WearableDistributor;

/// <summary>Restricts confirmed one-sex worn models. Does not infer sex from a body preset or thumbnail.</summary>
public static class OutfitGenderPolicy
{
    public static string RequiredGender(IEnumerable<WearableRecord> pieces)
    {
        var sexes = pieces.Where(piece => piece.Include).Select(piece => piece.SupportedGender)
            .Where(sex => sex is "Female" or "Male").Distinct().ToList();
        return sexes.Count > 1 ? "Conflict" : sexes.FirstOrDefault() ?? "Any";
    }

    public static string? ValidationIssue(PluginPatchOptions config)
    {
        var required = RequiredGender(config.Pieces);
        if (required == "Conflict") return "Selected parts mix female-only and male-only worn models. Exclude incompatible parts or create separate outfits.";
        return required != "Any" && config.Gender != required
            ? $"This outfit has {required.ToLowerInvariant()}-only parts. Choose {required} NPCs to avoid incompatible or invisible equipment."
            : null;
    }

    // Explicit user-confirmed family. A later installed conversion with models for both sexes takes precedence.
    public static string KnownRestriction(string plugin)
    {
        var stem = Regex.Replace(System.IO.Path.GetFileNameWithoutExtension(plugin), "[^a-z0-9]", "", RegexOptions.IgnoreCase);
        return stem.Equals("ObiLunarClawOutfit", StringComparison.OrdinalIgnoreCase) || stem.Equals("ObisLunarClawOutfit", StringComparison.OrdinalIgnoreCase)
            ? "Female" : "Unknown";
    }
}
