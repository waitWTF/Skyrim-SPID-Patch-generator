namespace WearableDistributor;

public sealed record QuickAudiencePreset(string Category, string Label, DistributionTargetRule Rule);

/// <summary>Presets are resolved from this scan; no downloaded INIs, guessed IDs or phantom mod targets.</summary>
public sealed class DistributionPresetCatalog(NpcTargetingService targeting)
{
    private readonly Dictionary<string, List<QuickAudiencePreset>> cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] CommonGroups = ["Bandits", "Forsworn", "Mages", "Necromancers", "Vampires", "Witches", "Farmers", "Merchants", "Blacksmiths", "Innkeepers", "Servants", "Miners", "Lumberjacks", "Nobles", "Adventurers", "Hunters", "Thieves", "Assassins", "Warriors", "Bards", "Priests", "Wenches", "Flower Girls"];

    public IReadOnlyList<QuickAudiencePreset> GetPresets(string gender)
    {
        if (cache.TryGetValue(gender, out var existing)) return existing;
        var result = new List<QuickAudiencePreset>();
        AddCollection(NpcTargetingService.AllCollection, "Common audiences · all installed NPCs", includeAll: false);
        foreach (var collection in targeting.GetCollections().Where(collection => collection != NpcTargetingService.AllCollection && collection != NpcTargetingService.OfficialCollection))
            AddCollection(collection, "Added by " + collection, includeAll: true);
        return cache[gender] = result;

        void AddCollection(string collection, string category, bool includeAll)
        {
            foreach (var group in CommonGroups)
            {
                var rule = targeting.BuildRule(group, collection);
                Add(group, rule);
                if (group is "Bandits" or "Forsworn" or "Adventurers")
                    foreach (var role in new[] { "Archers", "Chiefs and leaders", "Mages", "Melee fighters" })
                    {
                        var choices = targeting.GetRoles(collection, group).Where(choice => NpcTargetingService.FriendlyLabel(choice) == role).ToList();
                        if (choices.Count == 0) continue;
                        var refined = targeting.BuildRule(group, collection);
                        refined.Filters.AddRange(choices.Select(choice => new KeywordChoice { Plugin = choice.Plugin, LocalId = choice.LocalId, EditorId = choice.EditorId, TargetKind = choice.TargetKind, Category = "NPC role" }));
                        Add(group + " · " + role, refined);
                    }
            }
            if (includeAll) Add("All eligible NPCs from this mod", targeting.BuildRule("All NPCs", collection));
            void Add(string label, DistributionTargetRule rule)
            {
                targeting.Resolve(rule, gender);
                if (rule.EstimatedNpcCount > 0) result.Add(new QuickAudiencePreset(category, $"{label} · {rule.EstimatedNpcCount} NPCs", rule));
            }
        }
    }
}
