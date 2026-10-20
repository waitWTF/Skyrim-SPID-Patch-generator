namespace WearableDistributor;

/// <summary>One immutable scan's audience indexes and eagerly warmed presets, shared by all setup windows.</summary>
public sealed record AdvancedTargetChoice(string Label, KeywordChoice Choice);

public sealed class AudiencePreparation
{
    public IReadOnlyList<AdvancedTargetChoice> AdvancedTargets { get; }
    public NpcTargetingService Targeting { get; }
    public DistributionPresetCatalog Presets { get; }
    public AudiencePreparation(IEnumerable<KeywordChoice> choices, IEnumerable<NpcTargetProfile> profiles, IProgress<string>? progress = null)
    {
        progress?.Report("Indexing scanned NPCs and faction memberships…");
        Targeting = new NpcTargetingService(choices, profiles);
        Presets = new DistributionPresetCatalog(Targeting);
        foreach (var collection in Targeting.GetCollections())
        {
            progress?.Report($"Preparing groups, roles and races · {collection}…");
            foreach (var group in Targeting.GetGroupLabels(collection))
            {
                Targeting.GetRoles(collection, group);
                Targeting.GetRaces(collection, group);
            }
        }
        foreach (var sex in new[] { "Any", "Female", "Male" })
        {
            progress?.Report($"Preparing {sex.ToLowerInvariant()} NPC groups and merchant/faction presets…");
            Presets.GetPresets(sex, progress);
        }
        progress?.Report("Preparing readable advanced target names…");
        AdvancedTargets = choices.DistinctBy(choice => choice.TargetKind + "|" + choice.FormId, StringComparer.OrdinalIgnoreCase)
            .Select(choice => new AdvancedTargetChoice($"{NpcTargetingService.FriendlyLabel(choice)} · {choice.TargetKind} · {choice.Plugin}", choice))
            .OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase).ToList();
        progress?.Report("NPC groups ready. Opening the outfit setup…");
    }
}
