using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace WearableDistributor;

/// <summary>Explicitly opt selected outfits into one shared audience and one coverage roll.</summary>
public sealed class WardrobePoolWindow : Window
{
    private sealed class Member : System.ComponentModel.INotifyPropertyChanged
    {
        public PluginPatchOptions Config { get; init; } = null!;
        private bool include;
        private int weight = 1;
        private string share = "—";
        public bool Include { get => include; set { if (include == value) return; include = value; PropertyChanged?.Invoke(this, new(nameof(Include))); } }
        public int Weight { get => weight; set { if (weight == value) return; weight = value; PropertyChanged?.Invoke(this, new(nameof(Weight))); } }
        public string Share { get => share; set { share = value; PropertyChanged?.Invoke(this, new(nameof(Share))); } }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public string Outfit => Config.OutfitName;
        public string Plugin => Config.Source.Plugin;
        public string CurrentPool => string.IsNullOrEmpty(Config.WardrobeName) ? "Individual outfit" : Config.WardrobeName;
    }
    public WardrobePoolWindow(IReadOnlyList<PluginPatchOptions> options, PluginPatchOptions current)
    {
        UiTheme.Apply(this); Title = Loc.T("Random outfit pools");
        Width = 1000; Height = 740; MinWidth = 850; MinHeight = 580; MaxHeight = Math.Max(580, SystemParameters.WorkArea.Height);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = Loc.T("One wardrobe. One outfit per NPC."), FontSize = 24, FontWeight = FontWeights.SemiBold });
        header.Children.Add(UiTheme.Caption(Loc.T("Choose 2–20 outfits. A single overall chance decides whether an eligible NPC receives a wardrobe outfit. Weights decide which complete set they get. Equal weights give equal chances, not exact NPC counts.")));
        header.Children.Add(UiTheme.Caption(Loc.T("Each set keeps its own matching colors and optional parts. This does not schedule daily clothes changes. Other installed outfit distributors can still compete.")));
        var name = new TextBox { Text = current.WardrobeName, MaxLength = 80, MinWidth = 240, Margin = new Thickness(0, 4, 12, 8) };
        AutomationProperties.SetName(name,Loc.T("Wardrobe name"));
        var existing = new ComboBox { MinWidth = 210, ItemsSource = options.Select(c => c.WardrobeName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).Prepend("New wardrobe…").ToList(), SelectedIndex = 0, Margin = new Thickness(0, 4, 12, 8) };
        AutomationProperties.SetName(existing,Loc.T("Existing wardrobe"));
        if (!string.IsNullOrEmpty(current.WardrobeName)) existing.SelectedItem = existing.Items.Cast<string>().FirstOrDefault(n => n.Equals(current.WardrobeName, StringComparison.OrdinalIgnoreCase));
        var audienceChoices = options.Select(c => new AudienceSource(c)).ToList();
        var source = new ComboBox { ItemsSource = audienceChoices, SelectedItem = audienceChoices.First(c => ReferenceEquals(c.Config, current)), DisplayMemberPath = nameof(AudienceSource.Label), MinWidth = 320, Margin = new Thickness(0, 4, 12, 8) };
        AutomationProperties.SetName(source,Loc.T("Use NPC groups and sex from"));
        var chance = new ComboBox { IsEditable = true, ItemsSource = new[] { "100", "80", "75", "50", "25", "10", "5" }, Text = current.Chance, Width = 130, Margin = new Thickness(0, 4, 12, 8) };
        AutomationProperties.SetName(chance,Loc.T("Wardrobe overall chance percent"));
        var inputs = new WrapPanel(); inputs.Children.Add(Labeled(Loc.T("Wardrobe name"),name)); inputs.Children.Add(Labeled(Loc.T("Load an existing pool"),existing));
        header.Children.Add(inputs);
        var targets = new WrapPanel(); targets.Children.Add(Labeled(Loc.T("Use NPC groups and sex from"),source)); targets.Children.Add(Labeled(Loc.T("Overall chance (%)"),chance)); header.Children.Add(targets);
        header.Children.Add(UiTheme.Caption(Loc.T("Applying copies the chosen NPC groups, sex and overall chance to all checked outfits. Parts and trader stock are preserved. Only outfits opened in this setup are listed; select existing pool members together to change its audience.")));
        root.Children.Add(header);
        var rows = options.Select(c => new Member { Config = c, Include = string.IsNullOrEmpty(current.WardrobeName) ? ReferenceEquals(c, current) : c.WardrobeName.Equals(current.WardrobeName, StringComparison.OrdinalIgnoreCase), Weight = c.WardrobeWeight }).ToList();
        var grid = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, SelectionMode = DataGridSelectionMode.Extended, Margin = new Thickness(0, 12, 0, 12), CanUserAddRows = false };
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = Loc.T("Include"), Binding = new Binding(nameof(Member.Include)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 65 });
        grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("Outfit"), Binding = new Binding(nameof(Member.Outfit)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("Plugin"), Binding = new Binding(nameof(Member.Plugin)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridComboBoxColumn { Header = Loc.T("Weight (1–10)"), ItemsSource = Enumerable.Range(1, 10).ToArray(), SelectedItemBinding = new Binding(nameof(Member.Weight)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 115 });
        grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("Share in pool"), Binding = new Binding(nameof(Member.Share)), IsReadOnly = true, Width = 100 });
        grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("Current pool"), Binding = new Binding(nameof(Member.CurrentPool)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        AutomationProperties.SetName(grid,Loc.T("Outfits available for this wardrobe"));
        Grid.SetRow(grid, 1); root.Children.Add(grid);
        void Commit() { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); }
        existing.SelectionChanged += (_, _) =>
        {
            if (existing.SelectedIndex <= 0 || existing.SelectedItem is not string chosen) return;
            Commit(); name.Text = chosen;
            foreach (var row in rows) row.Include = row.Config.WardrobeName.Equals(chosen, StringComparison.OrdinalIgnoreCase);
            var first = rows.First(r => r.Include).Config; source.SelectedItem = audienceChoices.First(a => ReferenceEquals(a.Config, first)); chance.Text = first.Chance; grid.Items.Refresh();
        };
        var footer = new StackPanel();
        var feedback = UiTheme.Caption(""); feedback.Foreground = UiTheme.Brush("WarningBrush"); feedback.Visibility = Visibility.Collapsed; footer.Children.Add(feedback);
        var estimate = UiTheme.Caption(Loc.T("Weight 2 is twice as likely as weight 1. Example: three outfits at weight 1 → about 33% each after the overall roll.")); footer.Children.Add(estimate);
        void RefreshShares()
        {
            var total = rows.Where(r => r.Include).Sum(r => r.Weight);
            foreach (var row in rows) row.Share = row.Include && total > 0 ? (100.0 * row.Weight / total).ToString("0.#", CultureInfo.InvariantCulture) + "%" : "—";
        }
        foreach (var row in rows) row.PropertyChanged += (_, change) => { if (change.PropertyName is nameof(Member.Include) or nameof(Member.Weight)) RefreshShares(); };
        RefreshShares();
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var all = UiTheme.Button(Loc.T("Select all")); all.Click += (_, _) => { Commit(); foreach (var row in rows) row.Include = true; grid.Items.Refresh(); };
        var none = UiTheme.Button(Loc.T("None")); none.Click += (_, _) => { Commit(); foreach (var row in rows) row.Include = false; grid.Items.Refresh(); };
        var remove = UiTheme.Button(Loc.T("Remove checked from pools")); remove.Click += (_, _) =>
        {
            Commit(); var selected = rows.Where(r => r.Include).ToList();
            if (selected.Count == 0) { feedback.Text = Loc.T("Check the outfits to remove from their wardrobes."); feedback.Visibility = Visibility.Visible; return; }
            foreach (var row in selected) { row.Config.WardrobeName = ""; row.Config.WardrobeWeight = 1; }
            ActivityHistory.Add($"Removed {selected.Count} outfit(s) from random pools. Their NPC groups and individual chances are retained."); DialogResult = true;
        };
        var cancel = UiTheme.Button(Loc.T("Cancel")); cancel.IsCancel = true; cancel.Click += (_, _) => DialogResult = false;
        var apply = UiTheme.Button(Loc.T("Apply wardrobe"),true); apply.IsDefault = true;
        apply.Click += (_, _) =>
        {
            Commit();
            try
            {
                var label = WardrobePools.NormalizeName(name.Text); var selected = rows.Where(r => r.Include).ToList();
                if (string.IsNullOrEmpty(label)) throw new InvalidOperationException("Give this wardrobe a name, such as ‘Wenches wardrobe’.");
                if (selected.Count is < 2 or > WardrobePools.MaximumMembers) throw new InvalidOperationException($"Check between 2 and {WardrobePools.MaximumMembers} outfits for this wardrobe.");
                if (!int.TryParse(chance.Text.Trim().TrimEnd('%'), NumberStyles.None, CultureInfo.InvariantCulture, out var probability) || probability is < 1 or > 100) throw new InvalidOperationException("Enter an overall chance from 1 to 100.");
                var seed = ((AudienceSource)source.SelectedItem).Config;
                if (!seed.DistributeToNpcs || seed.TargetRules.Count == 0 || seed.TargetRules.Any(r => !r.NpcResolutionComplete || r.ResolvedNpcFormIds.Count == 0))
                    throw new InvalidOperationException("First enable NPC distribution and add a matching NPC group to the chosen source outfit. Then reopen Random outfit pools.");
                foreach (var row in selected)
                {
                    var required = OutfitGenderPolicy.RequiredGender(row.Config.Pieces);
                    if (required == "Conflict" || required != "Any" && required != seed.Gender) throw new InvalidOperationException($"{row.Outfit} has {required}-only or incompatible parts. Choose a source audience with the matching NPC sex, or exclude this outfit from the wardrobe.");
                    if (!row.Config.Pieces.Any(p => p.Include)) throw new InvalidOperationException($"Choose parts for {row.Outfit} before adding it to a wardrobe.");
                }
                // Clone before mutating the source itself. Unchecked members of this named pool leave it.
                var shared = WardrobePools.CopyTargets(seed.TargetRules); var sex = seed.Gender;
                foreach (var row in rows.Where(r => !r.Include && r.Config.WardrobeName.Equals(label, StringComparison.OrdinalIgnoreCase))) { row.Config.WardrobeName = ""; row.Config.WardrobeWeight = 1; }
                foreach (var row in selected)
                {
                    row.Config.WardrobeName = label; row.Config.WardrobeWeight = row.Weight; row.Config.DistributeToNpcs = true;
                    row.Config.Gender = sex; row.Config.Chance = probability.ToString(CultureInfo.InvariantCulture); row.Config.TargetRules = WardrobePools.CopyTargets(shared);
                }
                ActivityHistory.Add($"Applied random wardrobe ‘{label}’: {selected.Count} outfits · {probability}% coverage · one complete weighted set per eligible NPC."); DialogResult = true;
            }
            catch (InvalidOperationException error) { feedback.Text = error.Message; feedback.Visibility = Visibility.Visible; }
        };
        foreach (var button in new[] { all, none, remove, cancel, apply }) actions.Children.Add(button);
        footer.Children.Add(actions); Grid.SetRow(footer, 2); root.Children.Add(footer); Content = root;
    }
    private sealed record AudienceSource(PluginPatchOptions Config)
    {
        public string Label => Config.OutfitName + " · " + Loc.T(Config.Gender) + " · " + Loc.F($"{Config.TargetRules.Count} NPC group(s)");
    }
    private static FrameworkElement Labeled(string text, FrameworkElement control)
    {
        var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = text, Foreground = UiTheme.Brush("MutedBrush"), FontSize = 12 }); panel.Children.Add(control); return panel;
    }
}
