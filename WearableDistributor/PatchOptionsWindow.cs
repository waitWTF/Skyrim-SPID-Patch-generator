using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace WearableDistributor;

public sealed class PatchOptionsWindow : Window
{
    private readonly List<PluginPatchOptions> options = [];
    private readonly List<PageState> pages = [];
    private readonly IReadOnlyList<KeywordChoice> availableKeywords;
    private readonly string defaultChance;
    private readonly string framework;
    private int currentIndex;
    private TextBlock stepLabel = null!;
    private ProgressBar stepProgress = null!;
    private ContentControl pageHost = null!;
    private Button backButton = null!;
    private Button nextButton = null!;

    public IReadOnlyList<PluginPatchOptions> Options => options;

    public PatchOptionsWindow(IEnumerable<PluginEntry> plugins, IEnumerable<KeywordChoice> keywords, string defaultChance, string framework)
    {
        this.availableKeywords = keywords.ToList();
        this.defaultChance = defaultChance;
        this.framework = framework;
        foreach (var plugin in plugins)
        {
            var orderedKeywords = this.availableKeywords
                .OrderBy(keyword => keyword.Plugin, StringComparer.OrdinalIgnoreCase)
                .ThenBy(keyword => keyword.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(keyword => keyword.EditorId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var config = new PluginPatchOptions
            {
                Source = plugin,
                Chance = defaultChance,
                Pieces = plugin.Pieces.Select(piece => new WearableRecord
                {
                    Plugin = piece.Plugin, LocalId = piece.LocalId, EditorId = piece.EditorId, Name = piece.Name,
                    Slots = piece.Slots, SourcePath = piece.SourcePath, Include = true
                }).ToList(),
                Keywords = orderedKeywords.Select(keyword => new KeywordChoice
                {
                    EditorId = keyword.EditorId, Plugin = keyword.Plugin, Category = keyword.Category,
                    TargetKind = keyword.TargetKind, LocalId = keyword.LocalId,
                    Selected = keyword.EditorId.Equals("ActorTypeNPC", StringComparison.OrdinalIgnoreCase)
                }).ToList()
            };
            options.Add(config);
            pages.Add(BuildPage(config));
        }
        BuildWindow();
        ShowPage(0);
    }

    private void BuildWindow()
    {
        Title = $"{framework} Distribution Setup";
        Width = 1100; Height = 780; MinWidth = 900; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(243, 245, 242));

        var root = new DockPanel { Margin = new Thickness(18) };
        var banner = new Border { Background = new SolidColorBrush(Color.FromRgb(35, 54, 45)), CornerRadius = new CornerRadius(10), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 12) };
        var bannerText = new StackPanel();
        bannerText.Children.Add(new TextBlock { Text = $"{framework} Distribution Setup", Foreground = Brushes.White, FontSize = 21, FontWeight = FontWeights.SemiBold });
        bannerText.Children.Add(new TextBlock { Text = "Configure one plugin at a time. Choose NPC filters, distribution chance, and the pieces to include.", Foreground = new SolidColorBrush(Color.FromRgb(216, 228, 220)), Margin = new Thickness(0, 4, 0, 0) });
        banner.Child = bannerText; DockPanel.SetDock(banner, Dock.Top); root.Children.Add(banner);

        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var footerLeft = new StackPanel();
        stepLabel = new TextBlock { FontWeight = FontWeights.SemiBold };
        stepProgress = new ProgressBar { Height = 8, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 6, 20, 0) };
        footerLeft.Children.Add(stepLabel); footerLeft.Children.Add(stepProgress); footer.Children.Add(footerLeft);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, Margin = new Thickness(4), Padding = new Thickness(12, 8, 12, 8) };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        backButton = new Button { Content = "< Back", MinWidth = 90, Margin = new Thickness(4), Padding = new Thickness(12, 8, 12, 8) };
        backButton.Click += (_, _) => { CommitCurrentPage(); ShowPage(currentIndex - 1); };
        nextButton = new Button { Content = "Next >", MinWidth = 105, Margin = new Thickness(4), Padding = new Thickness(12, 8, 12, 8), IsDefault = true };
        nextButton.Click += (_, _) =>
        {
            CommitCurrentPage();
            if (currentIndex == pages.Count - 1) { DialogResult = true; Close(); }
            else ShowPage(currentIndex + 1);
        };
        buttons.Children.Add(cancel); buttons.Children.Add(backButton); buttons.Children.Add(nextButton);
        Grid.SetColumn(buttons, 1); footer.Children.Add(buttons);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);

        pageHost = new ContentControl(); root.Children.Add(pageHost);
        Content = root;
    }

    private PageState BuildPage(PluginPatchOptions config)
    {
        var page = new DockPanel { Margin = new Thickness(8) };
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        top.Children.Add(new TextBlock { Text = config.Source.Plugin, FontSize = 19, FontWeight = FontWeights.SemiBold });
        top.Children.Add(new TextBlock { Text = $"{config.Pieces.Count} wearable records found. Add one or more NPC groups for this plugin. Pick a group type first, then choose its specific faction, occupation, role, keyword, class, or race.", Foreground = Brushes.DimGray, Margin = new Thickness(0, 4, 0, 8), TextWrapping = TextWrapping.Wrap });

        var settings = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var chance = new ComboBox { IsEditable = true, Text = config.Chance, Width = 170, Margin = new Thickness(0, 2, 12, 2), ItemsSource = new[] { "100", "80", "50", "25", "10", "5" } };
        var gender = new ComboBox { Width = 150, Margin = new Thickness(0, 2, 12, 2), ItemsSource = new[] { "Any", "Female", "Male" }, SelectedItem = config.Gender };
        settings.Children.Add(Labeled("Distribution chance (%)", chance));
        settings.Children.Add(Labeled("NPC sex", gender));
        top.Children.Add(settings);

        var targetCategory = new ComboBox { Width = 220, Margin = new Thickness(0, 2, 12, 2), ItemsSource = config.Keywords.Select(item => item.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList() };
        var targetChoice = new ComboBox { Width = 440, Margin = new Thickness(0, 2, 12, 2), DisplayMemberPath = nameof(KeywordChoice.EditorId) };
        var targetSearch = new TextBox { Width = 190, Padding = new Thickness(7), Margin = new Thickness(0, 2, 8, 2), ToolTip = "Search group names and plugin names, e.g. JobFarmer, Necromancer, Bandit, or Adventurer" };
        var addTarget = new Button { Content = "Add group", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 2, 0, 2) };
        var targets = new ObservableCollection<KeywordChoice>(config.Keywords.Where(item => item.Selected));
        var targetList = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, Height = 175, ItemsSource = targets, HeadersVisibility = DataGridHeadersVisibility.Column, IsReadOnly = true };
        targetList.Columns.Add(new DataGridTextColumn { Header = "Group type", Binding = new Binding(nameof(KeywordChoice.Category)), Width = 150 });
        targetList.Columns.Add(new DataGridTextColumn { Header = "Faction / occupation / role", Binding = new Binding(nameof(KeywordChoice.EditorId)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        targetList.Columns.Add(new DataGridTextColumn { Header = "Plugin", Binding = new Binding(nameof(KeywordChoice.Plugin)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var removeTarget = new Button { Content = "Remove selected group", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 6, 0, 0) };
        removeTarget.Click += (_, _) => { if (targetList.SelectedItem is KeywordChoice selected) { targets.Remove(selected); selected.Selected = false; } };
        void RefreshTargetChoices()
        {
            var category = targetCategory.SelectedItem?.ToString() ?? "";
            var query = targetSearch.Text.Trim();
            targetChoice.ItemsSource = config.Keywords.Where(item => item.Category.Equals(category, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(query) || item.EditorId.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Plugin.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(item => item.Plugin, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.EditorId, StringComparer.OrdinalIgnoreCase).ToList();
            targetChoice.SelectedIndex = targetChoice.Items.Count > 0 ? 0 : -1;
        }
        targetCategory.SelectionChanged += (_, _) => RefreshTargetChoices();
        targetSearch.TextChanged += (_, _) => RefreshTargetChoices();
        addTarget.Click += (_, _) =>
        {
            if (targetChoice.SelectedItem is not KeywordChoice selected) return;
            var existing = targets.FirstOrDefault(item => item.FormId.Equals(selected.FormId, StringComparison.OrdinalIgnoreCase) && item.TargetKind == selected.TargetKind);
            if (existing is null) { selected.Selected = true; targets.Add(selected); }
        };
        if (targetCategory.Items.Count > 0) targetCategory.SelectedIndex = 0;
        var targetSelectors = new WrapPanel();
        targetSelectors.Children.Add(Labeled("Group type", targetCategory));
        targetSelectors.Children.Add(Labeled("Specific group", targetChoice));
        targetSelectors.Children.Add(Labeled("Search", targetSearch));
        targetSelectors.Children.Add(addTarget);
        var targetPanel = new DockPanel();
        DockPanel.SetDock(targetSelectors, Dock.Top); targetPanel.Children.Add(targetSelectors);
        DockPanel.SetDock(removeTarget, Dock.Bottom); targetPanel.Children.Add(removeTarget);
        targetPanel.Children.Add(targetList);
        var keywordBox = new GroupBox { Header = "Who should wear this? — add NPC groups for this plugin", Padding = new Thickness(8), Margin = new Thickness(0, 4, 0, 10) };
        keywordBox.Content = targetPanel;
        top.Children.Add(keywordBox);
        DockPanel.SetDock(top, Dock.Top); page.Children.Add(top);

        var pieceView = CollectionViewSource.GetDefaultView(config.Pieces);
        pieceView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(WearableRecord.Variant)));
        var pieceGrid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, ItemsSource = pieceView, HeadersVisibility = DataGridHeadersVisibility.Column };
        pieceGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "Include", Binding = new Binding(nameof(WearableRecord.Include)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 70 });
        pieceGrid.Columns.Add(new DataGridTextColumn { Header = "Outfit piece", Binding = new Binding(nameof(WearableRecord.Name)), Width = new DataGridLength(2, DataGridLengthUnitType.Star), IsReadOnly = true });
        pieceGrid.Columns.Add(new DataGridTextColumn { Header = "Color / variant", Binding = new Binding(nameof(WearableRecord.Variant)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });
        pieceGrid.Columns.Add(new DataGridTextColumn { Header = "Slots", Binding = new Binding(nameof(WearableRecord.Slots)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });
        var piecesActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        var selectPieces = new Button { Content = "Select all pieces", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 0) };
        selectPieces.Click += (_, _) => { foreach (var piece in config.Pieces) piece.Include = true; pieceGrid.Items.Refresh(); };
        var clearPieces = new Button { Content = "Clear all pieces", Padding = new Thickness(10, 5, 10, 5) };
        clearPieces.Click += (_, _) => { foreach (var piece in config.Pieces) piece.Include = false; pieceGrid.Items.Refresh(); };
        piecesActions.Children.Add(selectPieces); piecesActions.Children.Add(clearPieces);
        var piecesPanel = new DockPanel(); DockPanel.SetDock(piecesActions, Dock.Top); piecesPanel.Children.Add(piecesActions); piecesPanel.Children.Add(pieceGrid);
        var piecesBox = new GroupBox { Header = "Which outfit parts should be distributed? Uncheck wigs, hoods, or other pieces to skip.", Padding = new Thickness(8) };
        piecesBox.Content = piecesPanel;
        page.Children.Add(piecesBox);

        return new PageState(config, page, () =>
        {
            config.Chance = chance.Text.Trim().TrimEnd('%');
            config.Gender = gender.SelectedItem?.ToString() ?? "Any";
            config.Keywords = targets.ToList();
        });
    }

    private static FrameworkElement Labeled(string label, FrameworkElement control)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(control);
        return panel;
    }

    private void ShowPage(int index)
    {
        currentIndex = Math.Clamp(index, 0, pages.Count - 1);
        pageHost.Content = pages[currentIndex].View;
        stepLabel.Text = $"Plugin {currentIndex + 1} of {pages.Count}: {pages[currentIndex].Config.Source.Plugin}";
        stepProgress.Value = 100.0 * (currentIndex + 1) / pages.Count;
        backButton.IsEnabled = currentIndex > 0;
        nextButton.Content = currentIndex == pages.Count - 1 ? "Generate Patch" : "Next >";
    }

    private void CommitCurrentPage() => pages[currentIndex].Commit();

    private sealed record PageState(PluginPatchOptions Config, FrameworkElement View, Action Commit);
}
