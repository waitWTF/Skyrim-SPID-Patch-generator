using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;

namespace WearableDistributor;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<PluginEntry> visiblePlugins = [];
    private readonly List<PluginEntry> scannedPlugins = [];
    private readonly List<KeywordChoice> availableKeywords = [];
    private readonly string preferencesFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WearableDistributor", "settings.json");
    private AppPreferences preferences = new();
    private string dataPath = "";

    public MainWindow()
    {
        InitializeComponent();
        PluginsGrid.ItemsSource = visiblePlugins;
        LoadPreferences();
        DataPathBox.Text = preferences.DataPath;
        DefaultFrameworkBox.SelectedIndex = preferences.Framework == "SkyPatcher" ? 1 : 0;
        DefaultChanceBox.SelectedIndex = ChanceIndex(preferences.DefaultChance);
        IncludeAccessoriesBox.IsChecked = preferences.IncludeAccessories;
        ExclusionsBox.Text = string.Join(Environment.NewLine, preferences.AdditionalExclusions);
        dataPath = preferences.DataPath;
        if (Directory.Exists(dataPath)) ScanFolder();
        else if (TryFindDataFolder(out var discovered))
        {
            dataPath = discovered;
            DataPathBox.Text = discovered;
            preferences.DataPath = discovered;
            SavePreferences();
            ScanFolder();
        }
    }

    private void LoadPreferences()
    {
        try
        {
            if (File.Exists(preferencesFile))
                preferences = JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(preferencesFile)) ?? new AppPreferences();
            preferences.IgnoredPlugins = new HashSet<string>(preferences.IgnoredPlugins ?? [], StringComparer.OrdinalIgnoreCase);
            preferences.IncludedPlugins = new HashSet<string>(preferences.IncludedPlugins ?? [], StringComparer.OrdinalIgnoreCase);
            preferences.DistributionSettings = new Dictionary<string, SavedDistributionOptions>(preferences.DistributionSettings ?? new(), StringComparer.OrdinalIgnoreCase);
        }
        catch { preferences = new AppPreferences(); }
    }

    private void SavePreferences()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(preferencesFile)!);
        File.WriteAllText(preferencesFile, JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static bool TryFindDataFolder(out string path)
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common", "Skyrim Special Edition", "Data"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steamapps", "common", "Skyrim Special Edition", "Data"),
            @"C:\GOG Games\Skyrim Special Edition\Data"
        };
        path = roots.FirstOrDefault(Directory.Exists) ?? "";
        return !string.IsNullOrEmpty(path);
    }

    private void BrowseData_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose Skyrim Data or Vortex staging folder" };
        if (dialog.ShowDialog() != true) return;
        DataPathBox.Text = dialog.FolderName;
    }

    private void SavePreferences_Click(object sender, RoutedEventArgs e)
    {
        preferences.DataPath = DataPathBox.Text.Trim();
        dataPath = preferences.DataPath;
        preferences.Framework = DefaultFrameworkBox.SelectedIndex == 1 ? "SkyPatcher" : "SPID";
        preferences.DefaultChance = ChanceFromIndex(DefaultChanceBox.SelectedIndex);
        preferences.IncludeAccessories = IncludeAccessoriesBox.IsChecked == true;
        preferences.AdditionalExclusions = ExclusionsBox.Text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        SavePreferences();
        StatusText.Text = "Preferences saved.";
        if (Directory.Exists(dataPath)) ScanFolder();
    }

    private void Scan_Click(object sender, RoutedEventArgs e)
    {
        dataPath = DataPathBox.Text.Trim();
        if (!Directory.Exists(dataPath))
        {
            MainTabs.SelectedIndex = 1;
            StatusText.Text = "Choose a valid Skyrim Data folder in Preferences first.";
            return;
        }
        preferences.DataPath = dataPath;
        SavePreferences();
        ScanFolder();
    }

    private async void ScanFolder()
    {
        scannedPlugins.Clear();
        availableKeywords.Clear();
        visiblePlugins.Clear();
        var files = EnumeratePluginFiles(dataPath).ToList();
        var usedTargetForms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targetRecordCandidates = new List<KeywordChoice>();
        ActionProgress.Visibility = Visibility.Visible;
        ActionProgress.IsIndeterminate = false;
        ActionProgress.Value = 0;
        var distributionFiles = SafeEnumerateFiles(dataPath, "*.ini")
            .Where(IsDistributionIni)
            .Select(path => (Path: path, Text: SafeReadAllText(path)))
            .ToList();
        var errors = new List<string>();
        var completed = 0;
        foreach (var path in files)
        {
            try
            {
                using var mod = SkyrimMod.CreateFromBinaryOverlay(new Noggog.FilePath(path), SkyrimRelease.SkyrimSE);
                foreach (var npc in mod.Npcs)
                {
                    foreach (var faction in npc.Factions ?? [])
                        if (!faction.Faction.IsNull) usedTargetForms.Add($"0x{faction.Faction.FormKey.ID:X6}~{faction.Faction.FormKey.ModKey.FileName}");
                    if (npc.CrimeFaction is { IsNull: false } crimeFaction) usedTargetForms.Add($"0x{crimeFaction.FormKey.ID:X6}~{crimeFaction.FormKey.ModKey.FileName}");
                    if (!npc.Class.IsNull) usedTargetForms.Add($"0x{npc.Class.FormKey.ID:X6}~{npc.Class.FormKey.ModKey.FileName}");
                    if (!npc.Race.IsNull) usedTargetForms.Add($"0x{npc.Race.FormKey.ID:X6}~{npc.Race.FormKey.ModKey.FileName}");
                    foreach (var keyword in npc.Keywords ?? [])
                        if (!keyword.IsNull) usedTargetForms.Add($"0x{keyword.FormKey.ID:X6}~{keyword.FormKey.ModKey.FileName}");
                }
                foreach (var keyword in mod.Keywords)
                    RememberTargetCandidate(targetRecordCandidates, keyword.EditorID, keyword.FormKey.ModKey.FileName, keyword.FormKey.ID, "Keyword", KeywordCategory(keyword.EditorID));
                foreach (var faction in mod.Factions)
                    RememberTargetCandidate(targetRecordCandidates, faction.EditorID, faction.FormKey.ModKey.FileName, faction.FormKey.ID, "Faction", "Faction");
                foreach (var actorClass in mod.Classes)
                    RememberTargetCandidate(targetRecordCandidates, actorClass.EditorID, actorClass.FormKey.ModKey.FileName, actorClass.FormKey.ID, "Class", "Occupation / class");
                foreach (var race in mod.Races)
                    RememberTargetCandidate(targetRecordCandidates, race.EditorID, race.FormKey.ModKey.FileName, race.FormKey.ID, "Race", "Race");
                var pieces = mod.Armors.Select(armor =>
                {
                    var recordName = armor.Name?.String ?? armor.EditorID ?? "Unnamed armor";
                    return new WearableRecord
                    {
                        Plugin = armor.FormKey.ModKey.FileName,
                        LocalId = armor.FormKey.ID,
                        EditorId = armor.EditorID ?? "",
                        Name = recordName,
                        Slots = armor.BodyTemplate?.FirstPersonFlags.ToString() ?? "",
                        SourcePath = path
                    };
                }).Where(item => !IsExcludedRecord(item)).ToList();
                if (pieces.Count > 0)
                {
                    var pluginName = Path.GetFileName(path);
                    var reason = GetFilterReason(path, preferences.AdditionalExclusions, distributionFiles);
                    if (!preferences.IncludeAccessories && pieces.All(IsAccessoryOnly)) reason = AppendReason(reason, "Accessory-only items (enable in Preferences to scan these)");
                    var entry = new PluginEntry { Plugin = pluginName, SourcePath = path, Pieces = pieces, FilterReason = reason };
                    entry.Selected = (string.IsNullOrEmpty(reason) || preferences.IncludedPlugins.Contains(pluginName)) && !preferences.IgnoredPlugins.Contains(pluginName);
                    scannedPlugins.Add(entry);
                }
            }
            catch (Exception ex) { errors.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
            completed++;
            ActionProgress.Value = 100.0 * completed / Math.Max(files.Count, 1);
            StatusText.Text = $"Scanning plugins… {completed}/{files.Count}; {scannedPlugins.Count} wearable plugin candidates, {availableKeywords.Count} NPC keywords found.";
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        }
        ActionProgress.Visibility = Visibility.Collapsed;
        foreach (var candidate in targetRecordCandidates.Where(item => usedTargetForms.Contains(item.FormId)))
            AddTargetChoice(candidate.EditorId, candidate.Plugin, candidate.LocalId, candidate.TargetKind, candidate.Category);
        availableKeywords.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.EditorId, right.EditorId));
        scannedPlugins.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Plugin, right.Plugin));
        RefreshPluginList();
        var officialOnly = scannedPlugins.Count > 0 && scannedPlugins.All(item =>
        {
            var firstReason = item.FilterReason.Split(';', 2)[0].Trim();
            return firstReason is "Base game" or "Official update" or "DLC" or "Creation Club";
        });
        var scanHint = officialOnly
            ? " Only Skyrim/DLC/CC wearable records were found. If your mods are managed by Vortex, scan its staging folder containing the mod subfolders instead of the game's Data folder."
            : "";
        StatusText.Text = $"Found {scannedPlugins.Count} wearable plugin candidates; {scannedPlugins.Count(item => item.IsFiltered)} were held back by smart filters. Read {availableKeywords.Count} Skyrim and mod-added keywords." +
            (errors.Count > 0 ? $" {errors.Count} unreadable; first: {errors[0]}" : "") + scanHint;
    }

    private void AddTargetChoice(string? editorId, string plugin, uint localId, string targetKind, string category)
    {
        if (string.IsNullOrWhiteSpace(editorId)) return;
        var formId = $"0x{localId:X6}~{plugin}";
        if (availableKeywords.Any(item => item.TargetKind == targetKind && item.FormId.Equals(formId, StringComparison.OrdinalIgnoreCase))) return;
        availableKeywords.Add(new KeywordChoice { EditorId = editorId, Plugin = plugin, LocalId = localId, TargetKind = targetKind, Category = category });
    }

    private static void RememberTargetCandidate(List<KeywordChoice> candidates, string? editorId, string plugin, uint localId, string targetKind, string category)
    {
        if (string.IsNullOrWhiteSpace(editorId)) return;
        var choice = new KeywordChoice { EditorId = editorId, Plugin = plugin, LocalId = localId, TargetKind = targetKind, Category = category };
        if (!candidates.Any(item => item.TargetKind == targetKind && item.FormId.Equals(choice.FormId, StringComparison.OrdinalIgnoreCase))) candidates.Add(choice);
    }

    private static string GetFilterReason(string path, IEnumerable<string> additional, List<(string Path, string Text)> distributions)
    {
        var plugin = Path.GetFileNameWithoutExtension(path);
        var haystack = $"{plugin} {Path.GetFileName(Path.GetDirectoryName(path))}";
        var exclusions = new (string Term, string Reason)[]
        {
            ("skyrim", "Base game"), ("update", "Official update"), ("dawnguard", "DLC"), ("hearthfires", "DLC"), ("dragonborn", "DLC"),
            ("ccbg", "Creation Club"), ("cced", "Creation Club"), ("ccff", "Creation Club"), ("cckr", "Creation Club"), ("ccmtys", "Creation Club"), ("ccqdrs", "Creation Club"), ("cceejs", "Creation Club"), ("ccvsvs", "Creation Club"), ("ccasvs", "Creation Club"), ("ccbgss", "Creation Club"), ("ccrmss", "Creation Club"),
            ("legacy of the dragonborn", "LOTD content"), ("lotd", "LOTD content"), ("sib", "SiB replacer family"), ("lsar", "LSAR replacer family"), ("alsar", "Alsar replacer family"), ("replacer", "Replacer"),
            ("hair", "Hair content"), ("wig", "Hair content"), ("tattoo", "Tattoo content"), ("warpaint", "Paint/skin content"), ("body paint", "Paint/skin content"), ("skin texture", "Paint/skin content"),
            ("3bbb", "Body mod"), ("3ba", "Body mod"), ("cbbe", "Body mod"), ("bhunp", "Body mod"), ("himbo", "Body mod"), ("body slide", "Body mod"), ("bodyslide", "Body mod"), ("body mod", "Body mod"),
            ("apocalypse", "Magic mod"), ("magic of skyrim", "Magic mod"), ("magic", "Magic mod"), ("spell", "Magic mod"),
            ("follower", "Follower/NPC mod"), ("horse", "Creature/gameplay mod"), ("quest", "Quest/story mod"), ("dialogue", "Dialogue mod"), ("framework", "Framework"), ("creature", "Creature mod"), ("beast", "Creature mod"), ("mule", "Creature mod"), ("sound", "Sound mod"), ("location", "Location mod"), ("tower", "Location mod"), ("dungeon", "Location mod"), ("caravan", "Creature/gameplay mod"), ("weapon expansion", "Weapon mod"), ("npc overhaul", "NPC overhaul"), ("npc assets", "NPC assets"), ("race", "Race mod"),
            ("cutting room floor", "Quest/world edit"), ("forgotten city", "Quest/story mod"), ("clockwork", "Quest/story mod"), ("wyrmstooth", "Quest/story mod"), ("convenient horses", "Horse gameplay mod"), ("flower girls", "Framework/quest mod"), ("flowergirls", "Framework/quest mod"), ("fertility mode", "Gameplay framework"), ("headhunter", "Gameplay mod"), ("handheld lantern", "Utility item mod"), ("lanterns", "Utility item mod"), ("jk's palace", "Location edit"), ("levelers tower", "Location mod"), ("realm of lorkhan", "Quest/world mod"), ("relationship dialogue overhaul", "Dialogue mod"), ("serana dialog", "Dialogue mod"), ("sexlab", "Framework"), ("ostim", "Framework"), ("obody", "Body/NPC framework"), ("ocum", "Framework"), ("horny", "NPC/quest mod"), ("triumvirate", "Magic mod"), ("draugr upgrades", "Creature overhaul"), ("tkaa", "NPC overhaul"), ("pan_aio", "NPC overhaul"), ("pan_wicked", "NPC overhaul"), ("dressed up lovers", "NPC/quest mod"), ("dress up lovers", "NPC/quest mod"), ("praedys horns", "Accessory mod"), ("cuyiantlers", "Accessory mod"), ("astarothhorns", "Accessory mod"), ("horns", "Accessory mod"), ("antlers", "Accessory mod"), ("sabretooth", "Creature mod"), ("halfdragonrace", "Race mod"), ("dbm", "LOTD support plugin"), ("underwear", "Underwear content"), ("hdt", "Physics/framework plugin"), ("daemon girl", "Follower/NPC mod"), ("claudette", "Follower/NPC mod"), ("garm_ghost", "Creature mod"), ("wenches", "NPC/quest mod"), ("penitus", "NPC/quest mod"), ("ksws03", "Quest mod"), ("qwibnewlanterns", "Utility item mod"), ("pitfighter", "Quest mod"), ("noblehouse", "Quest/building mod"), ("miri_follower", "Follower mod"), ("laci", "Follower mod"), ("nwsfollowerframework", "Follower framework"), ("immersive sounds", "Sound mod"), ("khajiit caravan mules", "Creature mod"), ("nyrfollowerassets", "Follower assets"), ("trustyhpfollowerpack", "Follower mod"), ("ryder dog", "Follower/creature mod"), ("newgentleman", "Body mod")
        };
        foreach (var exclusion in exclusions)
            if (haystack.Contains(exclusion.Term, StringComparison.OrdinalIgnoreCase)) return exclusion.Reason;
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

    private static bool IsExcludedRecord(WearableRecord item)
    {
        var name = $"{item.Name} {item.EditorId}";
        if (string.IsNullOrWhiteSpace(item.Slots)) return true;
        return new[] { "hair", "wig", "tattoo", "warpaint", "face paint", "body paint", "skin texture", "spell", "magic", "body replacer" }
            .Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAccessoryOnly(WearableRecord item)
    {
        var slots = item.Slots;
        var core = new[] { "Body", "Head", "Hands", "Feet", "Forearms", "Calves", "Shield", "Tail" };
        return !core.Any(slot => slots.Contains(slot, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDistributionIni(string path) =>
        Path.GetFileName(path).Contains("_DISTR", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("SkyPatcher", StringComparison.OrdinalIgnoreCase);

    private static bool HasExistingDistribution(string pluginPath, List<(string Path, string Text)> distributionFiles)
    {
        var plugin = Path.GetFileName(pluginPath);
        var pluginName = Path.GetFileNameWithoutExtension(pluginPath);
        var parent = Path.GetFileName(Path.GetDirectoryName(pluginPath));
        return distributionFiles.Any(file => file.Text.Contains(plugin, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileNameWithoutExtension(file.Path).Contains(pluginName, StringComparison.OrdinalIgnoreCase) ||
            (parent?.Contains(plugin, StringComparison.OrdinalIgnoreCase) == true && Path.GetDirectoryName(file.Path) == Path.GetDirectoryName(pluginPath)));
    }

    private static IEnumerable<string> SafeEnumerateFiles(string directory, string pattern)
    {
        try { return Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories).ToList(); }
        catch { return []; }
    }

    private static string SafeReadAllText(string path)
    {
        try { return File.ReadAllText(path); }
        catch { return ""; }
    }

    private static IEnumerable<string> EnumeratePluginFiles(string root)
    {
        if (!Directory.Exists(root)) yield break;
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(directory).ToList(); } catch { continue; }
            foreach (var file in files)
                if (new[] { ".esp", ".esm", ".esl" }.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) yield return file;
            IEnumerable<string> children;
            try { children = Directory.EnumerateDirectories(directory).ToList(); } catch { continue; }
            foreach (var child in children) pending.Push(child);
        }
    }

    private void RefreshPluginList(object? sender = null, RoutedEventArgs? e = null) => RefreshPluginList();

    private void RefreshPluginList()
    {
        var query = SearchBox.Text.Trim();
        var showIgnored = ShowIgnoredBox.IsChecked == true;
        var showFiltered = ShowFilteredBox.IsChecked == true;
        visiblePlugins.Clear();
        foreach (var plugin in scannedPlugins.OrderBy(item => item.Plugin, StringComparer.OrdinalIgnoreCase).Where(item => (showFiltered || !item.IsFiltered || preferences.IncludedPlugins.Contains(item.Plugin)) &&
                     (showIgnored || !preferences.IgnoredPlugins.Contains(item.Plugin)) &&
                     (string.IsNullOrWhiteSpace(query) || item.Plugin.Contains(query, StringComparison.OrdinalIgnoreCase))))
            visiblePlugins.Add(plugin);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshPluginList();

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var plugin in visiblePlugins.Where(item => !preferences.IgnoredPlugins.Contains(item.Plugin))) plugin.Selected = true;
        PluginsGrid.Items.Refresh();
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var plugin in visiblePlugins) plugin.Selected = false;
        PluginsGrid.Items.Refresh();
    }

    private void InvertSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var plugin in visiblePlugins.Where(item => !preferences.IgnoredPlugins.Contains(item.Plugin))) plugin.Selected = !plugin.Selected;
        PluginsGrid.Items.Refresh();
    }

    private void EnableSelected_Click(object sender, RoutedEventArgs e) => SetSelectedRows(true);
    private void DisableSelected_Click(object sender, RoutedEventArgs e) => SetSelectedRows(false);

    private void IncludeFiltered_Click(object sender, RoutedEventArgs e)
    {
        if (PluginsGrid.SelectedItems.Count == 0) return;
        foreach (var plugin in PluginsGrid.SelectedItems.Cast<PluginEntry>().Where(candidate => candidate.IsFiltered))
        {
            preferences.IncludedPlugins.Add(plugin.Plugin);
            preferences.IgnoredPlugins.Remove(plugin.Plugin);
            plugin.Selected = true;
        }
        SavePreferences();
        RefreshPluginList();
        StatusText.Text = "Selected filtered candidate(s) will be included in future scans. Review them carefully before generating a patch.";
    }

    private void SetSelectedRows(bool enabled)
    {
        foreach (PluginEntry plugin in PluginsGrid.SelectedItems)
            if (!preferences.IgnoredPlugins.Contains(plugin.Plugin)) plugin.Selected = enabled;
        PluginsGrid.Items.Refresh();
    }

    private void IgnorePlugin_Click(object sender, RoutedEventArgs e)
    {
        if (PluginsGrid.SelectedItems.Count == 0) return;
        foreach (PluginEntry plugin in PluginsGrid.SelectedItems)
        {
            preferences.IgnoredPlugins.Add(plugin.Plugin);
            plugin.Selected = false;
        }
        SavePreferences();
        RefreshPluginList();
        StatusText.Text = "Plugin exclusion saved. Turn on ‘Show remembered exclusions’ and use Restore selected exclusion(s) to bring one back.";
    }

    private void RestorePlugin_Click(object sender, RoutedEventArgs e)
    {
        if (PluginsGrid.SelectedItems.Count == 0) return;
        foreach (PluginEntry plugin in PluginsGrid.SelectedItems)
        {
            preferences.IgnoredPlugins.Remove(plugin.Plugin);
            plugin.Selected = true;
        }
        SavePreferences();
        RefreshPluginList();
        StatusText.Text = "Selected plugin(s) restored to the scan list.";
    }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        var selected = scannedPlugins.Where(plugin => plugin.Selected && !preferences.IgnoredPlugins.Contains(plugin.Plugin) &&
            (!plugin.IsFiltered || preferences.IncludedPlugins.Contains(plugin.Plugin))).ToList();
        if (selected.Count == 0) { MessageBox.Show("Select at least one plugin to patch.", "No plugins selected"); return; }
        var dialog = new PatchOptionsWindow(selected, availableKeywords, preferences.DefaultChance, preferences.Framework, preferences.DistributionSettings, (plugin, settings) =>
        {
            preferences.DistributionSettings[plugin] = settings;
            SavePreferences();
        }) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var outputPicker = new OpenFolderDialog
        {
            Title = "Choose an output folder (a new MO2 mod folder or Skyrim Data folder)",
            InitialDirectory = Directory.Exists(preferences.OutputPath) ? preferences.OutputPath : dataPath
        };
        if (outputPicker.ShowDialog(this) != true) return;
        var outputDirectory = outputPicker.FolderName;
        var framework = preferences.Framework;
        var distributionRelativePath = framework == "SkyPatcher"
            ? Path.Combine("SKSE", "Plugins", "SkyPatcher", "npc", "WearableDistributor", "WearableDistributor.ini")
            : PatchGenerator.OutputIniName;
        var outputPluginPath = Path.Combine(outputDirectory, PatchGenerator.OutputPluginName);
        var outputDistributionPath = Path.Combine(outputDirectory, distributionRelativePath);
        if ((File.Exists(outputPluginPath) || File.Exists(outputDistributionPath)) &&
            MessageBox.Show("A generated patch already exists in this folder. Merge this run into it? Existing entries for the plugins you selected will be updated; all other outfits and distribution rules will be kept.", "Merge existing patch", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            preferences.OutputPath = outputDirectory;
            SavePreferences();
            ActionProgress.Visibility = Visibility.Visible;
            ActionProgress.IsIndeterminate = true;
            StatusText.Text = $"Generating {framework} outfit records and NPC distributions…";
            var result = await Task.Run(() => PatchGenerator.Generate(dialog.Options, outputDirectory, framework));
            ActionProgress.IsIndeterminate = false;
            ActionProgress.Visibility = Visibility.Collapsed;
            StatusText.Text = $"Generated {result.OutfitCount} outfit(s) and {result.DistributionCount} distribution rule(s) in {outputDirectory}";
            var runtimeNote = result.Framework == "SPID"
                ? "Install/enable SPID and enable WearableDistributor.esp together with the _DISTR.ini. SPID honors the configured chance."
                : "Install/enable SkyPatcher and enable WearableDistributor.esp with the generated npc INI. SkyPatcher outfit rules apply to every matching NPC and cannot use a random chance.";
            var masters = result.Masters.Count > 0 ? $"\n\nRequired source plugins: {string.Join(", ", result.Masters)}" : "";
            MessageBox.Show($"Patch generated and validated.\n\nPlugin: {result.PluginPath}\nDistribution: {result.DistributionPath}{masters}\n\n{runtimeNote}\n\nFor Mod Organizer 2, install the selected output folder as a mod and enable it in the profile.", "Patch ready", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ActionProgress.IsIndeterminate = false;
            ActionProgress.Visibility = Visibility.Collapsed;
            StatusText.Text = "Patch generation failed; no validated patch was produced.";
            MessageBox.Show($"Patch generation failed: {ex.Message}", "Patch generation failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static int ChanceIndex(string value) => value switch { "100" => 0, "80" => 1, "50" => 2, "25" => 3, "10" => 4, "5" => 5, _ => 3 };
    private static string ChanceFromIndex(int index) => index switch { 0 => "100", 1 => "80", 2 => "50", 4 => "10", 5 => "5", _ => "25" };
}
