using System.IO;
using System.Text.RegularExpressions;

namespace WearableDistributor;

public static class WearableFilterService
{
    public const int Revision = 2;
    public static bool IsOfficialContent(string plugin) => NpcTargetingService.IsOfficialPlugin(Path.GetFileName(plugin)) ||
        Path.GetFileName(plugin).Equals("_ResourcePack.esl", StringComparison.OrdinalIgnoreCase) ||
        Regex.IsMatch(Path.GetFileName(plugin), @"^cc[a-z0-9_-]+[.]es[mlp]$", RegexOptions.IgnoreCase);

    public static string GetLibraryReason(string path, IEnumerable<string> additional, IReadOnlyList<WearableContentRecord> content, IEnumerable<string> equipmentIds)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        foreach (var term in additional.Where(term => !string.IsNullOrWhiteSpace(term)))
            if (name.Contains(term, StringComparison.OrdinalIgnoreCase)) return "Custom exclusion: " + term;
        if (new[] { "zzLSAR", "SiB", "TheNewGentleman", "CBBE", "3BBB", "3BA", "BHUNP", "HIMBO" }.Contains(name, StringComparer.OrdinalIgnoreCase)) return "Body framework / vanilla replacer";
        return WearableContentClassifier.Classify(content, equipmentIds);
    }
    public static string GetFilterReason(string path, IEnumerable<string> additional, List<(string Path, string Text)> distributions)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.Equals("Skyrim.esm", StringComparison.OrdinalIgnoreCase)) return "Base game";
        if (fileName.Equals("Update.esm", StringComparison.OrdinalIgnoreCase)) return "Official update";
        if (NpcTargetingService.IsOfficialPlugin(fileName)) return "DLC";
        if (Regex.IsMatch(fileName, @"^cc[a-z]+[0-9]+[-_a-z0-9]*[.]es[mlp]$", RegexOptions.IgnoreCase)) return "Creation Club";
        if (fileName.Equals("zzLSAR.esp", StringComparison.OrdinalIgnoreCase) || fileName.Equals("SiB.esp", StringComparison.OrdinalIgnoreCase)) return "Vanilla replacer";
        if (new[] { "3BBB", "3BA", "CBBE", "BHUNP", "HIMBO", "TheNewGentleman" }.Any(name => Path.GetFileNameWithoutExtension(path).Equals(name, StringComparison.OrdinalIgnoreCase))) return "Body mod";
        var plugin = Path.GetFileNameWithoutExtension(path);
        if (Regex.IsMatch(plugin, @"^(?:BHUNP|CBBE|3BBB|3BA|HIMBO)(?:[\s_.-]*(?:3BBB|3BA|Body|SMP|Physics|Resources|Advanced|SE|[0-9]))*$", RegexOptions.IgnoreCase)) return "Body framework";
        var haystack = plugin;
        var exclusions = new (string Term, string Reason)[]
        {
            ("ordinator", "Perk overhaul"), ("vokrii", "Perk overhaul"), ("adamant", "Perk overhaul"),
            ("sperg", "Perk overhaul"), ("perkus maximus", "Perk overhaul"), ("perk overhaul", "Perk overhaul"),
            ("odin", "Magic overhaul"), ("mysticism", "Magic overhaul"), ("arcanum", "Magic overhaul"),
            ("summermyst", "Enchantment overhaul"), ("wintermyst", "Enchantment overhaul"),
            ("ussep", "Game fixes"), ("unofficial skyrim special edition patch", "Game fixes"),
            ("obis se", "NPC overhaul (OBIS bandits, not Obi's outfit mods)"),
            ("legacy of the dragonborn", "LOTD content"), ("lotd", "LOTD content"), ("simply bikini", "SiB replacer family"), ("lsar", "LSAR replacer family"), ("alsar", "Alsar replacer family"), ("replacer", "Replacer"),
            ("hair", "Hair content"), ("wig", "Hair content"), ("tattoo", "Tattoo content"), ("warpaint", "Paint/skin content"), ("body paint", "Paint/skin content"), ("skin texture", "Paint/skin content"),
            ("3bbb body", "Body mod"), ("body replacer", "Body mod"), ("body mod", "Body mod"),
            ("apocalypse", "Magic mod"), ("magic of skyrim", "Magic mod"), ("spell pack", "Magic mod"),
            ("follower", "Follower/NPC mod"), ("horse", "Creature/gameplay mod"), ("quest", "Quest/story mod"), ("dialogue", "Dialogue mod"), ("framework", "Framework"), ("creature", "Creature mod"), ("beast", "Creature mod"), ("mule", "Creature mod"), ("sound", "Sound mod"), ("location", "Location mod"), ("tower", "Location mod"), ("dungeon", "Location mod"), ("caravan", "Creature/gameplay mod"), ("weapon expansion", "Weapon mod"), ("npc overhaul", "NPC overhaul"), ("npc assets", "NPC assets"), ("race", "Race mod"),
            ("cutting room floor", "Quest/world edit"), ("forgotten city", "Quest/story mod"), ("clockwork", "Quest/story mod"), ("wyrmstooth", "Quest/story mod"), ("convenient horses", "Horse gameplay mod"), ("flower girls", "Framework/quest mod"), ("flowergirls", "Framework/quest mod"), ("fertility mode", "Gameplay framework"), ("headhunter", "Gameplay mod"), ("handheld lantern", "Utility item mod"), ("lanterns", "Utility item mod"), ("jk's palace", "Location edit"), ("levelers tower", "Location mod"), ("realm of lorkhan", "Quest/world mod"), ("relationship dialogue overhaul", "Dialogue mod"), ("serana dialog", "Dialogue mod"), ("sexlab", "Framework"), ("ostim", "Framework"), ("obody", "Body/NPC framework"), ("ocum", "Framework"), ("horny", "NPC/quest mod"), ("triumvirate", "Magic mod"), ("draugr upgrades", "Creature overhaul"), ("tkaa", "NPC overhaul"), ("pan_aio", "NPC overhaul"), ("pan_wicked", "NPC overhaul"), ("dressed up lovers", "NPC/quest mod"), ("dress up lovers", "NPC/quest mod"), ("praedys horns", "Accessory mod"), ("cuyiantlers", "Accessory mod"), ("astarothhorns", "Accessory mod"), ("horns", "Accessory mod"), ("antlers", "Accessory mod"), ("sabretooth", "Creature mod"), ("halfdragonrace", "Race mod"), ("dbm", "LOTD support plugin"), ("underwear", "Underwear content"), ("hdt smp object", "Physics/framework plugin"), ("daemon girl", "Follower/NPC mod"), ("claudette", "Follower/NPC mod"), ("garm_ghost", "Creature mod"), ("wenches", "NPC/quest mod"), ("penitus", "NPC/quest mod"), ("ksws03", "Quest mod"), ("qwibnewlanterns", "Utility item mod"), ("pitfighter", "Quest mod"), ("noblehouse", "Quest/building mod"), ("miri_follower", "Follower mod"), ("nwsfollowerframework", "Follower framework"), ("immersive sounds", "Sound mod"), ("khajiit caravan mules", "Creature mod"), ("nyrfollowerassets", "Follower assets"), ("trustyhpfollowerpack", "Follower mod"), ("ryder dog", "Follower/creature mod"), ("newgentleman", "Body mod")
        };
        foreach (var exclusion in exclusions)
            if (MatchesPluginTerm(haystack, exclusion.Term) && !(HasOutfitTitle(plugin) && new[]
                { "ordinator", "vokrii", "adamant", "sperg", "odin", "mysticism", "arcanum", "summermyst", "wintermyst", "hair", "wig", "horse", "quest", "beast", "tower", "dungeon", "location", "race", "horns", "antlers" }
                .Contains(exclusion.Term, StringComparer.OrdinalIgnoreCase))) return exclusion.Reason;
        foreach (var term in additional)
            if (haystack.Contains(term, StringComparison.OrdinalIgnoreCase)) return $"Custom exclusion: {term}";
        if (HasExistingDistribution(path, distributions)) return "Existing SPID/SkyPatcher distribution detected";
        return "";
    }

    private static string AppendReason(string existing, string addition) => string.IsNullOrWhiteSpace(existing) ? addition : $"{existing}; {addition}";

    private static string KeywordCategory(string? editorId)
    {
        if (string.IsNullOrWhiteSpace(editorId)) return "Mod keyword";
        if (editorId.StartsWith("Job", StringComparison.OrdinalIgnoreCase)) return "Occupation";
        if (editorId.StartsWith("ActorType", StringComparison.OrdinalIgnoreCase)) return "NPC type";
        if (editorId.StartsWith("LocType", StringComparison.OrdinalIgnoreCase)) return "Location type";
        if (editorId.StartsWith("Vendor", StringComparison.OrdinalIgnoreCase)) return "Vendor";
        if (editorId.StartsWith("Armor", StringComparison.OrdinalIgnoreCase)) return "Armor";
        if (editorId.StartsWith("WeapType", StringComparison.OrdinalIgnoreCase)) return "Weapon";
        return "Mod keyword";
    }

    public static bool IsExcludedRecord(WearableRecord item)
    {
        var name = $"{item.Name} {item.EditorId}";
        if (string.IsNullOrWhiteSpace(item.Slots) || item.Slots.Equals("None", StringComparison.OrdinalIgnoreCase) || item.Slots == "0") return true;
        var tokens = new[] { "hair", "wig", "tattoo", "warpaint", "face paint", "body paint", "skin texture", "body replacer" };
        return tokens.Any(term => MatchesPluginTerm(name, term));
    }

    public static bool IsAccessoryOnly(WearableRecord item)
    {
        var slots = OutfitPartSelection.BodySlots(item.Slots);
        return !new[] { 30, 32, 33, 34, 37, 38, 39, 40 }.Any(slot => slots.Contains("Slot " + slot));
    }

    public static string GetContentReason(string plugin, int pieces, int npcs, int spells, int perks, int worldspaces)
    {
        if (perks >= 10 && perks > pieces) return "Primarily perk/gameplay content";
        if (spells >= 8 && spells > pieces * 2) return "Primarily magic/gameplay content";
        if (worldspaces > 0 && npcs > 0) return "World/quest expansion with incidental wearables";
        if (npcs > 0 && !HasOutfitTitle(plugin) && (pieces < 12 || pieces < npcs * 4)) return "NPC/follower content with incidental wearables";
        return "";
    }

    private static bool HasOutfitTitle(string plugin) => Regex.IsMatch(NpcTargetingService.Humanize(plugin),
        @"\b(?:armor|armour|outfit|clothes|clothing|dress|tunic|robe|robes|attire|garb|equipment|cuirass|bodysuit)\b", RegexOptions.IgnoreCase);

    public static bool IsDistributionIni(string path) =>
        Path.GetFileName(path).Contains("_DISTR", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("SkyPatcher", StringComparison.OrdinalIgnoreCase);

    private static bool HasExistingDistribution(string pluginPath, List<(string Path, string Text)> distributionFiles)
    {
        var plugin = Path.GetFileName(pluginPath);
        return distributionFiles.Where(file => !IsOwnDistribution(file.Path)).Any(file => SuppliedForms(file.Text)
            .Any(form => form.Split('~', 2)[1].Equals(plugin, StringComparison.OrdinalIgnoreCase)));
    }

    public static bool IsOwnDistribution(string path) => DetectedPatchReader.IsGeneratedFile(path) ||
        path.Replace('/', '\\').Contains(@"\WearableDistributor\", StringComparison.OrdinalIgnoreCase);

    public static IEnumerable<string> SuppliedEditorIds(string text)
    {
        foreach (var raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(raw.Split(';', 2)[0].Trim(), @"^(?:Outfit|Item)\s*=\s*([a-z_][a-z0-9_]*)\s*(?:\||$)", RegexOptions.IgnoreCase);
            if (match.Success && !match.Groups[1].Value.Equals("NONE", StringComparison.OrdinalIgnoreCase)) yield return match.Groups[1].Value;
        }
    }

    // Read only supplied item/outfit operands. A plugin in an NPC filter or comment does not prove existing outfit distribution.
    public static IEnumerable<string> SuppliedForms(string text)
    {
        foreach (var raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Split(';', 2)[0].Trim();
            var spid = Regex.Match(line, @"^(?:Outfit|Item)\s*=\s*(0x[0-9a-f]+)~([^|]+)", RegexOptions.IgnoreCase);
            if (spid.Success) yield return Normalize(spid.Groups[1].Value, spid.Groups[2].Value.Trim());
            // SkyPatcher uses colons between operations and plugin|localID for record operands.
            foreach (Match match in Regex.Matches(line, @"(?:^|:)\s*(?:outfitDefault|outfitSleep|addToOutfits|addToLLs|addToContainers)\s*=\s*([^|:]+[.]es[mpl])\|(?:0x)?([0-9a-f]+)(?:[~,:]|$)", RegexOptions.IgnoreCase))
                yield return Normalize("0x" + match.Groups[2].Value, match.Groups[1].Value.Trim());
        }
        static string Normalize(string hex, string plugin) => uint.TryParse(hex[2..], System.Globalization.NumberStyles.HexNumber, null, out var id) ? $"0x{id:X6}~{plugin}" : hex + "~" + plugin;
    }


    private static bool MatchesPluginTerm(string text, string term)
    {
        if (term.Equals("dbm", StringComparison.OrdinalIgnoreCase)) return text.StartsWith("DBM", StringComparison.OrdinalIgnoreCase);
        if (term.StartsWith("pan_", StringComparison.OrdinalIgnoreCase)) return text.StartsWith(term, StringComparison.OrdinalIgnoreCase);
        var humanized = Regex.Replace(text, "([a-z0-9])([A-Z])", "$1 $2").Replace('_', ' ');
        return Regex.IsMatch(humanized, @"(?<![a-z0-9])" + Regex.Escape(term).Replace(@"\ ", @"[\s_-]+") + @"(?![a-z0-9])", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(text, @"(?<![a-z0-9])" + Regex.Escape(term) + @"(?![a-z0-9])", RegexOptions.IgnoreCase);
    }
}
