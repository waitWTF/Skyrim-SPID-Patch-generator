namespace WearableDistributor;

/// <summary>Membership comes from effective faction links, never an NPC name or quest title.</summary>
public static class FactionAudienceCatalog
{
    // EditorIDs verified against installed official master data. Resolve actual FormIDs from each scan.
    // Mod factions are offered separately by NpcTargetingService when they have matching NPCs.
    private static readonly Dictionary<string, string[]> Memberships = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Thieves Guild"] = ["ThievesGuildFaction"],
        ["Dark Brotherhood"] = ["DarkBrotherhoodFaction"],
        ["Companions"] = ["CompanionsFaction", "CompanionsHarbingerFaction"],
        ["College of Winterhold"] = ["CollegeofWinterholdFaction", "CollegeofWinterholdArchMageFaction"],
        ["Bards College"] = ["SolitudeBardsCollegeFaction"],
        ["Dawnguard"] = ["DLC1DawnguardFaction", "DLC1HunterFaction"],
        ["Volkihar vampires"] = ["DLC1VampireFaction", "DLC1VampireCrimeFaction"],
        ["Blades"] = ["BladesFaction", "BladesRecruitsFaction"],
        ["Greybeards"] = ["GreybeardFaction"],
        ["Imperial Legion"] = ["CWImperialFaction", "ImperialFaction"],
        ["Stormcloaks"] = ["CWSonsFaction", "SonsFaction"],
        ["Vigilants of Stendarr"] = ["VigilantOfStendarrFaction"],
        ["Thalmor"] = ["ThalmorFaction"],
        ["Penitus Oculatus"] = ["PenitusOculatusFaction"],
        ["Silver Hand"] = ["SilverHandFaction"],
        ["Morag Tong"] = ["DLC2MoragTongFaction"],
        ["Skaal"] = ["DLC2SkaalFaction", "DLC2SkaalVillageCitizenFaction", "DLC2SkaalShamanFaction"],
        ["East Empire Company"] = ["WindhelmEastEmpireFaction"]
    };
    public static IEnumerable<string> Groups => Memberships.Keys;
    public static bool IsKnown(string group) => Memberships.ContainsKey(group);
    public static bool Matches(string group, IEnumerable<string> forms, IReadOnlyDictionary<string, KeywordChoice> choices) =>
        Memberships.TryGetValue(group, out var members) && forms.Any(form => choices.TryGetValue(form, out var choice) &&
            choice.TargetKind == "Faction" && members.Contains(choice.EditorId, StringComparer.OrdinalIgnoreCase));
}
