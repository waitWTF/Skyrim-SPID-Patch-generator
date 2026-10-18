using WearableDistributor;

public static class UniformedProtectionChecks
{
    public static void Run()
    {
        var warrior = Choice("CombatWarrior", 1, "Class");
        var guard = Choice("GuardFaction", 2, "Faction");
        var imperial = Choice("ImperialRace", 3, "Race");
        var crime = Choice("CrimeFactionWhiterun", 4, "Faction");
        var pGuard = Profile(10, "GuardSolitudeDay", warrior, guard);
        var soldier = Profile(11, "CWSonsSoldier", warrior);
        var bandit = Profile(12, "BanditWarrior", warrior);
        var civilian = Profile(13, "RedguardMerchant", imperial, crime);
        var inherited = Profile(14, "MixedEncounter", warrior);
        inherited.Variants.Add(new NpcTargetVariant { TargetForms = [guard.FormId] });
        var targeting = new NpcTargetingService([warrior, guard, imperial, crime], [pGuard, soldier, bandit, civilian, inherited]);
        var all = targeting.BuildRule("All NPCs", NpcTargetingService.AllCollection);
        Check(all.ResolvedNpcFormIds.Count == 2 && all.ResolvedNpcFormIds.Contains(bandit.FormId) && all.ResolvedNpcFormIds.Contains(civilian.FormId), "all-NPC audiences exclude uniformed and mixed guard templates but keep civilians");
        Check(!targeting.GetGroupLabels(NpcTargetingService.AllCollection).Contains("Guards"), "guard group removed");
        Check(targeting.BuildRule("Warriors", NpcTargetingService.AllCollection).ResolvedNpcFormIds.SequenceEqual([bandit.FormId]), "warrior audience does not reintroduce guards");
        var saved = new DistributionTargetRule { Collection = NpcTargetingService.AllCollection, Filters = [new KeywordChoice { TargetKind = "Npc", Plugin = "Skyrim.esm", LocalId = 10, Category = "NPC audience" }] };
        targeting.Resolve(saved);
        Check(saved.ResolvedNpcFormIds.Count == 0 && !NpcTargetingService.Matches(saved, pGuard), "saved explicit guard selections cannot bypass protection");
        foreach (var identity in new[] { "Bodyguard", "WhiterunGuardFaction", "CWImperialFaction", "ThalmorSoldier", "PenitusOculatus", "HousecarlWhiterun", "DLC1Dawnguard", "VigilantOfStendarr", "LegateRikke" })
            Check(UniformedNpcPolicy.HasProtectedIdentity(identity), "protected identity " + identity);
        foreach (var identity in new[] { "RedguardRace", "ImperialRace", "CrimeFactionWhiterun", "BanditWarrior", "ImperialMerchant" })
            Check(!UniformedNpcPolicy.HasProtectedIdentity(identity), "civilian identity " + identity);
    }
    private static KeywordChoice Choice(string name, uint id, string kind) => new() { EditorId = name, LocalId = id, Plugin = "Skyrim.esm", TargetKind = kind, Category = kind };
    private static NpcTargetProfile Profile(uint id, string name, params KeywordChoice[] choices) => new() { FormId = $"0x{id:X6}~Skyrim.esm", Plugin = "Skyrim.esm", EditorId = name, Name = name, Gender = "Male", IsHumanoid = true, IsEncounterable = true, TargetForms = choices.Select(c => c.FormId).ToHashSet(StringComparer.OrdinalIgnoreCase) };
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException("Uniformed protection: " + message); }
}
