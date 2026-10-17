using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace WearableDistributor;

/// <summary>A FOMOD-style, scrollable outfit wizard. The footer and outfit navigation stay visible.</summary>
public sealed class PatchOptionsWindow : Window
{
    private readonly List<PluginPatchOptions> options = [];
    private readonly List<PageState> pages = [];
    private readonly IReadOnlyList<KeywordChoice> availableKeywords;
    private readonly NpcTargetingService targeting;
    private readonly bool hasNpcScan;
    private readonly string defaultChance;
    private readonly string framework;
    private readonly IReadOnlyDictionary<string, SavedDistributionOptions> savedOptions;
    private readonly Action<string, SavedDistributionOptions> saveOptions;
    private readonly IReadOnlyDictionary<string, SavedDistributionOptions> presets;
    private readonly Action<string, SavedDistributionOptions> savePreset;
    private readonly Action<string> removeSavedOptions;
    private readonly Action<string> removePreset;
    private readonly Dictionary<PluginPatchOptions, string> restorationNotes = [];
    private int currentIndex;
    private bool changingStep;
    private TextBlock stepLabel = null!;
    private ProgressBar stepProgress = null!;
    private ContentControl pageHost = null!;
    private ListBox outfitSteps = null!;
    private Button backButton = null!;
    private Button nextButton = null!;
    private Button skipButton = null!;
    private readonly ISet<string> skippedOutfits;
    private readonly IReadOnlyList<MerchantTarget> merchantTargets;
    public IReadOnlyList<PluginPatchOptions> Options => options.Where(config => !IsSkipped(config)).ToList();
    public IReadOnlyList<PluginPatchOptions> SkippedOptions => options.Where(IsSkipped).ToList();

    public PatchOptionsWindow(IEnumerable<PluginEntry> plugins, IEnumerable<KeywordChoice> keywords, string defaultChance, string framework,
        IReadOnlyDictionary<string, SavedDistributionOptions> savedOptions, Action<string, SavedDistributionOptions> saveOptions,
        IReadOnlyDictionary<string, SavedDistributionOptions>? presets = null, Action<string, SavedDistributionOptions>? savePreset = null,
        Action<string>? removeSavedOptions = null, Action<string>? removePreset = null, IEnumerable<NpcTargetProfile>? npcProfiles = null,
        ISet<string>? skippedOutfits = null, IEnumerable<MerchantTarget>? merchantTargets = null)
    {
        availableKeywords = keywords.DistinctBy(item => item.TargetKind + "|" + item.FormId, StringComparer.OrdinalIgnoreCase).ToList();
        var scannedNpcs = npcProfiles?.ToList() ?? [];
        hasNpcScan = scannedNpcs.Count > 0;
        targeting = new NpcTargetingService(availableKeywords, scannedNpcs);
        this.defaultChance = defaultChance; this.framework = framework;
        this.savedOptions = savedOptions; this.saveOptions = saveOptions;
        this.presets = presets ?? new Dictionary<string, SavedDistributionOptions>();
        this.savePreset = savePreset ?? ((_, _) => { });
        this.removeSavedOptions = removeSavedOptions ?? (_ => { });
        this.removePreset = removePreset ?? (_ => { });
        this.skippedOutfits = skippedOutfits ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        this.merchantTargets = merchantTargets?.ToList() ?? [];
        foreach (var plugin in plugins)
        {
            savedOptions.TryGetValue(plugin.Plugin, out var saved);
            AddInitialConfiguration(plugin, Path.GetFileNameWithoutExtension(plugin.Plugin), saved);
            var prefix = plugin.Plugin + "|outfit|";
            foreach (var pair in savedOptions.Where(pair => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                AddInitialConfiguration(plugin, string.IsNullOrWhiteSpace(pair.Value.OutfitName) ? pair.Key[prefix.Length..] : pair.Value.OutfitName, pair.Value);
        }
        if (options.Count == 0) throw new ArgumentException("Select at least one outfit plugin before opening distribution setup.", nameof(plugins));
        BuildWindow(); ShowPage(0);
    }

    private void AddInitialConfiguration(PluginEntry plugin, string name, SavedDistributionOptions? saved)
    {
        var excluded = (saved?.ExcludedPieceFormIds ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var restored = RestoreRules(saved, out var missing);
        var config = new PluginPatchOptions
        {
            Source = plugin, OutfitName = name,
            Chance = IsSkyPatcher ? "100" : saved?.Chance ?? defaultChance, Gender = saved?.Gender ?? "Any",
            Pieces = plugin.Pieces.Select(piece => ClonePiece(piece, !excluded.Contains(piece.FormId))).ToList(),
            Keywords = availableKeywords.ToList(),
            // A deliberately empty or stale saved setup must never silently become a broader suggestion.
            TargetRules = saved is not null ? restored : SuggestAudiences(plugin.Plugin)
        };
        if (missing > 0) restorationNotes[config] = $"{missing} saved audience(s) could not be restored completely. Their targets may have changed or been removed. Choose replacement audiences below.";
        config.MerchantCategories = (saved?.MerchantCategories ?? []).Where(MerchantStockService.Categories.Contains).Distinct().ToList();
        config.IncludeMerchandiseWeapons = saved?.IncludeMerchandiseWeapons ?? true;
        config.MerchantTargets = merchantTargets.ToList();
        options.Add(config); pages.Add(BuildPage(config));
    }

    private bool IsSkyPatcher => framework.Equals("SkyPatcher", StringComparison.OrdinalIgnoreCase);

    private void BuildWindow()
    {
        UiTheme.Apply(this);
        Title = $"Skyrim SPID Patch Generator · {framework} setup";
        Width = 1280; Height = 860; MinWidth = 980; MinHeight = 600;
        MaxHeight = Math.Max(600, SystemParameters.WorkArea.Height);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Closing += (_, _) => SaveAllPages();

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        var stage = new TextBlock { Text = "02  /  DISTRIBUTION", Foreground = UiTheme.Brush("AccentBrush"), FontWeight = FontWeights.SemiBold, FontSize = 12, Margin = new Thickness(0, 0, 0, 5) };
        var heading = new StackPanel(); heading.Children.Add(stage);
        heading.Children.Add(new TextBlock { Text = "Build your outfit distribution", FontSize = 25, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(UiTheme.Caption("Choose an audience and outfit parts. Each plugin has its own step; your choices are remembered."));
        header.Children.Add(heading); root.Children.Add(header);

        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(228) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) }); body.ColumnDefinitions.Add(new ColumnDefinition());
        var sidebar = new DockPanel();
        var sidebarTitle = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        sidebarTitle.Children.Add(new TextBlock { Text = "OUTFIT STEPS", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = UiTheme.Brush("MutedBrush") });
        sidebarTitle.Children.Add(UiTheme.Caption("Jump to any outfit to review or change it."));
        DockPanel.SetDock(sidebarTitle, Dock.Top); sidebar.Children.Add(sidebarTitle);
        var sidebarNote = UiTheme.Caption("Adults with wearable bodies only. Groups and races come from the current NPC scan.");
        DockPanel.SetDock(sidebarNote, Dock.Bottom); sidebar.Children.Add(sidebarNote);
        outfitSteps = new ListBox { Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), SelectionMode = SelectionMode.Single };
        AutomationProperties.SetName(outfitSteps, "Outfits in this patch setup");
        outfitSteps.SelectionChanged += (_, _) =>
        {
            if (changingStep || outfitSteps.SelectedIndex < 0 || outfitSteps.SelectedIndex == currentIndex) return;
            CommitCurrentPage(); ShowPage(outfitSteps.SelectedIndex);
        };
        sidebar.Children.Add(outfitSteps); body.Children.Add(sidebar);
        pageHost = new ContentControl(); Grid.SetColumn(pageHost, 2); body.Children.Add(pageHost);
        Grid.SetRow(body, 1); root.Children.Add(body);

        var footer = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var status = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 22, 0) };
        stepLabel = new TextBlock { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        stepProgress = new ProgressBar { Height = 5, Margin = new Thickness(0, 9, 0, 0), Maximum = 100 };
        status.Children.Add(stepLabel); status.Children.Add(stepProgress); footer.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var cancel = UiTheme.Button("Close setup"); cancel.MinWidth = 100; cancel.IsCancel = true;
        cancel.ToolTip = "Close without generating. Your setup choices are saved.";
        cancel.Click += (_, _) => { SaveAllPages(); DialogResult = false; };
        backButton = UiTheme.Button("← Back"); backButton.MinWidth = 90;
        backButton.Click += (_, _) => { CommitCurrentPage(); ShowPage(currentIndex - 1); };
        skipButton = UiTheme.Button("Skip outfit"); skipButton.MinWidth = 100;
        skipButton.ToolTip = "Leave this outfit out of this patch session. Saved choices and any existing patch for it are kept. Select it in the sidebar to include it again.";
        skipButton.Click += (_, _) =>
        {
            CommitCurrentPage();
            var key = GetSavedKey(pages[currentIndex].Config);
            if (skippedOutfits.Remove(key)) { ShowPage(currentIndex); return; }
            skippedOutfits.Add(key);
            if (Options.Count == 0) { DialogResult = false; return; }
            if (currentIndex < pages.Count - 1) ShowPage(currentIndex + 1);
            else CompleteSetup();
        };
        nextButton = UiTheme.Button("Next outfit →", true); nextButton.MinWidth = 138; nextButton.IsDefault = true;
        nextButton.Click += (_, _) =>
        {
            CommitCurrentPage();
            if (!IsSkipped(pages[currentIndex].Config) && !pages[currentIndex].Validate()) return;
            SavePage(pages[currentIndex].Config);
            if (currentIndex < pages.Count - 1) { ShowPage(currentIndex + 1); return; }
            CompleteSetup();
        };
        buttons.Children.Add(cancel); buttons.Children.Add(backButton); buttons.Children.Add(skipButton); buttons.Children.Add(nextButton); Grid.SetColumn(buttons, 1); footer.Children.Add(buttons);
        Grid.SetRow(footer, 2); root.Children.Add(footer); Content = root;
    }

