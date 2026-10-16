using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace WearableDistributor;

public sealed class PatchOptionsWindow : Window
{
    private static readonly string[] PresetGroups = ["Bandits", "Forsworn", "Guards", "Mages", "Necromancers", "Farmers", "Merchants", "Adventurers", "Warriors", "Hunters", "Thieves", "Assassins", "Vampires", "Wenches", "Witches", "Bards", "Priests", "Khajiit", "Argonians", "Orcs", "Nords", "Elves", "All NPCs"];
    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bandits"] = ["bandit", "bandits", "raider", "raiders", "banditfaction"], ["Forsworn"] = ["forsworn"],
        ["Guards"] = ["guard", "guards", "watchman", "watchmen", "guardfaction"], ["Mages"] = ["mage", "mages", "wizard", "wizards", "spellcaster", "sorcerer", "jobmage"],
        ["Necromancers"] = ["necromancer", "necromancers", "necromancy"], ["Farmers"] = ["farmer", "farmers", "farmhand", "farmhands", "laborer", "jobfarmer", "farming", "farm"],
        ["Merchants"] = ["merchant", "merchants", "trader", "traders", "jobmerchant"], ["Adventurers"] = ["adventurer", "adventurers", "explorer", "explorers"],
        ["Warriors"] = ["warrior", "warriors", "knight", "knights", "soldier", "soldiers", "fighter", "fighters"],
        ["Hunters"] = ["hunter", "hunters", "ranger", "rangers", "jobhunter"], ["Thieves"] = ["thief", "thieves", "thievesguild"],
        ["Assassins"] = ["assassin", "assassins"], ["Vampires"] = ["vampire", "vampires"], ["Wenches"] = ["wench", "wenches"], ["Witches"] = ["witch", "witches", "warlock", "warlocks"],
        ["Bards"] = ["bard", "bards"], ["Priests"] = ["priest", "priests", "cleric", "clerics"], ["Khajiit"] = ["khajiit"],
        ["Argonians"] = ["argonian", "argonians"], ["Orcs"] = ["orc", "orsimer"], ["Nords"] = ["nord", "nords"],
        ["Elves"] = ["elf", "elves", "altmer", "bosmer", "dunmer"]
    };
    private static readonly string[] ArchetypeWords = ["archer", "bow", "marksman", "chief", "boss", "melee", "warrior", "mage", "wizard", "necromancer", "onehanded", "twohanded", "sorcerer", "spellsword", "healer", "scout", "ranged"];
    private readonly List<PluginPatchOptions> options = [];
    private readonly List<PageState> pages = [];
    private readonly IReadOnlyList<KeywordChoice> availableKeywords;
    private readonly string defaultChance;
    private readonly string framework;
    private readonly IReadOnlyDictionary<string, SavedDistributionOptions> savedOptions;
    private readonly Action<string, SavedDistributionOptions> saveOptions;
    private int currentIndex;
    private TextBlock stepLabel = null!;
    private ProgressBar stepProgress = null!;
    private ContentControl pageHost = null!;
    private Button backButton = null!;
    private Button nextButton = null!;
    public IReadOnlyList<PluginPatchOptions> Options => options;

    public PatchOptionsWindow(IEnumerable<PluginEntry> plugins, IEnumerable<KeywordChoice> keywords, string defaultChance, string framework,
        IReadOnlyDictionary<string, SavedDistributionOptions> savedOptions, Action<string, SavedDistributionOptions> saveOptions)
    {
        availableKeywords = keywords.ToList(); this.defaultChance = defaultChance; this.framework = framework;
        this.savedOptions = savedOptions; this.saveOptions = saveOptions;
        foreach (var plugin in plugins)
        {
            var choices = availableKeywords.OrderBy(item => item.Plugin, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Category).ThenBy(item => item.EditorId).ToList();
            savedOptions.TryGetValue(plugin.Plugin, out var saved);
            var excludedPieces = saved?.ExcludedPieceFormIds.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var restoredRules = RestoreRules(saved, choices);
            var matches = GuessGroups(plugin.Plugin).ToList();
            var config = new PluginPatchOptions
            {
                Source = plugin, Chance = saved?.Chance ?? defaultChance, Gender = saved?.Gender ?? "Any",
                Pieces = plugin.Pieces.Select(piece => new WearableRecord { Plugin = piece.Plugin, LocalId = piece.LocalId, EditorId = piece.EditorId, Name = piece.Name, Slots = piece.Slots, SourcePath = piece.SourcePath, Include = !excludedPieces.Contains(piece.FormId) }).ToList(),
                Keywords = choices,
                TargetRules = restoredRules.Count > 0 ? restoredRules : matches.Select(group => BuildRule(group, choices, "Skyrim + official DLC")).Where(rule => rule.Filters.Count > 0).ToList()
            };
            options.Add(config); pages.Add(BuildPage(config));
        }
        BuildWindow(); ShowPage(0);
    }

    private void BuildWindow()
    {
        Title = $"{framework} Distribution Setup"; Width = 1120; Height = 800; MinWidth = 900; MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = new SolidColorBrush(Color.FromRgb(243, 245, 242));
        Closing += (_, _) => SaveAllPages();
        var root = new DockPanel { Margin = new Thickness(18) };
        var banner = new Border { Background = new SolidColorBrush(Color.FromRgb(35, 54, 45)), CornerRadius = new CornerRadius(10), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 12) };
        var header = new StackPanel(); header.Children.Add(new TextBlock { Text = $"{framework} Distribution Setup", Foreground = Brushes.White, FontSize = 21, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "Choose a familiar group, then narrow it down only if you want to.", Foreground = new SolidColorBrush(Color.FromRgb(216, 228, 220)), Margin = new Thickness(0, 4, 0, 0) }); banner.Child = header;
        DockPanel.SetDock(banner, Dock.Top); root.Children.Add(banner);
        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) }; footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var status = new StackPanel(); stepLabel = new TextBlock { FontWeight = FontWeights.SemiBold }; stepProgress = new ProgressBar { Height = 8, Margin = new Thickness(0, 6, 20, 0) }; status.Children.Add(stepLabel); status.Children.Add(stepProgress); footer.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, Margin = new Thickness(4), Padding = new Thickness(12, 8, 12, 8) }; cancel.Click += (_, _) => { SaveAllPages(); DialogResult = false; Close(); };
        backButton = new Button { Content = "< Back", MinWidth = 90, Margin = new Thickness(4), Padding = new Thickness(12, 8, 12, 8) }; backButton.Click += (_, _) => { CommitCurrentPage(); ShowPage(currentIndex - 1); };
        nextButton = new Button { Content = "Next >", MinWidth = 105, Margin = new Thickness(4), Padding = new Thickness(12, 8, 12, 8), IsDefault = true }; nextButton.Click += (_, _) => { CommitCurrentPage(); SavePage(pages[currentIndex].Config); if (currentIndex == pages.Count - 1) { DialogResult = true; Close(); } else ShowPage(currentIndex + 1); };
        buttons.Children.Add(cancel); buttons.Children.Add(backButton); buttons.Children.Add(nextButton); Grid.SetColumn(buttons, 1); footer.Children.Add(buttons); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        pageHost = new ContentControl(); root.Children.Add(pageHost); Content = root;
    }

    private PageState BuildPage(PluginPatchOptions config)
    {
        var page = new DockPanel { Margin = new Thickness(8) };
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        top.Children.Add(new TextBlock { Text = config.Source.Plugin, FontSize = 19, FontWeight = FontWeights.SemiBold });
        top.Children.Add(new TextBlock { Text = $"{config.Pieces.Count} pieces found. Choose who should wear this outfit. Suggested audiences are based on the plugin title.", Foreground = Brushes.DimGray, Margin = new Thickness(0, 4, 0, 8), TextWrapping = TextWrapping.Wrap });
        var settingRow = new WrapPanel();
        var chance = new ComboBox { IsEditable = true, Text = config.Chance, Width = 160, Margin = new Thickness(0, 2, 12, 2), ItemsSource = new[] { "100", "80", "50", "25", "10", "5" } };
        var gender = new ComboBox { Width = 140, Margin = new Thickness(0, 2, 12, 2), ItemsSource = new[] { "Any", "Female", "Male" }, SelectedItem = config.Gender };
        void SaveCurrent() { config.Chance = chance.Text.Trim().TrimEnd('%'); config.Gender = gender.SelectedItem?.ToString() ?? "Any"; SavePage(config); }
        settingRow.Children.Add(Labeled("Chance (%)", chance)); settingRow.Children.Add(Labeled("NPC sex", gender)); top.Children.Add(settingRow);

        var collection = new ComboBox { Width = 310, Margin = new Thickness(0, 2, 12, 2), ItemsSource = new[] { "Skyrim + official DLC", "Creation Club", "All installed mods", "Specific mod plugin" }, SelectedIndex = 0 };
        var group = new ComboBox { Width = 260, Margin = new Thickness(0, 2, 12, 2), ItemsSource = GetGroupLabels(config.Keywords) };
        var pluginCollection = new ComboBox { Width = 280, Margin = new Thickness(0, 2, 12, 2), DisplayMemberPath = nameof(PluginLabel.Name), Visibility = Visibility.Collapsed };
        pluginCollection.ItemsSource = config.Keywords.Select(item => item.Plugin).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name).Select(name => new PluginLabel(name)).ToList();
        collection.SelectionChanged += (_, _) => pluginCollection.Visibility = collection.SelectedItem?.ToString() == "Specific mod plugin" ? Visibility.Visible : Visibility.Collapsed;
        var archetypeOptions = new ObservableCollection<SelectableChoice>(); var raceOptions = new ObservableCollection<SelectableChoice>();
        var archetypeList = CheckList(archetypeOptions); var raceList = CheckList(raceOptions);
        var selectorGrid = new Grid { Margin = new Thickness(0, 2, 0, 6) };
        selectorGrid.ColumnDefinitions.Add(new ColumnDefinition()); selectorGrid.ColumnDefinitions.Add(new ColumnDefinition());
        selectorGrid.Children.Add(new GroupBox { Header = "Optional: narrow to NPC roles", Content = archetypeList, Margin = new Thickness(2), Padding = new Thickness(6), Height = 150 });
        var raceBox = new GroupBox { Header = "Optional: limit by race", Content = raceList, Margin = new Thickness(2), Padding = new Thickness(6), Height = 150 }; Grid.SetColumn(raceBox, 1); selectorGrid.Children.Add(raceBox);
        var hint = new TextBlock { Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 3, 2, 8), Text = "Leave both lists empty to include every matching NPC in the group. For example, Bandits + Archer + Nord." };
        var ruleList = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, Height = 105, ItemsSource = config.TargetRules, IsReadOnly = true };
        ruleList.Columns.Add(new DataGridTextColumn { Header = "Group", Binding = new Binding(nameof(DistributionTargetRule.Group)), Width = 125 });
        ruleList.Columns.Add(new DataGridTextColumn { Header = "Collection", Binding = new Binding(nameof(DistributionTargetRule.Collection)), Width = 180 });
        ruleList.Columns.Add(new DataGridTextColumn { Header = "Audience filters", Binding = new Binding(nameof(DistributionTargetRule.Filters)) { Converter = new FilterSummaryConverter() }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var add = new Button { Content = "Add this audience", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(2, 2, 6, 2) };
        var remove = new Button { Content = "Remove selected", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(2) };
        add.Click += (_, _) =>
        {
            var groupName = group.SelectedItem?.ToString() ?? ""; var collectionName = collection.SelectedItem?.ToString() ?? "Skyrim + official DLC";
            var pluginName = (pluginCollection.SelectedItem as PluginLabel)?.Name;
            var rule = BuildRule(groupName, config.Keywords, collectionName, pluginName);
            rule.Filters.AddRange(archetypeOptions.Where(item => item.Selected).Select(item => item.Choice));
            rule.Filters.AddRange(raceOptions.Where(item => item.Selected).Select(item => item.Choice));
            if (rule.Filters.Count == 0) { MessageBox.Show(this, "No matching NPC group was found in the scanned records. Choose All NPCs or another collection, or use the advanced search below.", "No matching group", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            config.TargetRules.Add(rule); ruleList.Items.Refresh(); SaveCurrent();
        };
        remove.Click += (_, _) => { if (ruleList.SelectedItem is DistributionTargetRule selected) config.TargetRules.Remove(selected); ruleList.Items.Refresh(); SaveCurrent(); };
        void RefreshOptions()
        {
            var selectedGroup = group.SelectedItem?.ToString() ?? "";
            var scope = GetCollectionPlugins(config.Keywords, collection.SelectedItem?.ToString() ?? "Skyrim + official DLC", (pluginCollection.SelectedItem as PluginLabel)?.Name);
            var roleWords = selectedGroup.Equals("Farmers", StringComparison.OrdinalIgnoreCase)
                ? new[] { "farmer", "farmers", "farmhand", "farmhands", "laborer", "laborers", "worker", "workers", "jobfarmer", "farming" }
                : ArchetypeWords;
            var archetypes = config.Keywords.Where(item => scope.Contains(item.Plugin) && roleWords.Any(word => GetWords(item.EditorId).Contains(word)) && item.TargetKind is "Class" or "Keyword").ToList();
            var races = config.Keywords.Where(item => scope.Contains(item.Plugin) && item.TargetKind == "Race").ToList();
            if (selectedGroup.Equals("Farmers", StringComparison.OrdinalIgnoreCase))
                archetypes = archetypes.Where(item => roleWords.Any(word => GetWords(item.EditorId).Contains(word))).ToList();
            else if (Aliases.TryGetValue(selectedGroup, out var aliases))
                archetypes = archetypes.Where(item => ArchetypeWords.Any(word => GetWords(item.EditorId).Contains(word)) || aliases.Any(alias => GetWords(item.EditorId).Contains(alias))).ToList();
            var selectedCollection = collection.SelectedItem?.ToString() ?? "Skyrim + official DLC";
            if (selectedCollection == "Skyrim + official DLC")
            {
                archetypes = archetypes.Where(item => IsOfficialPlugin(item.Plugin)).ToList();
                races = races.Where(item => IsOfficialPlugin(item.Plugin)).ToList();
            }
            FillChecks(archetypeOptions, archetypes); FillChecks(raceOptions, races);
        }
        group.SelectionChanged += (_, _) =>
        {
            if (group.SelectedItem is string selectedGroup && !PresetGroups.Contains(selectedGroup, StringComparer.OrdinalIgnoreCase))
                collection.SelectedItem = "All installed mods";
            RefreshOptions();
        };
        collection.SelectionChanged += (_, _) => RefreshOptions(); pluginCollection.SelectionChanged += (_, _) => RefreshOptions();
        if (config.TargetRules.Count > 0) group.SelectedItem = config.TargetRules[0].Group; else group.SelectedIndex = 0;
        RefreshOptions();

        var advanced = new Expander { Header = "Advanced: search installed NPC groups and records", IsExpanded = false, Margin = new Thickness(0, 5, 0, 0) };
        var advancedPanel = new StackPanel(); var search = new TextBox { Padding = new Thickness(7), Margin = new Thickness(0, 2, 0, 6) }; var advancedChoices = new ListBox { Height = 95, SelectionMode = SelectionMode.Extended, DisplayMemberPath = nameof(KeywordChoice.EditorId) };
        advancedChoices.ItemsSource = config.Keywords.OrderBy(item => item.Category).ThenBy(item => item.EditorId).ToList(); search.TextChanged += (_, _) => advancedChoices.ItemsSource = config.Keywords.Where(item => item.EditorId.Contains(search.Text, StringComparison.OrdinalIgnoreCase) || item.Plugin.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).OrderBy(item => item.Category).ThenBy(item => item.EditorId).ToList();
        var addAdvanced = new Button { Content = "Add selected records as an audience", Padding = new Thickness(10, 5, 10, 5), HorizontalAlignment = HorizontalAlignment.Left };
        addAdvanced.Click += (_, _) => { var selected = advancedChoices.SelectedItems.Cast<KeywordChoice>().Select(CloneChoice).ToList(); if (selected.Count == 0) return; config.TargetRules.Add(new DistributionTargetRule { Collection = "Advanced selection", Group = "Custom NPC group", Filters = selected }); ruleList.Items.Refresh(); SaveCurrent(); };
        advancedPanel.Children.Add(new TextBlock { Text = "Search factions, occupations, NPC types, races, or records added by mods." }); advancedPanel.Children.Add(search); advancedPanel.Children.Add(advancedChoices); advancedPanel.Children.Add(addAdvanced); advanced.Content = advancedPanel;

        var audienceStack = new StackPanel(); audienceStack.Children.Add(new WrapPanel { Children = { Labeled("1. NPC collection", collection), Labeled("2. Overall group", group), Labeled("Mod plugin", pluginCollection) } }); audienceStack.Children.Add(selectorGrid); audienceStack.Children.Add(hint); audienceStack.Children.Add(new WrapPanel { Children = { add, remove } }); audienceStack.Children.Add(ruleList); audienceStack.Children.Add(advanced);
        var audienceBox = new GroupBox { Header = "Who should wear this outfit?", Padding = new Thickness(8), Margin = new Thickness(0, 6, 0, 8), Content = audienceStack };
        top.Children.Add(audienceBox); DockPanel.SetDock(top, Dock.Top); page.Children.Add(top);

        var pieceView = CollectionViewSource.GetDefaultView(config.Pieces); pieceView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(WearableRecord.Variant)));
        var pieceGrid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, ItemsSource = pieceView, HeadersVisibility = DataGridHeadersVisibility.Column };
        pieceGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "Include", Binding = new Binding(nameof(WearableRecord.Include)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 70 });
        pieceGrid.Columns.Add(new DataGridTextColumn { Header = "Outfit piece", Binding = new Binding(nameof(WearableRecord.Name)), Width = new DataGridLength(2, DataGridLengthUnitType.Star), IsReadOnly = true });
        pieceGrid.Columns.Add(new DataGridTextColumn { Header = "Color / variant", Binding = new Binding(nameof(WearableRecord.Variant)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });
        pieceGrid.Columns.Add(new DataGridTextColumn { Header = "Slots", Binding = new Binding(nameof(WearableRecord.Slots)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });
        var pieceActions = new StackPanel { Orientation = Orientation.Horizontal };
        var select = new Button { Content = "Select all pieces", Margin = new Thickness(0, 0, 5, 5), Padding = new Thickness(8, 4, 8, 4) }; select.Click += (_, _) => { foreach (var piece in config.Pieces) piece.Include = true; pieceGrid.Items.Refresh(); };
        var clear = new Button { Content = "Clear all pieces", Margin = new Thickness(0, 0, 5, 5), Padding = new Thickness(8, 4, 8, 4) }; clear.Click += (_, _) => { foreach (var piece in config.Pieces) piece.Include = false; pieceGrid.Items.Refresh(); };
        pieceActions.Children.Add(select); pieceActions.Children.Add(clear); var piecePanel = new DockPanel(); DockPanel.SetDock(pieceActions, Dock.Top); piecePanel.Children.Add(pieceActions); piecePanel.Children.Add(pieceGrid);
        page.Children.Add(new GroupBox { Header = "Outfit parts — uncheck anything you want to leave out", Padding = new Thickness(8), Content = piecePanel });
        return new PageState(config, page, () => { config.Chance = chance.Text.Trim().TrimEnd('%'); config.Gender = gender.SelectedItem?.ToString() ?? "Any"; });
    }

    private static DistributionTargetRule BuildRule(string group, IReadOnlyList<KeywordChoice> choices, string collection, string? plugin = null)
    {
        var scope = GetCollectionPlugins(choices, collection, plugin);
        var eligible = choices.Where(item => scope.Contains(item.Plugin));
        var filters = ResolveGroupChoices(group, eligible);
        return new DistributionTargetRule { Collection = collection, Group = group, Filters = filters };
    }

    private static List<KeywordChoice> ResolveGroupChoices(string group, IEnumerable<KeywordChoice> source)
    {
        var choices = source.ToList(); if (group.Equals("All NPCs", StringComparison.OrdinalIgnoreCase)) return choices.Where(item => item.EditorId.Equals("ActorTypeNPC", StringComparison.OrdinalIgnoreCase) && item.TargetKind == "Keyword").Take(1).Select(CloneChoice).ToList();
        if (!Aliases.TryGetValue(group, out var aliases)) aliases = GetWords(group).Where(word => !new[] { "faction", "keyword", "job", "npc", "type", "group", "i", "the", "of" }.Contains(word, StringComparer.OrdinalIgnoreCase)).ToArray();
        var matching = choices.Where(item => aliases.Any(alias => GetWords(item.EditorId).Contains(alias)))
            .Where(item => !(group.Equals("Farmers", StringComparison.OrdinalIgnoreCase) && GetWords(item.EditorId).Contains("animal")))
            .ToList();
        var preferFaction = group is "Bandits" or "Forsworn" or "Guards" or "Farmers" or "Thieves" or "Assassins" or "Vampires";
        var preferred = matching.Where(item => preferFaction ? item.TargetKind == "Faction" : item.TargetKind == "Keyword").ToList();
        if (preferred.Count == 0) preferred = matching.Where(item => item.TargetKind is "Class" or "Faction").ToList();
        if (preferred.Count == 0) preferred = matching;
        return preferred.Select(CloneChoice).DistinctBy(item => item.FormId + "|" + item.TargetKind, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<string> GetGroupLabels(IEnumerable<KeywordChoice> choices)
    {
        var meaningful = new HashSet<string>(["wench", "courtesan", "maid", "bandit", "guard", "mage", "adventurer", "merchant", "farmer", "hunter", "vampire", "necromancer", "thief", "assassin", "forsworn", "soldier", "warrior", "witch", "priest", "bard"], StringComparer.OrdinalIgnoreCase);
        var discovered = choices.Where(item => item.TargetKind is "Faction" or "Keyword")
            .Where(item => GetWords(item.EditorId).Any(meaningful.Contains))
            .Where(item => !Aliases.Values.Any(aliases => aliases.Any(alias => GetWords(item.EditorId).Contains(alias))))
            .Select(item => Humanize(item.EditorId))
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase);
        return PresetGroups.Concat(discovered).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsOfficialPlugin(string name) => new[] { "Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm" }.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> GetCollectionPlugins(IReadOnlyList<KeywordChoice> choices, string collection, string? plugin = null)
    {
        IEnumerable<string> names = choices.Select(item => item.Plugin).Distinct(StringComparer.OrdinalIgnoreCase);
        if (collection == "Skyrim + official DLC") names = names.Where(IsOfficialPlugin);
        else if (collection == "Creation Club") names = names.Where(name => name.StartsWith("cc", StringComparison.OrdinalIgnoreCase));
        else if (collection == "Specific mod plugin") names = names.Where(name => plugin == null || name.Equals(plugin, StringComparison.OrdinalIgnoreCase));
        return names.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GuessGroups(string plugin)
    {
        var words = GetWords(Path.GetFileNameWithoutExtension(plugin));
        return Aliases.Where(pair => pair.Value.Any(words.Contains)).Select(pair => pair.Key);
    }

    private static void FillChecks(ObservableCollection<SelectableChoice> target, IEnumerable<KeywordChoice> choices)
    {
        target.Clear(); foreach (var choice in choices.GroupBy(item => item.FormId + "|" + item.TargetKind, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).Take(80)) target.Add(new SelectableChoice(CloneChoice(choice), $"{Humanize(choice.EditorId)}  ·  {choice.Plugin}"));
    }
    private static ListBox CheckList(ObservableCollection<SelectableChoice> items)
    {
        var list = new ListBox { ItemsSource = items, BorderThickness = new Thickness(0), Padding = new Thickness(0) };
        list.ItemTemplate = new DataTemplate(typeof(SelectableChoice)); var factory = new FrameworkElementFactory(typeof(CheckBox)); factory.SetBinding(CheckBox.ContentProperty, new Binding(nameof(SelectableChoice.Label))); factory.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(SelectableChoice.Selected)) { Mode = BindingMode.TwoWay }); list.ItemTemplate.VisualTree = factory; return list;
    }
    private static void AddCheckChoices(ObservableCollection<SelectableChoice> target, IEnumerable<KeywordChoice> choices) { foreach (var choice in choices) target.Add(new SelectableChoice(CloneChoice(choice), Humanize(choice.EditorId) + "  ·  " + choice.Plugin)); }
    private static KeywordChoice CloneChoice(KeywordChoice item) => new() { EditorId = item.EditorId, Plugin = item.Plugin, Category = item.Category, TargetKind = item.TargetKind, LocalId = item.LocalId, Selected = item.Selected };
    private static string Humanize(string value) { var split = Regex.Replace(value, "(?<=[a-z0-9])(?=[A-Z])", " "); return Regex.Replace(split.Replace('_', ' '), "(?<!^)(?=[A-Z][a-z])", " "); }
    private static HashSet<string> GetWords(string value) { var split = Regex.Replace(value, "(?<=[a-z0-9])(?=[A-Z])", " "); return Regex.Matches(split, "[A-Za-z0-9]+").Select(match => match.Value.ToLowerInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase); }
    private static FrameworkElement Labeled(string label, FrameworkElement control) { var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold }); panel.Children.Add(control); return panel; }
    private void ShowPage(int index) { currentIndex = Math.Clamp(index, 0, pages.Count - 1); pageHost.Content = pages[currentIndex].View; stepLabel.Text = $"Outfit {currentIndex + 1} of {pages.Count}: {pages[currentIndex].Config.Source.Plugin}"; stepProgress.Value = 100.0 * (currentIndex + 1) / pages.Count; backButton.IsEnabled = currentIndex > 0; nextButton.Content = currentIndex == pages.Count - 1 ? "Generate Patch" : "Next >"; }
    private void CommitCurrentPage() => pages[currentIndex].Commit();
    private void SaveAllPages()
    {
        foreach (var page in pages) { page.Commit(); SavePage(page.Config); }
    }
    private void SavePage(PluginPatchOptions config)
    {
        saveOptions(config.Source.Plugin, new SavedDistributionOptions
        {
            Chance = config.Chance,
            Gender = config.Gender,
            ExcludedPieceFormIds = config.Pieces.Where(piece => !piece.Include).Select(piece => piece.FormId).ToList(),
            TargetRules = config.TargetRules.Select(rule => new SavedDistributionTargetRule
            {
                Collection = rule.Collection,
                Group = rule.Group,
                Filters = rule.Filters.Select(item => new SavedTargetChoice { EditorId = item.EditorId, Plugin = item.Plugin, Category = item.Category, TargetKind = item.TargetKind, LocalId = item.LocalId }).ToList()
            }).ToList()
        });
    }
    private static List<DistributionTargetRule> RestoreRules(SavedDistributionOptions? saved, IReadOnlyList<KeywordChoice> choices)
    {
        if (saved is null) return [];
        var available = choices.ToDictionary(item => item.TargetKind + "|" + item.FormId, StringComparer.OrdinalIgnoreCase);
        var restored = new List<DistributionTargetRule>();
        foreach (var savedRule in saved.TargetRules ?? [])
        {
            var filters = savedRule.Filters.Select(item => available.GetValueOrDefault(item.TargetKind + "|0x" + item.LocalId.ToString("X6") + "~" + item.Plugin))
                .Where(item => item is not null).Cast<KeywordChoice>().Select(CloneChoice).ToList();
            if (filters.Count > 0) restored.Add(new DistributionTargetRule { Collection = savedRule.Collection, Group = savedRule.Group, Filters = filters });
        }
        return restored;
    }
    private sealed record PageState(PluginPatchOptions Config, FrameworkElement View, Action Commit);
    private sealed record PluginLabel(string Name);
    private sealed class SelectableChoice(KeywordChoice choice, string label) { public KeywordChoice Choice { get; } = choice; public string Label { get; } = label; public bool Selected { get; set; } }
    private sealed class FilterSummaryConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value is List<KeywordChoice> filters ? string.Join(", ", filters.Select(item => Humanize(item.EditorId))) : "";
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
    }
}
