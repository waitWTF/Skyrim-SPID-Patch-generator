using System.Text.RegularExpressions;

namespace WearableDistributor;

/// <summary>Builds audience choices from effective, encountered adult NPC populations.</summary>
public sealed class NpcTargetingService
{
    public const string OfficialCollection = "Skyrim + official DLC";
    public const string AllCollection = "All installed NPCs";
    public const string UniqueGroup = "Unique NPCs";
    public const string UniqueWarning = "Unique NPCs may have scripted outfits or quest scenes. Replacing their clothing can conflict with those changes; static scans cannot guarantee quest compatibility. Uniformed protectors stay excluded from this group.";
    private readonly List<KeywordChoice> choices;
    private readonly List<NpcTargetProfile> profiles;
    private readonly Dictionary<string, KeywordChoice> byForm;
    private readonly Dictionary<string, HashSet<string>> wordsByForm;
    private readonly Dictionary<string, IReadOnlyList<NpcTargetProfile>> collectionCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string[]> Groups = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bandits"] = ["bandit", "bandits", "raider", "raiders"], ["Forsworn"] = ["forsworn"],
        ["Mages"] = ["mage", "wizard", "sorcerer", "spellcaster"],
        ["Necromancers"] = ["necromancer", "necromancy", "necro"], ["Farmers"] = ["farmer", "farmhand", "farming"],
        ["Merchants"] = ["merchant", "trader", "vendor"], ["Adventurers"] = ["adventurer", "explorer"],
        ["Warriors"] = ["warrior", "knight", "soldier", "fighter"], ["Hunters"] = ["hunter", "ranger"],
        ["Thieves"] = ["thief", "thieves", "thievesguild"], ["Assassins"] = ["assassin", "darkbrotherhood"],
        ["Vampires"] = ["vampire"], ["Wenches"] = ["wench", "wenches"], ["Witches"] = ["witch", "warlock"],
        ["Bards"] = ["bard"], ["Priests"] = ["priest", "cleric"], ["Khajiit"] = ["khajiit"],
        ["Blacksmiths"] = ["blacksmith", "smith"], ["Innkeepers"] = ["innkeeper", "innserver", "barkeep", "barmaid"],
        ["Servants"] = ["servant", "maid"], ["Miners"] = ["miner", "mining"],
        ["Lumberjacks"] = ["lumberjack", "woodcutter"], ["Nobles"] = ["noble", "nobles", "jarl"],
        ["Flower Girls"] = ["dibellan", "courtesan"],
        ["Argonians"] = ["argonian"], ["Orcs"] = ["orc", "orsimer"], ["Nords"] = ["nord"],
        ["Elves"] = ["elf", "elves", "altmer", "bosmer", "dunmer", "highelf", "woodelf", "darkelf"]
    };

    public NpcTargetingService(IEnumerable<KeywordChoice> choices, IEnumerable<NpcTargetProfile> profiles)
    {
        this.choices = choices.DistinctBy(choice => choice.FormId, StringComparer.OrdinalIgnoreCase).ToList();
        var protectionChoices = this.choices.ToDictionary(choice => choice.FormId, StringComparer.OrdinalIgnoreCase);
        var population = profiles.ToList();
        foreach (var profile in population)
        {
            profile.IsProtectedUniformed |= UniformedNpcPolicy.IsProtected(profile, protectionChoices);
            profile.IsProtectedGuard |= UniformedNpcPolicy.IsGuard(profile, protectionChoices);
        }
        this.profiles = population.Where(profile => profile.HasWearableBody && !profile.IsProtectedGuard).ToList();
        byForm = this.choices.ToDictionary(choice => choice.FormId, StringComparer.OrdinalIgnoreCase);
        wordsByForm = this.choices.ToDictionary(choice => choice.FormId, choice => Words(choice.EditorId).ToHashSet(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> GetCollections() => new[] { OfficialCollection, AllCollection }
        .Concat(profiles.Select(profile => profile.Plugin).Where(plugin => !IsOfficialPlugin(plugin))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)).ToList();

    public IReadOnlyList<string> GetGroupLabels(string collection, string? plugin = null)
    {
        var population = InCollection(collection, plugin).ToList();
        var groups = Groups.Keys.Concat(FactionAudienceCatalog.Groups).Append(UniqueGroup).Where(group => population.Any(profile => InGroup(profile, group))).ToList();
        var used = population.SelectMany(profile => profile.TargetForms).ToHashSet(StringComparer.OrdinalIgnoreCase);
        groups.AddRange(choices.Where(choice => choice.TargetKind == "Faction" && used.Contains(choice.FormId))
            .Where(choice => MeaningfulFaction(choice.EditorId) && !Groups.Any(group => MatchesWords(choice.EditorId, group.Value)))
            .Select(choice => Humanize(choice.EditorId)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(label => population.Any(profile => InGroup(profile, label))).Order(StringComparer.OrdinalIgnoreCase));
        if (population.Any(profile => profile.CanReceiveWearables)) groups.Add("All NPCs");
        return groups;
    }

    public List<KeywordChoice> GetRoles(string collection, string group, string? plugin = null) => group == UniqueGroup
        ? InCollection(collection, plugin).Where(profile => InGroup(profile, group)).Select(profile =>
        {
            var choice = NpcChoice(profile); choice.EditorId += " · " + profile.Plugin + " · " + profile.FormId.Split('~')[0]; return choice;
        }).OrderBy(choice => choice.EditorId, StringComparer.OrdinalIgnoreCase).ToList()
        : PopulationChoices(collection, group, plugin).Where(choice => choice.TargetKind == "Class" ||
            choice.TargetKind == "Keyword" && choice.EditorId.StartsWith("Job", StringComparison.OrdinalIgnoreCase) ||
            choice.TargetKind is "Keyword" or "Faction" && MatchesWords(choice.EditorId, ["archer", "missile", "marksman", "chief", "boss", "mage", "warrior", "melee", "scout", "healer", "spellsword", "archmage", "harbinger", "recruit", "initiate", "leader", "shaman"]))
            .OrderBy(FriendlyLabel, StringComparer.OrdinalIgnoreCase).ToList();

    public List<KeywordChoice> GetRaces(string collection, string group, string? plugin = null) =>
        PopulationChoices(collection, group, plugin).Where(choice => choice.TargetKind == "Race")
            .OrderBy(FriendlyLabel, StringComparer.OrdinalIgnoreCase).ToList();

    public DistributionTargetRule BuildRule(string group, string collection, string? plugin = null)
    {
        var members = InCollection(collection, plugin).Where(profile => InGroup(profile, group)).ToList();
        return new DistributionTargetRule
        {
            Collection = collection == "Specific mod plugin" && plugin != null ? plugin : collection,
            Group = group,
            Filters = members.Select(profile => NpcChoice(profile)).ToList(),
            EstimatedNpcCount = members.Count,
            ProtectedNpcCount = members.Count(profile => profile.IsProtectedUniformed),
            ResolvedNpcFormIds = members.Select(profile => profile.FormId).ToList(),
            NpcResolutionComplete = true
        };
    }

    public IReadOnlyList<NpcTargetProfile> GetMatchingProfiles(DistributionTargetRule rule, string gender = "Any")
    {
        if (rule.Filters.Count == 0) return [];
        var groups = rule.Filters.GroupBy(choice => choice.Category, StringComparer.OrdinalIgnoreCase).Select(group =>
            (Npcs: group.Where(choice => choice.TargetKind.Equals("Npc", StringComparison.OrdinalIgnoreCase)).Select(choice => choice.FormId).ToHashSet(StringComparer.OrdinalIgnoreCase),
             Forms: group.Where(choice => !choice.TargetKind.Equals("Npc", StringComparison.OrdinalIgnoreCase)).Select(choice => choice.FormId).ToHashSet(StringComparer.OrdinalIgnoreCase))).ToList();
        return InCollection(rule.Collection, null).Where(profile => EligibleForGroup(profile, rule.Group) &&
            (!FactionAudienceCatalog.IsKnown(rule.Group) && rule.Group != UniqueGroup || InGroup(profile, rule.Group)) &&
            (profile.Variants.Count == 0
                ? Fits(profile.FormId, profile.Gender, profile.TargetForms)
                : profile.Variants.All(variant => Fits(profile.FormId, variant.Gender, variant.TargetForms)))).ToList();
        bool Fits(string npcId, string sex, HashSet<string> forms) =>
            (gender.Equals("Any", StringComparison.OrdinalIgnoreCase) || gender.Equals(sex, StringComparison.OrdinalIgnoreCase)) &&
            groups.All(group => group.Npcs.Contains(npcId) || group.Forms.Overlaps(forms));
    }
    public int CountMatches(DistributionTargetRule rule, string gender = "Any") => GetMatchingProfiles(rule, gender).Count;

    public void Resolve(DistributionTargetRule rule, string gender = "Any")
    {
        var matched = GetMatchingProfiles(rule, gender);
        rule.ResolvedNpcFormIds = matched.Select(profile => profile.FormId).ToList();
        rule.ProtectedNpcCount = matched.Count(profile => profile.IsProtectedUniformed);
        rule.EstimatedNpcCount = rule.ResolvedNpcFormIds.Count;
        rule.NpcResolutionComplete = true;
    }

    public static bool Matches(DistributionTargetRule rule, NpcTargetProfile profile, string gender = "Any")
    {
        if (!EligibleForGroup(profile, rule.Group) || rule.Filters.Count == 0) return false;
        if (!InCollection(profile, rule.Collection, null)) return false;
        var groups = rule.Filters.GroupBy(choice => choice.Category, StringComparer.OrdinalIgnoreCase).ToList();
        return profile.Variants.Count == 0 ? Fits(profile.Gender, profile.TargetForms) : profile.Variants.All(variant => Fits(variant.Gender, variant.TargetForms));
        bool Fits(string sex, HashSet<string> forms) =>
            (gender.Equals("Any", StringComparison.OrdinalIgnoreCase) || gender.Equals(sex, StringComparison.OrdinalIgnoreCase)) &&
            groups.All(group => group.Any(choice => choice.TargetKind.Equals("Npc", StringComparison.OrdinalIgnoreCase)
                ? choice.FormId.Equals(profile.FormId, StringComparison.OrdinalIgnoreCase) : forms.Contains(choice.FormId)));
    }

    public static bool IsOfficialPlugin(string plugin) => new[] { "Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm" }
        .Contains(plugin, StringComparer.OrdinalIgnoreCase);

    private IEnumerable<NpcTargetProfile> InCollection(string collection, string? plugin)
    {
        var key = collection + "\0" + plugin;
        if (!collectionCache.TryGetValue(key, out var population)) collectionCache[key] = population = profiles.Where(profile => InCollection(profile, collection, plugin)).ToList();
        return population;
    }
    private static bool InCollection(NpcTargetProfile profile, string collection, string? plugin) => collection switch
    {
        OfficialCollection => IsOfficialPlugin(profile.Plugin),
        "Creation Club" => profile.Plugin.StartsWith("cc", StringComparison.OrdinalIgnoreCase),
        "Specific mod plugin" => plugin == null || profile.Plugin.Equals(plugin, StringComparison.OrdinalIgnoreCase),
        AllCollection or "All installed mods" or "All NPCs" or "Advanced selection" => true,
        _ => profile.Plugin.Equals(collection, StringComparison.OrdinalIgnoreCase)
    };

    private bool InGroup(NpcTargetProfile profile, string group)
    {
        if (!EligibleForGroup(profile, group)) return false;
        if (group == UniqueGroup) return profile.IsUnique;
        if (group == "All NPCs") return true;
        return profile.Variants.Count == 0 ? InGroupForms(profile.TargetForms, profile.OccupationTags) : profile.Variants.All(variant => InGroupForms(variant.TargetForms, variant.OccupationTags));
        bool InGroupForms(HashSet<string> forms, HashSet<string> occupationTags)
        {
        if (FactionAudienceCatalog.IsKnown(group)) return FactionAudienceCatalog.Matches(group, forms, byForm);
        if (!Groups.TryGetValue(group, out var aliases))
            return forms.Any(form => byForm.TryGetValue(form, out var choice) && !IsNegativeEvidence(choice.EditorId) && Humanize(choice.EditorId).Equals(group, StringComparison.OrdinalIgnoreCase));
        // Occupations must follow occupation records or actual AI package evidence, never the NPC's name.
        if (group == "Farmers" && occupationTags.Contains("Farmer")) return true;
        if (group == "Wenches" && (HasWenchEvidence(profile.EditorId) || MatchesWords(profile.Name, ["wench", "wenches"]))) return true;
        if (group == "Adventurers" && !IsOfficialPlugin(profile.Plugin) && !IsNegativeEvidence(profile.EditorId) && MatchesWords(profile.EditorId, ["adventurer", "explorer"])) return true;
        return forms.Any(form => byForm.TryGetValue(form, out var choice) &&
            (choice.TargetKind is "Faction" or "Class" or "Keyword" or "Race") && !IsNegativeEvidence(choice.EditorId) &&
            (HasAlias(form, aliases) || group == "Wenches" && HasWenchEvidence(choice.EditorId)) &&
            (group != "Farmers" || !MatchesWords(choice.EditorId, ["animal", "animals", "crime", "ownership"])));
        }
    }

    private static bool EligibleForGroup(NpcTargetProfile profile, string group) => profile.CanReceiveWearables ||
        profile.HasWearableBody && !profile.IsProtectedGuard && FactionAudienceCatalog.IsKnown(group);

    private bool HasAlias(string form, IEnumerable<string> aliases) => wordsByForm.TryGetValue(form, out var words) &&
        aliases.Any(alias => words.Contains(alias) || words.Contains(alias + "s") || string.Concat(words).Equals(alias, StringComparison.OrdinalIgnoreCase));

    private static bool IsNegativeEvidence(string editorId) => Words(editorId).Any(word => word.StartsWith("exclud", StringComparison.OrdinalIgnoreCase) || word is "exclusion" or "not");
    private static bool HasWenchEvidence(string editorId) => !IsNegativeEvidence(editorId) && Words(editorId)
        .Any(word => word.EndsWith("wench", StringComparison.OrdinalIgnoreCase) || word.EndsWith("wenches", StringComparison.OrdinalIgnoreCase) || word.StartsWith("wench", StringComparison.OrdinalIgnoreCase));

    private IEnumerable<KeywordChoice> PopulationChoices(string collection, string group, string? plugin)
    {
        var used = InCollection(collection, plugin).Where(profile => InGroup(profile, group)).SelectMany(profile => profile.TargetForms).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return choices.Where(choice => used.Contains(choice.FormId));
    }

    private static KeywordChoice NpcChoice(NpcTargetProfile profile)
    {
        var split = profile.FormId.Split('~', 2);
        return new KeywordChoice { EditorId = string.IsNullOrWhiteSpace(profile.Name) ? profile.EditorId : profile.Name,
            Plugin = split[1], LocalId = Convert.ToUInt32(split[0][2..], 16), TargetKind = "Npc", Category = "NPC audience" };
    }

    private static bool MeaningfulFaction(string id) => !IsNegativeEvidence(id) && MatchesWords(id,
        ["wench", "maid", "courtesan", "adventurer", "farmer", "merchant", "bandit", "hunter", "guard", "priest", "soldier", "assassin", "mage", "necromancer", "guild", "college", "brotherhood", "companions", "dawnguard", "clan", "order"]);
    private static bool MatchesWords(string text, IEnumerable<string> aliases)
    {
        var words = Words(text);
        var merged = string.Concat(words);
        return aliases.Any(alias => words.Contains(alias, StringComparer.OrdinalIgnoreCase) || words.Contains(alias + "s", StringComparer.OrdinalIgnoreCase) || merged.Equals(alias, StringComparison.OrdinalIgnoreCase));
    }
    private static string[] Words(string text) => Regex.Replace(SplitCamelCase(text), "[^a-zA-Z0-9]+", " ").ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    private static string SplitCamelCase(string text) => Regex.Replace(Regex.Replace(text, "([A-Z]+)([A-Z][a-z])", "$1 $2"), "([a-z0-9])([A-Z])", "$1 $2");
    public static string Humanize(string text) => Regex.Replace(SplitCamelCase(text), "[_]+", " ").Trim();

    public static string FriendlyLabel(KeywordChoice choice)
    {
        if (choice.TargetKind == "Npc") return choice.EditorId;
        if (choice.TargetKind != "Race")
        {
            var text = choice.EditorId;
            if (MatchesWords(text, ["archmage", "harbinger", "leader"])) return "Leaders";
            if (MatchesWords(text, ["recruit", "initiate"])) return "Recruits and initiates";
            if (MatchesWords(text, ["shaman"])) return "Shamans";
            if (MatchesWords(text, ["chief", "boss"])) return "Chiefs and leaders";
            if (MatchesWords(text, ["archer", "marksman", "missile", "ranged", "bow"])) return "Archers";
            if (MatchesWords(text, ["necromancer", "necromancy", "necro"])) return "Necromancers";
            if (MatchesWords(text, ["healer"])) return "Healers";
            if (MatchesWords(text, ["spellsword"])) return "Spellswords";
            if (MatchesWords(text, ["mage", "wizard", "sorcerer"])) return "Mages";
            if (MatchesWords(text, ["twohanded", "2h"])) return "Two handed fighters";
            if (MatchesWords(text, ["onehanded", "1h"])) return "One handed fighters";
            if (MatchesWords(text, ["melee", "warrior"])) return "Melee fighters";
            if (MatchesWords(text, ["farmer", "farmhand", "farming"])) return "Farmers";
            if (MatchesWords(text, ["merchant", "trader", "vendor"])) return "Merchants";
        }
        var label = Humanize(choice.EditorId);
        label = Regex.Replace(label, @"^(?:(?:Enc|Combat|Class|Job|DLC\s*\d+)\s*)+", "", RegexOptions.IgnoreCase);
        label = Regex.Replace(label, @"\s*(?:Race|Faction|Keyword)$", "", RegexOptions.IgnoreCase);
        return string.IsNullOrWhiteSpace(label) ? choice.EditorId : label;
    }
}
