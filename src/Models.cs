using System.Text.RegularExpressions;

namespace WearableDistributor;

public sealed class WearableRecord
{
    public string Plugin { get; set; } = "";
    public uint LocalId { get; set; }
    public string EditorId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Slots { get; set; } = "";
    public string Category { get; set; } = "Clothing";
    public bool Include { get; set; } = true;
    public string SourcePath { get; set; } = "";
    public string Variant
    {
        get
        {
            var match = Regex.Match(Name, @"\(([^)]*(?:light|dark|red|blue|black|white|green|gold|silver|color)[^)]*)\)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : "Standard / unmarked";
        }
    }
    public string FormId => $"0x{LocalId:X6}~{Plugin}";
}

public sealed class OutfitPlan
{
    public string Name { get; set; } = "";
    public string Plugin { get; set; } = "";
    public List<WearableRecord> Pieces { get; set; } = [];
    public string GroupKind { get; set; } = "NPC type";
    public string Group { get; set; } = "ActorTypeNPC";
    public string Chance { get; set; } = "100";
    public string Gender { get; set; } = "Any";
    public bool Selected { get; set; } = true;
    public string SuggestionReason { get; set; } = "";
}

public sealed class PluginEntry
{
    public List<WearableRecord> Weapons { get; set; } = [];
    public string Plugin { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public List<WearableRecord> Pieces { get; set; } = [];
    public bool Selected { get; set; } = true;
    public string FilterReason { get; set; } = "";
    public string PatchStatus { get; set; } = "Not patched";
    public string ValidationIssue { get; set; } = "";
    public bool CanPatch => string.IsNullOrWhiteSpace(ValidationIssue);
    public bool IsFiltered => !string.IsNullOrWhiteSpace(FilterReason);
    public int PieceCount => Pieces.Count;
}

public sealed class KeywordChoice
{
    public string EditorId { get; set; } = "";
    public string Plugin { get; set; } = "";
    public string Category { get; set; } = "Other";
    public string TargetKind { get; set; } = "Keyword";
    public uint LocalId { get; set; }
    public string FormId => $"0x{LocalId:X6}~{Plugin}";
    public bool Selected { get; set; }
    public string SuggestionReason { get; set; } = "";
}

public sealed class PluginPatchOptions
{
    public List<string> MerchantCategories { get; set; } = [];
    public bool IncludeMerchandiseWeapons { get; set; } = true;
    public List<MerchantTarget> MerchantTargets { get; set; } = [];
    public PluginEntry Source { get; set; } = new();
    public string OutfitName { get; set; } = "";
    public string GroupKind { get; set; } = "NPC type";
    public string TargetGroup { get; set; } = "ActorTypeNPC";
    public string Chance { get; set; } = "25";
    public string Gender { get; set; } = "Any";
    public List<WearableRecord> Pieces { get; set; } = [];
    public List<KeywordChoice> Keywords { get; set; } = [];
    public List<DistributionTargetRule> TargetRules { get; set; } = [];
}

public sealed class DistributionTargetRule
{
    public string Collection { get; set; } = "Skyrim + official DLC";
    public string Group { get; set; } = "";
    public List<KeywordChoice> Filters { get; set; } = [];
    public int EstimatedNpcCount { get; set; }
    public List<string> ResolvedNpcFormIds { get; set; } = [];
    public bool NpcResolutionComplete { get; set; }
}

public sealed class NpcTargetProfile
{
    public string FormId { get; set; } = "";
    public string Name { get; set; } = "";
    public HashSet<string> TargetForms { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Plugin { get; set; } = "";
    public string WinningPlugin { get; set; } = "";
    public string EditorId { get; set; } = "";
    public string Gender { get; set; } = "Any";
    public string RaceFormId { get; set; } = "";
    public bool IsChild { get; set; }
    public bool IsHumanoid { get; set; }
    public bool IsEncounterable { get; set; }
    public string EncounterEvidence { get; set; } = "";
    public bool HasUnresolvedTemplate { get; set; }
    public bool UsesTemplateInventory { get; set; }
    public List<NpcTargetVariant> Variants { get; set; } = [];
    public HashSet<string> OccupationTags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsProtectedUniformed { get; set; }
    public bool CanReceiveWearables => IsHumanoid && !IsChild && IsEncounterable && !HasUnresolvedTemplate && !UniformedNpcPolicy.IsProtected(this);
}

public sealed class NpcTargetVariant
{
    public string Gender { get; set; } = "Any";
    public HashSet<string> TargetForms { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> OccupationTags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AppPreferences
{
    public int SchemaVersion { get; set; } = 2;
    public string DataPath { get; set; } = "";
    public string PluginListPath { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public string Framework { get; set; } = "SPID";
    public string DefaultChance { get; set; } = "25";
    public bool IncludeAccessories { get; set; }
    public HashSet<string> IgnoredPlugins { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> IncludedPlugins { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> AdditionalExclusions { get; set; } = [];
    public Dictionary<string, SavedDistributionOptions> DistributionSettings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, SavedDistributionOptions> DistributionPresets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string BackupOutputPath { get; set; } = "";
}

public sealed class PatchManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string Framework { get; set; } = "SPID";
    public string OutputPlugin { get; set; } = "WearableDistributor.esp";
    public string DistributionPath { get; set; } = "";
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public List<PatchManifestEntry> Entries { get; set; } = [];
}

public sealed class PatchManifestEntry
{
    public List<MerchantStockRule> MerchantStock { get; set; } = [];
    public string SourcePlugin { get; set; } = "";
    public string OutfitName { get; set; } = "";
    public string OutfitEditorId { get; set; } = "";
    public string Framework { get; set; } = "SPID";
    public string DistributionPath { get; set; } = "";
    public List<string> IncludedPieces { get; set; } = [];
    public List<string> ExcludedPieces { get; set; } = [];
    public List<string> RequiredMasters { get; set; } = [];
    public List<string> Audiences { get; set; } = [];
    public int Chance { get; set; } = 100;
    public string Gender { get; set; } = "Any";
    public int DistributionRows { get; set; }
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class PatchInventoryRow
{
    public string Health { get; set; } = "Not checked";
    public string SourcePlugin { get; set; } = "";
    public string IncludedPieces { get; set; } = "";
    public string Audiences { get; set; } = "";
    public string Chance { get; set; } = "";
    public string Gender { get; set; } = "";
    public string LastGenerated { get; set; } = "";
    public string Details { get; set; } = "";
    public PatchManifestEntry Entry { get; set; } = new();
}

public sealed class SavedDistributionOptions
{
    public List<string> MerchantCategories { get; set; } = [];
    public bool IncludeMerchandiseWeapons { get; set; } = true;
    public string OutfitName { get; set; } = "";
    public string Chance { get; set; } = "25";
    public string Gender { get; set; } = "Any";
    public List<string> ExcludedPieceFormIds { get; set; } = [];
    public List<SavedDistributionTargetRule> TargetRules { get; set; } = [];
}

public sealed record MerchantTarget(string Plugin, uint LocalId, string EditorId, string Category);
public sealed record MerchantStockRule(string ContainerPlugin, uint ContainerId, string Category, string ItemPlugin, uint ItemId, string ItemKind);

public sealed class SavedDistributionTargetRule
{
    public string Collection { get; set; } = "Skyrim + official DLC";
    public string Group { get; set; } = "";
    public List<SavedTargetChoice> Filters { get; set; } = [];
}

public sealed class SavedTargetChoice
{
    public string EditorId { get; set; } = "";
    public string Plugin { get; set; } = "";
    public string Category { get; set; } = "Other";
    public string TargetKind { get; set; } = "Keyword";
    public uint LocalId { get; set; }
}
