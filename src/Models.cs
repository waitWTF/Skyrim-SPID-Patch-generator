using System.Text.RegularExpressions;

namespace WearableDistributor;

public sealed class WearableRecord : System.ComponentModel.INotifyPropertyChanged
{
    public string Plugin { get; set; } = "";
    public uint LocalId { get; set; }
    public string EditorId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Slots { get; set; } = "";
    public string Category { get; set; } = "Clothing";
    private string partMode = "Required";
    private int optionalChance = 50;
    public static string[] PartModes { get; } = ["Required", "Optional", "Excluded"];
    public string PartMode
    {
        get => partMode;
        set { if (!PartModes.Contains(value) || partMode == value) return; partMode = value; Changed(nameof(PartMode)); }
    }
    public int OptionalChance { get => optionalChance; set { if (optionalChance == value) return; optionalChance = value; Changed(nameof(OptionalChance)); } }
    public bool Include { get => PartMode != "Excluded"; set => PartMode = value ? "Required" : "Excluded"; }
    public bool IsRequired { get => PartMode == "Required"; set => PartMode = value ? "Required" : "Optional"; }
    public bool IsOptionalPart => PartMode == "Optional";
    public bool IsLewd => OutfitPartPresets.IsLewd(this);
    public string ContentLabel => IsLewd ? "LEWD · explicit opt-in" : "Unmarked";
    public string InclusionLabel => PartMode == "Optional" ? $"Optional {OptionalChance}%" : PartMode;
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string primary)
    {
        var names = primary == nameof(PartMode) ? new[] { nameof(PartMode), nameof(Include), nameof(IsRequired), nameof(IsOptionalPart), nameof(InclusionLabel) } : new[] { nameof(OptionalChance), nameof(InclusionLabel) };
        foreach (var name in names) PropertyChanged?.Invoke(this, new(name));
    }
    public string SourcePath { get; set; } = "";
    // Unknown is deliberately different from support for both sexes. These are record/model hints, not mesh validation.
    public string SupportedGender { get; set; } = "Unknown";
    public string GenderEvidence { get; set; } = "";
    public List<WornModelPaths> WornModels { get; set; } = [];
    public string MeshEvidence { get; set; } = "Not checked";
    public string Variant
    {
        get
        {
            // Recognize bracketed and plain annotations consistently, including Rogue [Dark].
            var color = Regex.Match(Name, @"\b(?:(?:dark|light)\s+)?(?:black|white|red|blue|green|purple|pink|yellow|orange|brown|grey|gray|gold|silver)\b|\b(?:dark|light)\b", RegexOptions.IgnoreCase);
            if (!color.Success) return "Standard / unmarked";
            return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Regex.Replace(color.Value.ToLowerInvariant().Replace("grey", "gray"), @"\s+", " "));
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
    public MeshAssetIndex? MeshAssets { get; set; }
    public List<WearableRecord> Weapons { get; set; } = [];
    public string Plugin { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public List<WearableRecord> Pieces { get; set; } = [];
    public bool Selected { get; set; }
    public string FilterReason { get; set; } = "";
    public string PatchStatus { get; set; } = "Not patched";
    public string PatchLocations { get; set; } = "";
    public string DistributionNote { get; set; } = "";
    public bool HasGeneratedPatch { get; set; }
    public bool SourceDetected { get; set; } = true;
    public string LibraryNote => string.Join(" · ", new[] { FilterReason, DistributionNote, PatchLocations }.Where(text => !string.IsNullOrWhiteSpace(text)));
    public string ValidationIssue { get; set; } = "";
    public bool CanPatch => SourceDetected && !WearableFilterService.IsOfficialContent(Plugin) && string.IsNullOrWhiteSpace(ValidationIssue);
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
    public string WardrobeName { get; set; } = "";
    public int WardrobeWeight { get; set; } = 1;
    public bool MatchVariants { get; set; }
    public string PartPreset { get; set; } = "Custom";
    public string PreferredColor { get; set; } = "";
    public bool AllowLewdParts { get; set; }
    public bool DistributeToNpcs { get; set; } = true;
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
    public int ProtectedNpcCount { get; set; }
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
    public bool IsUnique { get; set; }
    public bool IsHumanoid { get; set; }
    public bool IsEncounterable { get; set; }
    public string EncounterEvidence { get; set; } = "";
    public bool HasUnresolvedTemplate { get; set; }
    public bool UsesTemplateInventory { get; set; }
    public List<NpcTargetVariant> Variants { get; set; } = [];
    public HashSet<string> OccupationTags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsProtectedUniformed { get; set; }
    public bool IsProtectedGuard { get; set; }
    public bool HasWearableBody => IsHumanoid && !IsChild && IsEncounterable && !HasUnresolvedTemplate;
    public bool CanReceiveWearables => HasWearableBody && !UniformedNpcPolicy.IsProtected(this);
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
    public string BaseGameDataPath { get; set; } = "";
    public string PluginListPath { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public string Framework { get; set; } = "SPID";
    public string DefaultChance { get; set; } = "25";
    public bool IncludeAccessories { get; set; }
    public HashSet<string> IgnoredPlugins { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> UnhiddenPlugins { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> IncludedPlugins { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> DisabledPlugins { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int SmartFilterRevision { get; set; }
    public List<string> AdditionalExclusions { get; set; } = [];
    public Dictionary<string, SavedDistributionOptions> DistributionSettings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, SavedDistributionOptions> DistributionPresets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> PatchSearchFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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
    public string WardrobeName { get; set; } = "";
    public int WardrobeWeight { get; set; } = 1;
    public List<DistributionTargetRule> WardrobeTargets { get; set; } = [];
    public bool WardrobeRuleOwner { get; set; }
    public List<OutfitColorBranch> VariantBranches { get; set; } = [];
    public bool MatchVariants { get; set; }
    public string PartPreset { get; set; } = "Custom";
    public List<SavedPartChoice> PartChoices { get; set; } = [];
    public bool DistributeToNpcs { get; set; } = true;
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
    public string OutputDirectory { get; set; } = "";
    public string PatchPlugin { get; set; } = "";
    public bool CanManage { get; set; } = true;
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
    public string WardrobeName { get; set; } = "";
    public int WardrobeWeight { get; set; } = 1;
    public bool MatchVariants { get; set; }
    public string PartPreset { get; set; } = "Custom";
    public string PreferredColor { get; set; } = "";
    public bool AllowLewdParts { get; set; }
    public List<SavedPartChoice> PartChoices { get; set; } = [];
    public bool DistributeToNpcs { get; set; } = true;
    public List<string> MerchantCategories { get; set; } = [];
    public bool IncludeMerchandiseWeapons { get; set; } = true;
    public string OutfitName { get; set; } = "";
    public string Chance { get; set; } = "25";
    public string Gender { get; set; } = "Any";
    public List<string> ExcludedPieceFormIds { get; set; } = [];
    public List<SavedDistributionTargetRule> TargetRules { get; set; } = [];
}

public sealed record SavedPartChoice(string FormId, string Mode, int Chance);

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
