using System.IO;
using System.Text.RegularExpressions;

namespace WearableDistributor;

public static class WearableFilterService
{
    public static string GetFilterReason(string path, IEnumerable<string> additional, List<(string Path, string Text)> distributions)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.Equals("Skyrim.esm", StringComparison.OrdinalIgnoreCase)) return "Base game";
        if (fileName.Equals("Update.esm", StringComparison.OrdinalIgnoreCase)) return "Official update";
        if (NpcTargetingService.IsOfficialPlugin(fileName)) return "DLC";
        if (Regex.IsMatch(fileName, @"^cc[a-z0-9]+[.]es[mlp]$", RegexOptions.IgnoreCase)) return "Creation Club";
        if (fileName.Equals("zzLSAR.esp", StringComparison.OrdinalIgnoreCase) || fileName.Equals("SiB.esp", StringComparison.OrdinalIgnoreCase)) return "Vanilla replacer";
        if (new[] { "3BBB", "3BA", "CBBE", "BHUNP", "HIMBO", "TheNewGentleman" }.Any(name => Path.GetFileNameWithoutExtension(path).Equals(name, StringComparison.OrdinalIgnoreCase))) return "Body mod";
        var plugin = Path.GetFileNameWithoutExtension(path);
        var haystack = plugin;
        var exclusions = new (string Term, string Reason)[]
        {
            ("legacy of the dragonborn", "LOTD content"), ("lotd", "LOTD content"), ("simply bikini", "SiB replacer family"), ("lsar", "LSAR replacer family"), ("alsar", "Alsar replacer family"), ("replacer", "Replacer"),
            ("hair", "Hair content"), ("wig", "Hair content"), ("tattoo", "Tattoo content"), ("warpaint", "Paint/skin content"), ("body paint", "Paint/skin content"), ("skin texture", "Paint/skin content"),
            ("3bbb body", "Body mod"), ("body replacer", "Body mod"), ("body mod", "Body mod"),
            ("apocalypse", "Magic mod"), ("magic of skyrim", "Magic mod"), ("spell pack", "Magic mod"),
            ("follower", "Follower/NPC mod"), ("horse", "Creature/gameplay mod"), ("quest", "Quest/story mod"), ("dialogue", "Dialogue mod"), ("framework", "Framework"), ("creature", "Creature mod"), ("beast", "Creature mod"), ("mule", "Creature mod"), ("sound", "Sound mod"), ("location", "Location mod"), ("tower", "Location mod"), ("dungeon", "Location mod"), ("caravan", "Creature/gameplay mod"), ("weapon expansion", "Weapon mod"), ("npc overhaul", "NPC overhaul"), ("npc assets", "NPC assets"), ("race", "Race mod"),
            ("cutting room floor", "Quest/world edit"), ("forgotten city", "Quest/story mod"), ("clockwork", "Quest/story mod"), ("wyrmstooth", "Quest/story mod"), ("convenient horses", "Horse gameplay mod"), ("flower girls", "Framework/quest mod"), ("flowergirls", "Framework/quest mod"), ("fertility mode", "Gameplay framework"), ("headhunter", "Gameplay mod"), ("handheld lantern", "Utility item mod"), ("lanterns", "Utility item mod"), ("jk's palace", "Location edit"), ("levelers tower", "Location mod"), ("realm of lorkhan", "Quest/world mod"), ("relationship dialogue overhaul", "Dialogue mod"), ("serana dialog", "Dialogue mod"), ("sexlab", "Framework"), ("ostim", "Framework"), ("obody", "Body/NPC framework"), ("ocum", "Framework"), ("horny", "NPC/quest mod"), ("triumvirate", "Magic mod"), ("draugr upgrades", "Creature overhaul"), ("tkaa", "NPC overhaul"), ("pan_aio", "NPC overhaul"), ("pan_wicked", "NPC overhaul"), ("dressed up lovers", "NPC/quest mod"), ("dress up lovers", "NPC/quest mod"), ("praedys horns", "Accessory mod"), ("cuyiantlers", "Accessory mod"), ("astarothhorns", "Accessory mod"), ("horns", "Accessory mod"), ("antlers", "Accessory mod"), ("sabretooth", "Creature mod"), ("halfdragonrace", "Race mod"), ("dbm", "LOTD support plugin"), ("underwear", "Underwear content"), ("hdt smp object", "Physics/framework plugin"), ("daemon girl", "Follower/NPC mod"), ("claudette", "Follower/NPC mod"), ("garm_ghost", "Creature mod"), ("wenches", "NPC/quest mod"), ("penitus", "NPC/quest mod"), ("ksws03", "Quest mod"), ("qwibnewlanterns", "Utility item mod"), ("pitfighter", "Quest mod"), ("noblehouse", "Quest/building mod"), ("miri_follower", "Follower mod"), ("nwsfollowerframework", "Follower framework"), ("immersive sounds", "Sound mod"), ("khajiit caravan mules", "Creature mod"), ("nyrfollowerassets", "Follower assets"), ("trustyhpfollowerpack", "Follower mod"), ("ryder dog", "Follower/creature mod"), ("newgentleman", "Body mod")
        };
        foreach (var exclusion in exclusions)
            if (MatchesPluginTerm(haystack, exclusion.Term)) return exclusion.Reason;
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
        var slots = item.Slots;
        var core = new[] { "Body", "Head", "Hands", "Feet", "Forearms", "Calves", "Shield", "Tail" };
        return !core.Any(slot => slots.Contains(slot, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsDistributionIni(string path) =>
        Path.GetFileName(path).Contains("_DISTR", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("SkyPatcher", StringComparison.OrdinalIgnoreCase);

    private static bool HasExistingDistribution(string pluginPath, List<(string Path, string Text)> distributionFiles)
    {
        var plugin = Path.GetFileName(pluginPath);
        var pluginName = Path.GetFileNameWithoutExtension(pluginPath);
        var parent = Path.GetFileName(Path.GetDirectoryName(pluginPath));
        return distributionFiles.Where(file => !Path.GetFileName(file.Path).Equals(PatchGenerator.OutputIniName, StringComparison.OrdinalIgnoreCase) && !file.Path.Contains(Path.Combine("WearableDistributor", "WearableDistributor.ini"), StringComparison.OrdinalIgnoreCase)).Any(file => file.Text.Contains(plugin, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileNameWithoutExtension(file.Path).Contains(pluginName, StringComparison.OrdinalIgnoreCase) ||
            (parent?.Contains(plugin, StringComparison.OrdinalIgnoreCase) == true && Path.GetDirectoryName(file.Path) == Path.GetDirectoryName(pluginPath)));
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
