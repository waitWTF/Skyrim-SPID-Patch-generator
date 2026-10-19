using System.Text.RegularExpressions;

namespace WearableDistributor;

/// <summary>Keep uniformed protectors out of every wearable audience, including broad selections.</summary>
public static class UniformedNpcPolicy
{
    public static bool HasGuardIdentity(string text)
    {
        var split = Regex.Replace(Regex.Replace(text ?? "", "([A-Z]+)([A-Z][a-z])", "$1 $2"), "([a-z0-9])([A-Z])", "$1 $2");
        return Regex.Matches(split, "[A-Za-z]+").Any(match => new[] { "guard", "guards", "bodyguard", "bodyguards", "watchman", "watchmen", "citywatch", "housecarl", "housecarls" }.Contains(match.Value, StringComparer.OrdinalIgnoreCase));
    }
    public static bool IsGuard(NpcTargetProfile profile, IReadOnlyDictionary<string, KeywordChoice> choices) =>
        profile.IsProtectedGuard || HasGuardIdentity(profile.EditorId) || HasGuardIdentity(profile.Name) ||
        profile.TargetForms.Concat(profile.Variants.SelectMany(variant => variant.TargetForms)).Any(form =>
            choices.TryGetValue(form, out var choice) && choice.TargetKind is "Faction" or "Class" or "Keyword" or "Outfit" && HasGuardIdentity(choice.EditorId));
    private static readonly HashSet<string> ProtectedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "guard", "guards", "bodyguard", "bodyguards", "watchman", "watchmen", "citywatch",
        "soldier", "soldiers", "stormcloak", "stormcloaks", "legion", "legionary", "legionnaire",
        "officer", "officers", "commander", "commanders", "legate", "legates", "captain",
        "housecarl", "housecarls", "thalmor", "penitus", "oculatus", "dawnguard", "vigilant", "vigilants"
    };

    public static bool HasProtectedIdentity(string text)
    {
        // Split acronyms and camel case: CWImperialFaction, GuardSolitudeDay.
        // Redguard and ordinary Imperial citizens must NOT count as guards/soldiers.
        var split = Regex.Replace(Regex.Replace(text ?? "", "([A-Z]+)([A-Z][a-z])", "$1 $2"), "([a-z0-9])([A-Z])", "$1 $2");
        var words = Regex.Matches(split, "[A-Za-z]+").Select(match => match.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return words.Overlaps(ProtectedWords) ||
            words.Contains("CW") && (words.Contains("Imperial") || words.Contains("Sons"));
    }

    public static bool IsProtected(NpcTargetProfile profile, IReadOnlyDictionary<string, KeywordChoice>? choices = null)
    {
        if (profile.IsProtectedUniformed || HasProtectedIdentity(profile.EditorId) || HasProtectedIdentity(profile.Name)) return true;
        if (choices is null) return false;
        // Inspect all inherited alternatives: a mixed leveled/template base cannot leak a guard variant.
        return profile.TargetForms.Concat(profile.Variants.SelectMany(variant => variant.TargetForms))
            .Any(form => choices.TryGetValue(form, out var choice) &&
                choice.TargetKind is "Faction" or "Class" or "Keyword" or "Outfit" && HasProtectedIdentity(choice.EditorId));
    }
}