    private PageState BuildPage(PluginPatchOptions config)
    {
        var content = new StackPanel { Margin = new Thickness(0, 0, 12, 8) };
        var title = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        title.Children.Add(new TextBlock { Text = config.OutfitName, FontSize = 21, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        title.Children.Add(UiTheme.Caption(config.Source.Plugin));
        var summary = new TextBlock { Foreground = UiTheme.Brush("AccentBrush"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
        summary.ToolTip = "NPC counts describe distinct base records in the scan. More than one spawned actor can use the same record.";
        title.Children.Add(summary); content.Children.Add(title);
        var feedback = new TextBlock { Foreground = UiTheme.Brush("WarningBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), Visibility = Visibility.Collapsed };
        if (restorationNotes.TryGetValue(config, out var restoreNote)) { feedback.Text = restoreNote; feedback.Visibility = Visibility.Visible; }
        content.Children.Add(feedback);

        var chance = new ComboBox { Width = 140, IsEditable = !IsSkyPatcher, IsEnabled = !IsSkyPatcher, ItemsSource = new[] { "100", "80", "50", "25", "10", "5" }, Text = config.Chance, Margin = new Thickness(0, 5, 14, 5) };
        if (IsSkyPatcher) { chance.SelectedItem = "100"; chance.Text = "100"; }
        var gender = new ComboBox { Width = 150, ItemsSource = new[] { "Any", "Female", "Male" }, SelectedItem = config.Gender, Margin = new Thickness(0, 5, 14, 5) };
        if (gender.SelectedItem is null) gender.SelectedIndex = 0;
        var settings = new StackPanel();
        settings.Children.Add(new WrapPanel { Children = { Labeled(IsSkyPatcher ? "Assignment (%)" : "Chance per NPC (%)", chance), Labeled("NPC sex", gender) } });
        var chanceHint = UiTheme.Caption(""); settings.Children.Add(chanceHint);
        var sell = new CheckBox { Content = "Also sell selected outfit parts through traders", IsChecked = config.MerchantCategories.Count > 0, Margin = new Thickness(0, 10, 0, 5) };
        var shopOptions = new StackPanel { Margin = new Thickness(23, 0, 0, 0) };
        var shopChecks = MerchantStockService.Categories.Select(category => (Category: category, Box: new CheckBox
        {
            Content = category + " (" + merchantTargets.Count(target => target.Category == category) + " stock containers)",
            IsChecked = config.MerchantCategories.Count == 0 || config.MerchantCategories.Contains(category)
        })).ToList();
        foreach (var (_, box) in shopChecks) shopOptions.Children.Add(box);
        var stockWeapons = new CheckBox { Content = $"Include weapons from this plugin ({config.Source.Weapons.Count})", IsChecked = config.IncludeMerchandiseWeapons, IsEnabled = config.Source.Weapons.Count > 0, Margin = new Thickness(0, 6, 0, 0) };
        shopOptions.Children.Add(stockWeapons);
        shopOptions.Children.Add(UiTheme.Caption("Requires SkyPatcher with container patching enabled, even with SPID NPC outfits. Adds one of each included item to shop stock; NPC chance does not apply. Weapons go only to blacksmiths/general goods, never clothing traders."));
        shopOptions.Visibility = sell.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        settings.Children.Add(sell); settings.Children.Add(shopOptions);
        content.Children.Add(new GroupBox { Header = "1. Distribution", Content = settings, Margin = new Thickness(0, 0, 0, 12) });

        var ruleList = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, Height = 150, SelectionMode = DataGridSelectionMode.Single };
        ruleList.Columns.Add(new DataGridTextColumn { Header = "Audience", Binding = new Binding(nameof(AudienceRow.Group)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        ruleList.Columns.Add(new DataGridTextColumn { Header = "Roles / races", Binding = new Binding(nameof(AudienceRow.Details)), Width = new DataGridLength(1.5, DataGridLengthUnitType.Star) });
        ruleList.Columns.Add(new DataGridTextColumn { Header = "NPCs", Binding = new Binding(nameof(AudienceRow.Matches)), Width = 70 });
        var audiencesEmpty = UiTheme.Caption("No audience added yet. Choose a group below, then click Add audience.");
        var collection = new ComboBox { MinWidth = 250, MaxWidth = 340, ItemsSource = targeting.GetCollections(), SelectedIndex = 0, Margin = new Thickness(0, 5, 12, 6) };
        var group = new ComboBox { MinWidth = 200, MaxWidth = 320, Margin = new Thickness(0, 5, 0, 6) };
        var roleChoices = new ObservableCollection<SelectableChoice>(); var raceChoices = new ObservableCollection<SelectableChoice>();
        var roles = CheckList(roleChoices); var races = CheckList(raceChoices);
        var roleEmpty = UiTheme.Caption("No separate roles found for this group. All members will be included.");
        var raceEmpty = UiTheme.Caption("No usable race records found for this group.");
        var rolePanel = new StackPanel(); rolePanel.Children.Add(roleEmpty); rolePanel.Children.Add(roles);
        var racePanel = new StackPanel(); racePanel.Children.Add(raceEmpty); racePanel.Children.Add(races);
        var narrow = new Grid { Margin = new Thickness(0, 6, 0, 0) }; narrow.ColumnDefinitions.Add(new ColumnDefinition()); narrow.ColumnDefinitions.Add(new ColumnDefinition());
        var rolesBox = new GroupBox { Header = "Roles · optional", Content = rolePanel, Background = UiTheme.Brush("AppBackgroundBrush"), Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10) };
        var racesBox = new GroupBox { Header = "Races · optional", Content = racePanel, Background = UiTheme.Brush("AppBackgroundBrush"), Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10) }; Grid.SetColumn(racesBox, 1);
        narrow.Children.Add(rolesBox); narrow.Children.Add(racesBox);
        var draftSummary = UiTheme.Caption(""); draftSummary.Margin = new Thickness(0, 10, 0, 8);
        var addAudience = UiTheme.Button("＋ Add audience", true);
        var removeAudience = UiTheme.Button("Remove selected"); removeAudience.IsEnabled = false;
        ruleList.SelectionChanged += (_, _) => removeAudience.IsEnabled = ruleList.SelectedItem is AudienceRow;

        void CommitFields()
        {
            config.Chance = IsSkyPatcher ? "100" : chance.Text.Trim().TrimEnd('%').Trim();
            config.Gender = gender.SelectedItem?.ToString() ?? "Any";
            config.MerchantCategories = sell.IsChecked == true ? shopChecks.Where(item => item.Box.IsChecked == true).Select(item => item.Category).ToList() : [];
            config.IncludeMerchandiseWeapons = stockWeapons.IsChecked == true;
            stockWeapons.IsEnabled = config.Source.Weapons.Count > 0 && config.MerchantCategories.Any(category => category != MerchantStockService.Clothing);
        }
        void UpdateSummary()
        {
            CommitFields();
            var match = hasNpcScan ? config.TargetRules.SelectMany(rule => targeting.GetMatchingProfiles(rule, config.Gender)).DistinctBy(profile => profile.FormId, StringComparer.OrdinalIgnoreCase).Count() : -1;
            summary.Text = $"{config.Pieces.Count(piece => piece.Include)} / {config.Pieces.Count} parts included   ·   {config.TargetRules.Count} audience(s)" + (match >= 0 ? $"   ·   {match} matching NPCs" : "") + $"   ·   {config.Chance}%" + (config.MerchantCategories.Count > 0 ? "   ·   Merchandise enabled" : "");
            chanceHint.Text = IsSkyPatcher ? "Every matched NPC receives this outfit. Use SPID for a random chance. Existing outfits are replaced."
                : int.TryParse(config.Chance, out var probability) && probability is > 0 and <= 100
                    ? probability == 100 ? "Every eligible NPC receives this outfit. Existing outfits are replaced."
                        : 100 % probability == 0 ? $"{probability}% gives this outfit to roughly 1 in {100 / probability} eligible NPCs. Existing outfits are replaced."
                            : $"{probability}% rolls independently for each eligible NPC. Existing outfits are replaced."
                    : "Enter a whole-number chance from 1 to 100.";
            if (outfitSteps is not null) RefreshSteps();
        }
        void SaveCurrent() { CommitFields(); SavePage(config); UpdateSummary(); }
        sell.Checked += (_, _) => { shopOptions.Visibility = Visibility.Visible; SaveCurrent(); };
        sell.Unchecked += (_, _) => { shopOptions.Visibility = Visibility.Collapsed; SaveCurrent(); };
        foreach (var (_, box) in shopChecks) { box.Checked += (_, _) => SaveCurrent(); box.Unchecked += (_, _) => SaveCurrent(); }
        stockWeapons.Checked += (_, _) => SaveCurrent(); stockWeapons.Unchecked += (_, _) => SaveCurrent();
        void RefreshAudiences()
        {
            ruleList.ItemsSource = config.TargetRules.Select(rule => new AudienceRow(rule.Group, AudienceDetails(rule), hasNpcScan ? targeting.CountMatches(rule, config.Gender).ToString(CultureInfo.InvariantCulture) : "—", rule)).ToList();
            audiencesEmpty.Visibility = config.TargetRules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ruleList.Visibility = config.TargetRules.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateSummary();
        }
        DistributionTargetRule DraftRule()
        {
            var draft = targeting.BuildRule(group.SelectedItem?.ToString() ?? "", collection.SelectedItem?.ToString() ?? NpcTargetingService.OfficialCollection);
            draft.Filters.AddRange(roleChoices.Where(item => item.Selected).SelectMany(item => item.Choices).Select(item => CloneChoice(item, "NPC role")));
            draft.Filters.AddRange(raceChoices.Where(item => item.Selected).SelectMany(item => item.Choices).Select(item => CloneChoice(item, "NPC race")));
            draft.NpcResolutionComplete = false;
            return draft;
        }
        void RefreshDraft()
        {
            var draft = DraftRule(); var matches = targeting.GetMatchingProfiles(draft, gender.SelectedItem?.ToString() ?? "Any");
            addAudience.IsEnabled = draft.Filters.Count > 0 && matches.Count > 0;
            draftSummary.Text = matches.Count == 0
                ? hasNpcScan ? "No eligible NPCs match this selection. Try another group, role, race, or collection." : "NPC targets are unavailable. Rescan the deployed game Data folder to enable audience selection."
                : $"{matches.Count} matching NPCs · " + string.Join(", ", matches.Take(4).Select(profile => string.IsNullOrWhiteSpace(profile.Name) ? profile.EditorId : profile.Name)) + (matches.Count > 4 ? ", …" : "");
            draftSummary.Foreground = UiTheme.Brush(matches.Count == 0 ? "WarningBrush" : "MutedBrush");
        }
        void RefreshNarrowing()
        {
            var selectedCollection = collection.SelectedItem?.ToString() ?? NpcTargetingService.OfficialCollection;
            var selectedGroup = group.SelectedItem?.ToString() ?? "";
            FillChecks(roleChoices, targeting.GetRoles(selectedCollection, selectedGroup), RefreshDraft);
            FillChecks(raceChoices, targeting.GetRaces(selectedCollection, selectedGroup), RefreshDraft);
            roleEmpty.Visibility = roleChoices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            raceEmpty.Visibility = raceChoices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            roles.Visibility = roleChoices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            races.Visibility = raceChoices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshDraft();
        }
        void RefreshGroups()
        {
            var previous = group.SelectedItem?.ToString();
            var groups = targeting.GetGroupLabels(collection.SelectedItem?.ToString() ?? NpcTargetingService.OfficialCollection);
            group.ItemsSource = groups;
            group.SelectedItem = groups.Contains(previous ?? "") ? previous : groups.FirstOrDefault();
            RefreshNarrowing();
        }
        collection.SelectionChanged += (_, _) => RefreshGroups();
        group.SelectionChanged += (_, _) => RefreshNarrowing();
        gender.SelectionChanged += (_, _) => { RefreshAudiences(); RefreshDraft(); SaveCurrent(); };
        chance.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdateSummary()));
        chance.LostKeyboardFocus += (_, _) => SaveCurrent();
        addAudience.Click += (_, _) =>
        {
            var rule = DraftRule();
            if (targeting.CountMatches(rule, config.Gender) == 0) return;
            if (config.TargetRules.Any(existing => RuleSignature(existing) == RuleSignature(rule)))
            {
                feedback.Text = "That audience is already included. Select another group or narrow the roles and races."; feedback.Visibility = Visibility.Visible; return;
            }
            config.TargetRules.Add(rule); feedback.Visibility = Visibility.Collapsed; RefreshAudiences(); SaveCurrent();
        };
        removeAudience.Click += (_, _) => { if (ruleList.SelectedItem is AudienceRow row) config.TargetRules.Remove(row.Rule); RefreshAudiences(); SaveCurrent(); };

        var audiencePanel = new StackPanel();
        audiencePanel.Children.Add(UiTheme.Caption("Pick the NPC collection, then a familiar group. Leave roles and races unchecked to include the whole group. Multiple checked roles or races are alternatives."));
        audiencePanel.Children.Add(new WrapPanel { Children = { Labeled("NPC collection", collection), Labeled("Group", group) } });
        audiencePanel.Children.Add(narrow); audiencePanel.Children.Add(draftSummary);
        audiencePanel.Children.Add(new WrapPanel { Margin = new Thickness(0, 0, 0, 12), Children = { addAudience, removeAudience } });
        audiencePanel.Children.Add(audiencesEmpty); audiencePanel.Children.Add(ruleList);
        audiencePanel.Children.Add(UiTheme.Caption("Separate audiences are alternatives. Within one audience: group + any selected role + any selected race are required together."));
        content.Children.Add(new GroupBox { Header = "2. Who should wear it?", Content = audiencePanel, Margin = new Thickness(0, 0, 0, 12) });

        var pieceView = new ListCollectionView(config.Pieces); pieceView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(WearableRecord.Variant)));
        var pieceGrid = new DataGrid { AutoGenerateColumns = false, ItemsSource = pieceView, Height = 320, MinHeight = 170, SelectionMode = DataGridSelectionMode.Single };
        VirtualizingPanel.SetIsVirtualizingWhenGrouping(pieceGrid, true); VirtualizingPanel.SetVirtualizationMode(pieceGrid, VirtualizationMode.Recycling);
        var usePart = new FrameworkElementFactory(typeof(CheckBox));
        usePart.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(WearableRecord.Include)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        usePart.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        usePart.SetValue(AutomationProperties.NameProperty, "Include outfit part");
        pieceGrid.Columns.Add(new DataGridTemplateColumn { Header = "Use", CellTemplate = new DataTemplate { VisualTree = usePart }, Width = 55 });
        pieceGrid.Columns.Add(new DataGridTextColumn { Header = "Outfit part", Binding = new Binding(nameof(WearableRecord.Name)), IsReadOnly = true, Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        pieceGrid.Columns.Add(new DataGridTextColumn { Header = "Color / variant", Binding = new Binding(nameof(WearableRecord.Variant)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        pieceGrid.Columns.Add(new DataGridTextColumn { Header = "Body slots", Binding = new Binding(nameof(WearableRecord.Slots)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var groupStyle = new GroupStyle();
        var groupHeader = new FrameworkElementFactory(typeof(TextBlock)); groupHeader.SetBinding(TextBlock.TextProperty, new Binding("Name")); groupHeader.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); groupHeader.SetValue(TextBlock.ForegroundProperty, UiTheme.Brush("AccentBrush")); groupHeader.SetValue(TextBlock.MarginProperty, new Thickness(10, 10, 10, 7));
        groupStyle.HeaderTemplate = new DataTemplate { VisualTree = groupHeader }; pieceGrid.GroupStyle.Add(groupStyle);
        pieceGrid.AddHandler(CheckBox.CheckedEvent, new RoutedEventHandler((_, _) => Dispatcher.BeginInvoke(new Action(SaveCurrent))));
        pieceGrid.AddHandler(CheckBox.UncheckedEvent, new RoutedEventHandler((_, _) => Dispatcher.BeginInvoke(new Action(SaveCurrent))));
        var pieceActions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        void SetPieces(Func<WearableRecord, bool> include) { foreach (var piece in config.Pieces) piece.Include = include(piece); pieceGrid.Items.Refresh(); SaveCurrent(); }
        var all = UiTheme.Button("All parts"); all.Click += (_, _) => SetPieces(_ => true);
        var none = UiTheme.Button("None"); none.Click += (_, _) => SetPieces(_ => false);
        var compatible = UiTheme.Button("Choose compatible parts");
        compatible.ToolTip = "Uses one color, unmarked parts, and the first compatible part for each body slot. Starts from the currently included parts, or all parts if none are included. Review the result; meshes are not inspected.";
        compatible.Click += (_, _) =>
        {
            var candidates = config.Pieces.Any(piece => piece.Include) ? config.Pieces.Where(piece => piece.Include) : config.Pieces;
            var selected = OutfitPartSelection.FindCompatibleSet(candidates).Select(piece => piece.FormId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            SetPieces(piece => selected.Contains(piece.FormId));
        };
        var excludeHoods = UiTheme.Button("Exclude hoods & wigs"); excludeHoods.Click += (_, _) => SetPieces(piece => piece.Include && !Regex.IsMatch(piece.Name + " " + piece.EditorId, @"hood|wig|hair", RegexOptions.IgnoreCase));
        pieceActions.Children.Add(all); pieceActions.Children.Add(none); pieceActions.Children.Add(compatible); pieceActions.Children.Add(excludeHoods);
        var piecePanel = new StackPanel(); piecePanel.Children.Add(UiTheme.Caption("Uncheck optional parts such as hoods or wigs. Colors are grouped below; alternate body pieces may share slots, so choose a compatible set."));
        piecePanel.Children.Add(pieceActions);
        var variants = config.Pieces.Select(piece => piece.Variant).Where(variant => variant != "Standard / unmarked").Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (variants.Count > 1)
        {
            var variantPicker = new ComboBox { ItemsSource = variants, SelectedIndex = 0, MinWidth = 200, Margin = new Thickness(0, 0, 8, 8) };
            var useColor = UiTheme.Button("Use this color + unmarked parts"); useColor.Margin = new Thickness(0, 0, 0, 8);
            useColor.Click += (_, _) => { if (variantPicker.SelectedItem is string variant) SetPieces(piece => piece.Variant == "Standard / unmarked" || piece.Variant.Equals(variant, StringComparison.OrdinalIgnoreCase)); };
            piecePanel.Children.Add(new WrapPanel { Children = { variantPicker, useColor } });
        }
        piecePanel.Children.Add(pieceGrid);
        content.Children.Add(new GroupBox { Header = "3. Outfit parts", Content = piecePanel, Margin = new Thickness(0, 0, 0, 12) });

        var tools = new StackPanel();
        var presetPicker = new ComboBox { Width = 230, ItemsSource = presets.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList(), Margin = new Thickness(0, 5, 8, 8) };
        var applyPreset = UiTheme.Button("Apply"); var savePresetButton = UiTheme.Button("Save audience preset…"); var deletePresetButton = UiTheme.Button("Delete preset");
        applyPreset.Click += (_, _) =>
        {
            if (presetPicker.SelectedItem is not string name || !presets.TryGetValue(name, out var preset)) return;
            config.TargetRules = RestoreRules(preset, out var missing);
            config.Chance = IsSkyPatcher ? "100" : preset.Chance; config.Gender = preset.Gender;
            chance.Text = config.Chance; gender.SelectedItem = config.Gender;
            feedback.Text = missing > 0 ? $"{missing} preset audience(s) are unavailable in this scan. Choose replacement audiences." : "Audience preset applied. Outfit parts are unchanged.";
            feedback.Visibility = Visibility.Visible; RefreshAudiences(); SaveCurrent();
        };
        savePresetButton.Click += (_, _) =>
        {
            CommitFields();
            if (config.TargetRules.Count == 0) { feedback.Text = "Add an audience before saving a reusable preset."; feedback.Visibility = Visibility.Visible; return; }
            var name = TextPromptWindow.Show(this, "Save audience preset", "Name this audience setup:");
            if (string.IsNullOrWhiteSpace(name)) return;
            savePreset(name.Trim(), SaveConfiguration(config, false));
            presetPicker.ItemsSource = presets.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList(); presetPicker.SelectedItem = name.Trim();
        };
        deletePresetButton.Click += (_, _) =>
        {
            if (presetPicker.SelectedItem is not string name || MessageBox.Show(this, $"Delete the reusable preset '{name}'?", "Delete preset", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            removePreset(name); presetPicker.ItemsSource = presets.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList();
        };
        tools.Children.Add(new WrapPanel { Children = { Labeled("Reusable audience setup", presetPicker), applyPreset, savePresetButton, deletePresetButton } });
        tools.Children.Add(UiTheme.Caption("Presets keep audience, sex, and chance settings. Outfit parts stay specific to this outfit."));
        var createVariant = UiTheme.Button("Create another outfit from this plugin…"); createVariant.HorizontalAlignment = HorizontalAlignment.Left;
        createVariant.Click += (_, _) =>
        {
            CommitFields(); pieceGrid.CommitEdit(DataGridEditingUnit.Cell, true); pieceGrid.CommitEdit(DataGridEditingUnit.Row, true); SaveCurrent();
            var name = TextPromptWindow.Show(this, "Create a separate outfit", "Name this outfit (for example, Dark or Archer):");
            if (string.IsNullOrWhiteSpace(name)) return;
            if (options.Any(item => item.Source.Plugin.Equals(config.Source.Plugin, StringComparison.OrdinalIgnoreCase) && item.OutfitName.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
            { feedback.Text = "That outfit name is already used in this plugin. Choose another name."; feedback.Visibility = Visibility.Visible; return; }
            var duplicate = CloneConfiguration(config, name.Trim()); var at = currentIndex + 1;
            options.Insert(at, duplicate); pages.Insert(at, BuildPage(duplicate)); ShowPage(at);
        };
        tools.Children.Add(createVariant);
        tools.Children.Add(UiTheme.Caption("Use a separate named outfit for another color or set with different parts or an audience of its own."));
        if (!config.OutfitName.Equals(Path.GetFileNameWithoutExtension(config.Source.Plugin), StringComparison.OrdinalIgnoreCase))
        {
            var removeVariant = UiTheme.Button("Remove this named outfit"); removeVariant.HorizontalAlignment = HorizontalAlignment.Left;
            removeVariant.Click += (_, _) =>
            {
                if (MessageBox.Show(this, $"Remove '{config.OutfitName}' from this patch setup?", "Remove named outfit", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                var index = pages.FindIndex(page => ReferenceEquals(page.Config, config));
                options.Remove(config); pages.RemoveAt(index); removeSavedOptions(GetSavedKey(config));
                ShowPage(Math.Clamp(index - 1, 0, pages.Count - 1));
            };
            tools.Children.Add(removeVariant);
        }
        tools.Children.Add(BuildAdvanced(config, RefreshAudiences, SaveCurrent, feedback));
        content.Children.Add(new Expander { Header = "Presets, separate outfits & advanced targets", Content = tools, Margin = new Thickness(0, 2, 0, 0) });

        RefreshGroups();
        if (config.TargetRules.FirstOrDefault() is { } initial)
        {
            if (targeting.GetCollections().Contains(initial.Collection)) collection.SelectedItem = initial.Collection;
            if (targeting.GetGroupLabels(collection.SelectedItem?.ToString() ?? NpcTargetingService.OfficialCollection).Contains(initial.Group)) group.SelectedItem = initial.Group;
        }
        RefreshAudiences();
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false, PanningMode = PanningMode.VerticalOnly };
        UiTheme.EnableScrollChaining(scroll);
        bool Validate()
        {
            pieceGrid.CommitEdit(DataGridEditingUnit.Cell, true); pieceGrid.CommitEdit(DataGridEditingUnit.Row, true); CommitFields();
            var problems = new List<string>();
            if (!int.TryParse(config.Chance, NumberStyles.Integer, CultureInfo.InvariantCulture, out var probability) || probability is < 1 or > 100) problems.Add("Enter a whole-number chance from 1 to 100.");
            if (!config.Pieces.Any(piece => piece.Include)) problems.Add("Include at least one outfit part.");
            if (config.TargetRules.Count == 0) problems.Add("Add at least one audience using the Add audience button.");
            if (sell.IsChecked == true && config.MerchantCategories.Count == 0) problems.Add("Tick at least one trader category, or untick merchandise.");
            foreach (var category in config.MerchantCategories)
                if (!merchantTargets.Any(target => target.Category == category)) problems.Add($"No confirmed {category} stock containers found. Rescan or untick that category.");
            if (hasNpcScan && config.TargetRules.Any(rule => targeting.CountMatches(rule, config.Gender) == 0)) problems.Add("One or more saved audiences match no eligible NPCs. Remove or replace those audiences.");
            feedback.Text = string.Join(" ", problems); feedback.Visibility = problems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (problems.Count > 0) scroll.ScrollToTop();
            UpdateSummary(); return problems.Count == 0;
        }
        return new PageState(config, scroll, () => { pieceGrid.CommitEdit(DataGridEditingUnit.Cell, true); pieceGrid.CommitEdit(DataGridEditingUnit.Row, true); CommitFields(); }, Validate);
    }

    private Expander BuildAdvanced(PluginPatchOptions config, Action refresh, Action save, TextBlock feedback)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        panel.Children.Add(UiTheme.Caption("Search actual installed NPC targets. Select several records with Ctrl. Records in the same category are alternatives; different categories are required together."));
        var search = new TextBox { Margin = new Thickness(0, 3, 0, 8) }; AutomationProperties.SetName(search, "Search NPC targets");
        var choices = new ListBox { Height = 185, SelectionMode = SelectionMode.Extended, DisplayMemberPath = nameof(AdvancedChoice.Label) };
        var source = config.Keywords.Select(choice => new AdvancedChoice($"{FriendlyLabel(choice)} · {choice.TargetKind} · {choice.Plugin}", choice)).OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase).ToList();
        choices.ItemsSource = source; search.TextChanged += (_, _) => choices.ItemsSource = source.Where(item => item.Label.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToList();
        var add = UiTheme.Button("Add selected targets as audience"); add.HorizontalAlignment = HorizontalAlignment.Left; add.Margin = new Thickness(0, 8, 0, 0);
        add.Click += (_, _) =>
        {
            var selected = choices.SelectedItems.Cast<AdvancedChoice>().Select(item => CloneChoice(item.Choice)).ToList();
            if (selected.Count == 0) return;
            var rule = new DistributionTargetRule { Collection = NpcTargetingService.AllCollection, Group = "Custom audience", Filters = selected };
            if (hasNpcScan && targeting.CountMatches(rule, config.Gender) == 0) { feedback.Text = "Those combined targets match no eligible NPCs. Choose another combination."; feedback.Visibility = Visibility.Visible; return; }
            if (config.TargetRules.Any(existing => RuleSignature(existing) == RuleSignature(rule))) return;
            config.TargetRules.Add(rule); feedback.Visibility = Visibility.Collapsed; refresh(); save();
        };
        panel.Children.Add(search); panel.Children.Add(choices); panel.Children.Add(add);
        return new Expander { Header = "Advanced target browser", Content = panel, Margin = new Thickness(0, 10, 0, 0) };
    }

    private List<DistributionTargetRule> RestoreRules(SavedDistributionOptions? saved, out int missing)
    {
        missing = 0; if (saved is null) return [];
        var available = availableKeywords.ToDictionary(choice => choice.TargetKind + "|" + choice.FormId, StringComparer.OrdinalIgnoreCase);
        var restored = new List<DistributionTargetRule>();
        foreach (var savedRule in saved.TargetRules ?? [])
        {
            var collection = savedRule.Collection == "All installed mods" ? NpcTargetingService.AllCollection : savedRule.Collection;
            var knownGroup = hasNpcScan && targeting.GetGroupLabels(collection).Contains(savedRule.Group, StringComparer.OrdinalIgnoreCase);
            var savedFilters = knownGroup ? savedRule.Filters.Where(filter => filter.TargetKind is "Class" or "Keyword" or "Race").ToList() : savedRule.Filters;
            var resolved = savedFilters.Select(filter => (Saved: filter, Current: available.GetValueOrDefault(filter.TargetKind + "|0x" + filter.LocalId.ToString("X6") + "~" + filter.Plugin))).ToList();
            // Dropping just one required filter can widen an audience. Restore all filters or require user repair.
            if (resolved.Any(pair => pair.Current is null)) { missing++; continue; }
            var rule = knownGroup ? targeting.BuildRule(savedRule.Group, collection) : new DistributionTargetRule { Collection = collection, Group = savedRule.Group };
            rule.Filters.AddRange(resolved.Select(pair => CloneChoice(pair.Current!, pair.Saved.Category)));
            rule.NpcResolutionComplete = false;
            if (rule.Filters.Count == 0 || hasNpcScan && targeting.CountMatches(rule, saved.Gender) == 0) { missing++; continue; }
            restored.Add(rule);
        }
        return restored;
    }

    private List<DistributionTargetRule> SuggestAudiences(string plugin)
    {
        if (!hasNpcScan) return [];
        var titleWords = Words(Path.GetFileNameWithoutExtension(plugin));
        var suggestions = targeting.GetGroupLabels(NpcTargetingService.OfficialCollection)
            .Where(group => group != "All NPCs" && Words(group).Any(word => titleWords.Contains(word) || titleWords.Contains(word.TrimEnd('s')) || titleWords.Any(titleWord => titleWord.StartsWith(word.TrimEnd('s') + "core", StringComparison.OrdinalIgnoreCase))))
            .Select(group => targeting.BuildRule(group, NpcTargetingService.OfficialCollection)).Where(rule => rule.Filters.Count > 0).Take(3).ToList();
        return suggestions;
    }

    private void RefreshSteps()
    {
        if (outfitSteps is null) return;
        changingStep = true;
        outfitSteps.Items.Clear();
        for (var i = 0; i < options.Count; i++)
        {
            var config = options[i]; var ready = config.TargetRules.Count > 0 && config.Pieces.Any(piece => piece.Include);
            var skipped = IsSkipped(config);
            var label = new StackPanel();
            label.Children.Add(new TextBlock { Text = $"{i + 1:00}   {config.OutfitName}", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            label.Children.Add(new TextBlock { Text = skipped ? "Skipped · not included this session" : ready ? $"{config.Pieces.Count(piece => piece.Include)} parts · {config.TargetRules.Count} audience(s)" : "Choose parts & audience", FontSize = 11, Foreground = UiTheme.Brush(!skipped && ready ? "AccentBrush" : "MutedBrush"), Margin = new Thickness(22, 5, 0, 0), TextWrapping = TextWrapping.Wrap });
            outfitSteps.Items.Add(new ListBoxItem { Content = label, ToolTip = config.Source.Plugin, Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 5) });
        }
        outfitSteps.SelectedIndex = Math.Clamp(currentIndex, 0, options.Count - 1); changingStep = false;
    }

    private void ShowPage(int index)
    {
        currentIndex = Math.Clamp(index, 0, pages.Count - 1); pageHost.Content = pages[currentIndex].View;
        stepLabel.Text = $"Outfit {currentIndex + 1} of {pages.Count} · {pages[currentIndex].Config.OutfitName}";
        stepProgress.Value = 100.0 * (currentIndex + 1) / pages.Count;
        backButton.IsEnabled = currentIndex > 0; nextButton.Content = currentIndex == pages.Count - 1 ? "Review patch →" : "Next outfit →";
        skipButton.Content = IsSkipped(pages[currentIndex].Config) ? "Include outfit" : "Skip outfit";
        RefreshSteps(); outfitSteps.ScrollIntoView(outfitSteps.SelectedItem);
    }

    private bool IsSkipped(PluginPatchOptions config) => skippedOutfits.Contains(GetSavedKey(config));

    private void CompleteSetup()
    {
        if (Options.Count == 0) { DialogResult = false; return; }
        for (var i = 0; i < pages.Count; i++)
        {
            if (IsSkipped(pages[i].Config)) continue;
            pages[i].Commit();
            if (pages[i].Validate()) continue;
            ShowPage(i); return;
        }
        DialogResult = true;
    }

    private void CommitCurrentPage() { if (pages.Count > 0) { pages[currentIndex].Commit(); SavePage(pages[currentIndex].Config); } }
    private void SaveAllPages() { foreach (var page in pages) { page.Commit(); SavePage(page.Config); } }
    private void SavePage(PluginPatchOptions config) => saveOptions(GetSavedKey(config), SaveConfiguration(config, true));
    private static SavedDistributionOptions SaveConfiguration(PluginPatchOptions config, bool includePieces) => new()
    {
        OutfitName = config.OutfitName, Chance = config.Chance, Gender = config.Gender,
        MerchantCategories = config.MerchantCategories.ToList(), IncludeMerchandiseWeapons = config.IncludeMerchandiseWeapons,
        ExcludedPieceFormIds = includePieces ? config.Pieces.Where(piece => !piece.Include).Select(piece => piece.FormId).ToList() : [],
        TargetRules = config.TargetRules.Select(rule => new SavedDistributionTargetRule
        {
            Collection = rule.Collection, Group = rule.Group,
            Filters = rule.Filters.Select(choice => new SavedTargetChoice { EditorId = choice.EditorId, Plugin = choice.Plugin, Category = choice.Category, TargetKind = choice.TargetKind, LocalId = choice.LocalId }).ToList()
        }).ToList()
    };

    private static string GetSavedKey(PluginPatchOptions config) => config.OutfitName.Equals(Path.GetFileNameWithoutExtension(config.Source.Plugin), StringComparison.OrdinalIgnoreCase) ? config.Source.Plugin : config.Source.Plugin + "|outfit|" + config.OutfitName;
    private static WearableRecord ClonePiece(WearableRecord piece, bool include) => new() { Plugin = piece.Plugin, LocalId = piece.LocalId, EditorId = piece.EditorId, Name = piece.Name, Slots = piece.Slots, Category = piece.Category, SourcePath = piece.SourcePath, Include = include };
    private static KeywordChoice CloneChoice(KeywordChoice choice, string? category = null) => new() { EditorId = choice.EditorId, Plugin = choice.Plugin, Category = category ?? choice.Category, TargetKind = choice.TargetKind, LocalId = choice.LocalId, Selected = choice.Selected, SuggestionReason = choice.SuggestionReason };
    private static PluginPatchOptions CloneConfiguration(PluginPatchOptions source, string name) => new() { Source = source.Source, OutfitName = name, Chance = source.Chance, Gender = source.Gender, MerchantCategories = source.MerchantCategories.ToList(), IncludeMerchandiseWeapons = source.IncludeMerchandiseWeapons, MerchantTargets = source.MerchantTargets.ToList(), Pieces = source.Pieces.Select(piece => ClonePiece(piece, piece.Include)).ToList(), Keywords = source.Keywords, TargetRules = source.TargetRules.Select(rule => new DistributionTargetRule { Collection = rule.Collection, Group = rule.Group, Filters = rule.Filters.Select(choice => CloneChoice(choice)).ToList() }).ToList() };
    private static string AudienceDetails(DistributionTargetRule rule)
    {
        var refinements = rule.Filters.Where(choice => choice.TargetKind != "Npc").Select(FriendlyLabel).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return rule.Collection + (refinements.Count > 0 ? " · " + string.Join(", ", refinements.Take(8)) + (refinements.Count > 8 ? ", …" : "") : " · All roles & races");
    }
    private static string RuleSignature(DistributionTargetRule rule) => rule.Collection.ToUpperInvariant() + "|" + string.Join(";", rule.Filters.Select(choice => choice.Category.ToUpperInvariant() + ":" + choice.FormId.ToUpperInvariant()).Order(StringComparer.Ordinal));
    private static string FriendlyLabel(KeywordChoice choice) => NpcTargetingService.FriendlyLabel(choice);
    private static HashSet<string> Words(string text) => Regex.Matches(Regex.Replace(text, "([a-z0-9])([A-Z])", "$1 $2"), "[A-Za-z]+").Select(match => match.Value.ToLowerInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static FrameworkElement Labeled(string text, FrameworkElement control)
    {
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        panel.Children.Add(new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = UiTheme.Brush("MutedBrush") }); panel.Children.Add(control);
        AutomationProperties.SetName(control, text); return panel;
    }
    private static ListBox CheckList(ObservableCollection<SelectableChoice> choices)
    {
        var list = new ListBox { ItemsSource = choices, MaxHeight = 150, MinHeight = 60, BorderThickness = new Thickness(0), Padding = new Thickness(0) };
        var check = new FrameworkElementFactory(typeof(CheckBox)); check.SetBinding(ContentControl.ContentProperty, new Binding(nameof(SelectableChoice.Label))); check.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(SelectableChoice.Details))); check.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(SelectableChoice.Selected)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        list.ItemTemplate = new DataTemplate { VisualTree = check };
        return list;
    }
    private static void FillChecks(ObservableCollection<SelectableChoice> target, IEnumerable<KeywordChoice> choices, Action changed)
    {
        var selected = target.Where(item => item.Selected).SelectMany(item => item.Choices).Select(item => item.FormId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        target.Clear();
        foreach (var group in choices.DistinctBy(choice => choice.FormId, StringComparer.OrdinalIgnoreCase).GroupBy(FriendlyLabel, StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var grouped = group.Select(choice => CloneChoice(choice)).ToList();
            var item = new SelectableChoice(grouped, group.Key, grouped.Any(choice => selected.Contains(choice.FormId)));
            item.PropertyChanged += (_, _) => changed(); target.Add(item);
        }
    }

    private sealed record PageState(PluginPatchOptions Config, FrameworkElement View, Action Commit, Func<bool> Validate);
    private sealed record AudienceRow(string Group, string Details, string Matches, DistributionTargetRule Rule);
    private sealed record AdvancedChoice(string Label, KeywordChoice Choice);
    private sealed class SelectableChoice(List<KeywordChoice> choices, string label, bool selected) : INotifyPropertyChanged
    {
        private bool isSelected = selected;
        public List<KeywordChoice> Choices { get; } = choices;
        public string Label { get; } = label;
        public string Details => string.Join("\n", Choices.Select(choice => choice.EditorId + " · " + choice.Plugin));
        public bool Selected { get => isSelected; set { if (isSelected == value) return; isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
