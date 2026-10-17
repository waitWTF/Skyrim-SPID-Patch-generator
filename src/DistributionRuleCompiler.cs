using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace WearableDistributor;

internal static class DistributionRuleCompiler
{
    private const int MaximumAudiences = 512;
    private const int MaximumTargets = 100_000;
    private const int MaximumGroups = 64;
    private const int MaximumRuleLength = 4_000_000;
    private static readonly HashSet<string> SupportedKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Keyword", "Faction", "Class", "Race", "Npc", "CombatStyle", "Outfit", "VoiceType", "Spell", "Perk", "FormList", "Location", "Skin"
    };

    internal static void Validate(PluginPatchOptions configuration)
    {
        if (configuration.TargetRules.Count > MaximumAudiences)
            throw new InvalidOperationException($"An outfit can have at most {MaximumAudiences} audiences. Narrow the selection before generating.");
        if (configuration.TargetRules.Sum(rule => (long)rule.Filters.Count) > MaximumTargets ||
            configuration.TargetRules.Sum(rule => (long)rule.ResolvedNpcFormIds.Count) > MaximumTargets * 4L)
            throw new InvalidOperationException("The combined audiences contain too many target records. Narrow their selection before generating.");
        var anyResolved = configuration.TargetRules.Any(rule => rule.NpcResolutionComplete);
        if (anyResolved && configuration.TargetRules.Any(rule => !rule.NpcResolutionComplete))
            throw new InvalidOperationException("The NPC preview is incomplete. Rescan and review every selected audience before generating.");
        foreach (var rule in configuration.TargetRules)
        {
            if (rule.Filters.Count > MaximumTargets || rule.Filters.GroupBy(target => target.Category, StringComparer.OrdinalIgnoreCase).Count() > MaximumGroups)
                throw new InvalidOperationException("This audience contains too many target selections. Narrow its groups before generating.");
            if (rule.NpcResolutionComplete && rule.ResolvedNpcFormIds.Count == 0)
                throw new InvalidOperationException($"'{rule.Group}' matches no eligible NPCs. Choose another audience or rescan your active mod list.");
            if (rule.ResolvedNpcFormIds.Count > MaximumTargets)
                throw new InvalidOperationException("This audience matches too many NPC records. Narrow the audience before generating.");
            foreach (var target in rule.Filters)
            {
                if (!SupportedKinds.Contains(target.TargetKind))
                    throw new InvalidOperationException($"The target type '{target.TargetKind}' is not supported. Rescan and choose a supported NPC target.");
                ValidatePluginName(target.Plugin);
                if (target.LocalId is 0 or > 0xFFFFFF) throw new InvalidOperationException("An NPC filter contains an invalid local FormID. Rescan its source plugin.");
                if (target.TargetKind.Equals("Keyword", StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(target.EditorId) || target.EditorId.IndexOfAny(['|', '~', ',', '+', '*', '-', '\r', '\n']) >= 0))
                    throw new InvalidOperationException($"Keyword '{target.EditorId}' cannot safely be represented as a SPID keyword filter.");
            }
            foreach (var id in rule.ResolvedNpcFormIds) ParseSpidForm(id);
        }
        if (configuration.TargetRules.SelectMany(rule => rule.ResolvedNpcFormIds).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaximumTargets + 1).Count() > MaximumTargets)
            throw new InvalidOperationException("The combined audiences exceed the NPC limit. Narrow the audience before generating.");
    }

    internal static List<string> Compile(PluginPatchOptions configuration, uint outfitId, bool skyPatcher)
    {
        var lines = new List<string>();
        var resolved = configuration.TargetRules.All(rule => rule.NpcResolutionComplete);
        if (skyPatcher && !resolved)
            throw new InvalidOperationException("SkyPatcher requires a resolved NPC preview to preserve your group, role, race and sex choices. Rescan and review the audience before generating.");
        // NPC form filters can also match template descendants. Enforce adult
        // status again at runtime, even when the scanned roster excluded children.
        var trait = configuration.Gender switch { "Female" => "F/-C", "Male" => "M/-C", _ => "-C" };
        if (resolved)
        {
            var targets = configuration.TargetRules.SelectMany(rule => rule.ResolvedNpcFormIds).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(ParseSpidForm).OrderBy(form => form.Plugin, StringComparer.OrdinalIgnoreCase).ThenBy(form => form.Id).ToList();
            if (targets.Count == 0) throw new InvalidOperationException("The selected audiences match no eligible NPCs.");
            if (skyPatcher)
            {
                // The explicit-NPC path patches the listed base records directly.
                // Sex and other required conditions were resolved by the scanner.
                foreach (var chunk in targets.Chunk(256))
                    lines.Add($"filterByNpcs={string.Join(',', chunk.Select(form => SkyForm(form.Id, form.Plugin)))}:outfitDefault={SkyForm(outfitId, PatchGenerator.OutputPluginName)}");
            }
            else
            {
                // Union audiences BEFORE rolling chance, so matching two factions
                // does not turn a configured 25% chance into two independent rolls.
                lines.Add($"Outfit = {SpidForm(outfitId, PatchGenerator.OutputPluginName)}|NONE|{string.Join(',', targets.Select(form => SpidForm(form.Id, form.Plugin)))}|NONE|{trait}|NONE|{configuration.Chance.Trim().TrimEnd('%')}");
            }
        }
        else
        {
            // Legacy/API configurations may have no roster. Dynamic SPID helper
            // keywords implement OR within categories, AND between categories,
            // and OR between audiences without a Cartesian product of chance rolls.
            var audienceKey = "WD_Audience_" + Hash(PatchGenerator.StableEditorIdFor(configuration.Source.Plugin, configuration.OutfitName));
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var audience in configuration.TargetRules)
            {
                var groupKeys = new List<string>();
                foreach (var group in audience.Filters.GroupBy(target => target.Category, StringComparer.OrdinalIgnoreCase))
                {
                    var choices = group.DistinctBy(target => target.FormId, StringComparer.OrdinalIgnoreCase).ToList();
                    var groupKey = "WD_Target_" + Hash(audienceKey + "|" + group.Key + "|" + string.Join(';', choices.Select(target => target.FormId).OrderBy(value => value, StringComparer.OrdinalIgnoreCase)));
                    groupKeys.Add(groupKey);
                    var keywordChoices = choices.Where(target => target.TargetKind.Equals("Keyword", StringComparison.OrdinalIgnoreCase)).Select(target => target.EditorId).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
                    var formChoices = choices.Where(target => !target.TargetKind.Equals("Keyword", StringComparison.OrdinalIgnoreCase)).Select(target => target.FormId).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
                    if (keywordChoices.Count > 0) AddUnique($"Keyword = {groupKey}|{string.Join(',', keywordChoices)}", emitted, lines);
                    if (formChoices.Count > 0) AddUnique($"Keyword = {groupKey}|NONE|{string.Join(',', formChoices)}", emitted, lines);
                }
                AddUnique($"Keyword = {audienceKey}|{string.Join('+', groupKeys)}", emitted, lines);
            }
            lines.Add($"Outfit = {SpidForm(outfitId, PatchGenerator.OutputPluginName)}|{audienceKey}|NONE|NONE|{trait}|NONE|{configuration.Chance.Trim().TrimEnd('%')}");
        }
        if (lines.Count > 40_000 || lines.Any(line => line.Length > MaximumRuleLength))
            throw new InvalidOperationException("The generated audience rules are too large. Narrow the selected NPC groups before generating.");
        return lines;
    }

    internal static IEnumerable<string> RequiredTargetPlugins(PluginPatchOptions configuration) => configuration.TargetRules
        .SelectMany(rule => rule.NpcResolutionComplete ? rule.ResolvedNpcFormIds.Select(id => ParseSpidForm(id).Plugin) : rule.Filters.Select(target => target.Plugin));

    internal static (uint Id, string Plugin) ParseSpidForm(string value)
    {
        var delimiter = value.IndexOf('~');
        if (delimiter < 3 || !value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Invalid SPID form identifier: {value}");
        var plugin = value[(delimiter + 1)..];
        ValidatePluginName(plugin);
        if (!uint.TryParse(value.AsSpan(2, delimiter - 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id) || id is 0 or > 0xFFFFFF)
            throw new InvalidDataException($"Invalid local FormID: {value}");
        return (id, plugin);
    }

    internal static (uint Id, string Plugin) ParseSkyPatcherForm(string value)
    {
        var delimiter = value.IndexOf('|');
        if (delimiter <= 0) throw new InvalidDataException($"Invalid SkyPatcher form identifier '{value}'. Expected Plugin.esp|hexID. Repatch outfits generated by earlier versions.");
        var plugin = value[..delimiter];
        ValidatePluginName(plugin);
        if (!uint.TryParse(value.AsSpan(delimiter + 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id) || id is 0 or > 0xFFFFFF)
            throw new InvalidDataException($"Invalid SkyPatcher local FormID: {value}");
        return (id, plugin);
    }

    internal static void ValidatePluginName(string plugin)
    {
        if (string.IsNullOrWhiteSpace(plugin) || Path.GetFileName(plugin) != plugin ||
            plugin.IndexOfAny(['|', '~', ':', ',', '+', '\r', '\n', '/', '\\']) >= 0 ||
            !(plugin.EndsWith(".esp", StringComparison.OrdinalIgnoreCase) || plugin.EndsWith(".esm", StringComparison.OrdinalIgnoreCase) || plugin.EndsWith(".esl", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"Invalid plugin filename in generated output: '{plugin}'.");
    }

    private static string SpidForm(uint id, string plugin) => $"0x{id:X6}~{plugin}";
    private static string SkyForm(uint id, string plugin) => $"{plugin}|{id:X6}";
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToUpperInvariant())))[..24];
    private static void AddUnique(string line, HashSet<string> emitted, List<string> lines) { if (emitted.Add(line)) lines.Add(line); }
}
