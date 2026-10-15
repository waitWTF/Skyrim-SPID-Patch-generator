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
    public string Plugin { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public List<WearableRecord> Pieces { get; set; } = [];
    public bool Selected { get; set; } = true;
    public string FilterReason { get; set; } = "";
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
}

public sealed class PluginPatchOptions
{
    public PluginEntry Source { get; set; } = new();
    public string GroupKind { get; set; } = "NPC type";
    public string TargetGroup { get; set; } = "ActorTypeNPC";
    public string Chance { get; set; } = "25";
    public string Gender { get; set; } = "Any";
    public List<WearableRecord> Pieces { get; set; } = [];
    public List<KeywordChoice> Keywords { get; set; } = [];
}

public sealed class AppPreferences
{
    public string DataPath { get; set; } = "";
    public string Framework { get; set; } = "SPID";
    public string DefaultChance { get; set; } = "25";
    public bool IncludeAccessories { get; set; }
    public HashSet<string> IgnoredPlugins { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> IncludedPlugins { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> AdditionalExclusions { get; set; } = [];
}
