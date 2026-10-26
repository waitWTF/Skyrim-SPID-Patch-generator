namespace WearableDistributor;

public sealed record QuickAudiencePreset(string Category, string Label, DistributionTargetRule Rule)
{
    public string DisplayCategory => Loc.AudienceLabel(Category);
    public string DisplayLabel => Loc.AudienceLabel(Label);
}

/// <summary>Presets are resolved from this scan; no downloaded INIs, guessed IDs or phantom mod targets.</summary>
public sealed class DistributionPresetCatalog(NpcTargetingService targeting)
{
    private readonly Dictionary<string, List<QuickAudiencePreset>> cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] CommonGroups = ["Bandits", "Forsworn", "Mages", "Necromancers", "Vampires", "Witches", "Farmers", "Merchants", "Blacksmiths", "Innkeepers", "Servants", "Miners", "Lumberjacks", "Nobles", "Adventurers", "Hunters", "Thieves", "Assassins", "Warriors", "Bards", "Priests", "Wenches", "Flower Girls"];

    public IReadOnlyList<QuickAudiencePreset> GetPresets(string gender, IProgress<string>? progress = null)
    {
        if (cache.TryGetValue(gender, out var existing)) return existing;
        var result = new List<QuickAudiencePreset>();
        AddCollection(NpcTargetingService.AllCollection, "Common NPC groups · all installed NPCs", includeAll: false);
        foreach (var collection in targeting.GetCollections().Where(collection => collection != NpcTargetingService.AllCollection && collection != NpcTargetingService.OfficialCollection))
            AddCollection(collection, "Added by " + collection, includeAll: true);
        return cache[gender] = result;

        void AddCollection(string collection, string category, bool includeAll)
        {
            progress?.Report($"Preparing {gender.ToLowerInvariant()} NPC groups · {collection}…");
            foreach (var group in CommonGroups.Concat(FactionAudienceCatalog.Groups).Append(NpcTargetingService.UniqueGroup))
            {
                var rule = targeting.BuildRule(group, collection);
                if (rule.Filters.Count == 0) continue;
                var faction = FactionAudienceCatalog.IsKnown(group);
                Add(faction ? group + " · All members" : group, rule, faction);
                if (faction || group is "Bandits" or "Forsworn" or "Adventurers")
                    foreach (var roleGroup in targeting.GetRoles(collection, group).GroupBy(NpcTargetingService.FriendlyLabel).Take(32))
                    {
                        var role = roleGroup.Key;
                        var choices = roleGroup.ToList();
                        if (choices.Count == 0) continue;
                        var refined = targeting.BuildRule(group, collection);
                        refined.Filters.AddRange(choices.Select(choice => new KeywordChoice { Plugin = choice.Plugin, LocalId = choice.LocalId, EditorId = choice.EditorId, TargetKind = choice.TargetKind, Category = "NPC role" }));
                        Add(group + " · " + role, refined, faction);
                    }
            }
            if (includeAll) Add("All eligible NPCs from this mod", targeting.BuildRule("All NPCs", collection));
            void Add(string label, DistributionTargetRule rule, bool faction = false)
            {
                targeting.Resolve(rule, gender);
                if (rule.EstimatedNpcCount > 0) result.Add(new QuickAudiencePreset(
                    faction ? "Factions & guilds · " + collection : category,
                    $"{label} · {rule.EstimatedNpcCount} NPCs" + (rule.ProtectedNpcCount > 0 ? $" · ⚠ Protected ({rule.ProtectedNpcCount})" : rule.Group == NpcTargetingService.UniqueGroup ? " · ⚠ Quest/script caution" : ""), rule));
            }
        }
    }
}
