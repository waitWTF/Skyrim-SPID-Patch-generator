using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace WearableDistributor;

internal sealed class OutfitPartsEditor : StackPanel
{
    public DataGrid Grid { get; }
    private readonly PluginPatchOptions config;
    private readonly Action save;
    private readonly TextBlock summary = UiTheme.Caption("");
    private readonly ValidationNotice notes = new();
    private bool updating;
    private CheckBox? variants;

    public OutfitPartsEditor(PluginPatchOptions config, Action save)
    {
        this.config = config; this.save = save;
        Children.Add(UiTheme.Caption(Loc.T("Choose a preset, then adjust individual parts. Required = one version guaranteed; Optional = chance after the outfit roll; Excluded = never included. Unticking Required makes a part optional. Use Excluded to remove it entirely.")));
        Children.Add(UiTheme.Caption(Loc.T("Trader stock includes one of each Required or Optional part. Optional percentages apply only to NPC outfits; they do not change shop stock.")));
        var presets = new WrapPanel { Margin = new Thickness(0, 10, 0, 4) };
        foreach (var name in OutfitPartPresets.Names)
        {
            var button = UiTheme.Button(name); button.ToolTip = name == "Complete set" ? Loc.T("Matching color sets with one version of each garment guaranteed. Wigs and marked lewd alternatives stay excluded.") : Loc.T("Main and uncertain clothing stay required. Recognizable accessories get the preset's inclusion chance. Wigs and marked lewd alternatives stay excluded.");
            button.Click += (_, _) => Change(() => OutfitPartPresets.Apply(config, name)); presets.Children.Add(button);
        }
        var custom = UiTheme.Button(Loc.T("Custom")); custom.Click += (_, _) => Change(() => config.PartPreset = "Custom"); presets.Children.Add(custom);
        Children.Add(presets);
        var modifiers = new WrapPanel { Margin = new Thickness(0, 0, 0, 7) };
        void Add(string label, Action action) { var b = UiTheme.Button(label); b.Click += (_, _) => Change(action); modifiers.Children.Add(b); }
        Add("No headgear", () => { foreach (var p in config.Pieces.Where(OutfitPartPresets.IsHeadgear)) p.PartMode = "Excluded"; config.PartPreset = "Custom · no headgear"; });
        Add("No wigs", () => { foreach (var p in config.Pieces.Where(OutfitPartPresets.IsWig)) p.PartMode = "Excluded"; config.PartPreset = "Custom · no wigs"; });
        Add("Matching colors", () => OutfitPartPresets.Apply(config, OutfitPartPresets.Names.Contains(config.PartPreset) ? config.PartPreset : "Balanced"));
        Add("Reset to Balanced", () => OutfitPartPresets.Apply(config, "Balanced"));
        var lewdCount = config.Pieces.Count(p => p.IsLewd);
        if (lewdCount > 0)
        {
            var explicitButton = UiTheme.Button(Loc.F($"Use lewd variant ({lewdCount})"));
            explicitButton.ToolTip = Loc.T("Explicit opt-in for marked lewd alternatives in this outfit. Normal presets and Reset never select these pieces.");
            explicitButton.Click += (_, _) =>
            {
                if (MessageBox.Show(Window.GetWindow(this),Loc.T("Use the marked lewd alternative for this outfit? This selects one compatible set with preference for those pieces. Review the parts; detection uses names, not meshes."),Loc.T("Explicit lewd variant"),MessageBoxButton.YesNo,MessageBoxImage.Question) == MessageBoxResult.Yes)
                    Change(() => OutfitPartPresets.Apply(config, "Balanced", true));
            };
            modifiers.Children.Add(explicitButton);
        }
        Children.Add(modifiers);
        var colors = config.Pieces.Select(p => p.Variant).Where(v => v != "Standard / unmarked").Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (colors.Count > 0)
        {
            const string automatic = "All matching colors (random)";
            var colorPicker = new ComboBox { ItemsSource = new[] { automatic }.Concat(colors).ToList(), SelectedItem = colors.Contains(config.PreferredColor) ? config.PreferredColor : automatic, MinWidth = 180, Margin = new Thickness(6, 0, 8, 0) };
            AutomationProperties.SetName(colorPicker,Loc.T("Preferred outfit color"));
            var useColor = UiTheme.Button(Loc.T("Use selected color"));
            useColor.Click += (_, _) => Change(() =>
            {
                config.PreferredColor = colorPicker.SelectedItem is string c && c != automatic ? c : "";
                OutfitPartPresets.Apply(config, OutfitPartPresets.Names.Contains(config.PartPreset) ? config.PartPreset : "Balanced");
            });
            Children.Add(new WrapPanel { Children = { UiTheme.Caption(Loc.T("Matching color")), colorPicker, useColor } });
        }
        var bulkChance = new ComboBox { Width = 85, ItemsSource = new[] { 5, 10, 25, 40, 50, 75, 90 }, SelectedItem = 50, Margin = new Thickness(6, 0, 8, 0) };
        AutomationProperties.SetName(bulkChance,Loc.T("Chance for all optional parts"));
        var setChance = UiTheme.Button(Loc.T("Set all optional chances"));
        setChance.Click += (_, _) => { if (bulkChance.SelectedItem is int value) Change(() => { foreach (var p in config.Pieces.Where(p => p.PartMode == "Optional")) p.OptionalChance = value; config.PartPreset = "Custom"; }); };
        Children.Add(new WrapPanel { Children = { UiTheme.Caption(Loc.T("Optional inclusion %")), bulkChance, setChance } });
        variants = new CheckBox { Content = Loc.T("Choose one version per garment and keep colors matching"), IsChecked = config.MatchVariants, Margin = new Thickness(0, 5, 0, 6) };
        variants.Checked += (_, _) => { if (!updating) Change(() => config.MatchVariants = true); };
        variants.Unchecked += (_, _) => { if (!updating) Change(() => config.MatchVariants = false); };
        AutomationProperties.SetName(variants,Loc.T("Random matching garment alternatives"));
        Children.Add(variants);
        Children.Add(UiTheme.Caption(Loc.T("Matching variants: Skyrim picks a color once, then one version of each garment (for example normal or torn). Required alternatives are a guaranteed group, not several items at once. Shared accessories use their Optional %; recognized belts default to optional.")));
        Children.Add(summary); Children.Add(notes);
        Grid = new DataGrid { AutoGenerateColumns = false, ItemsSource = config.Pieces, Height = 340, MinHeight = 170, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single };
        var required = new FrameworkElementFactory(typeof(CheckBox)); required.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(WearableRecord.IsRequired)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        required.SetValue(AutomationProperties.NameProperty, "Required outfit part"); required.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        Grid.Columns.Add(new DataGridTemplateColumn { Header = Loc.T("Required"), CellTemplate = new DataTemplate { VisualTree = required }, Width = 100 });
        var mode = new FrameworkElementFactory(typeof(ComboBox)); mode.SetValue(ItemsControl.ItemsSourceProperty, WearableRecord.PartModes); mode.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(WearableRecord.PartMode)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        mode.SetValue(ItemsControl.ItemTemplateProperty, Loc.ChoiceTemplate());
        mode.SetValue(AutomationProperties.NameProperty, "Part inclusion mode");
        Grid.Columns.Add(new DataGridTemplateColumn { Header = Loc.T("State"), CellTemplate = new DataTemplate { VisualTree = mode }, Width = 140 });
        var probability = new FrameworkElementFactory(typeof(ComboBox)); probability.SetValue(ItemsControl.ItemsSourceProperty, Enumerable.Range(1, 99).ToList()); probability.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(WearableRecord.OptionalChance)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        probability.SetBinding(IsEnabledProperty, new Binding(nameof(WearableRecord.IsOptionalPart))); probability.SetValue(AutomationProperties.NameProperty, "Optional inclusion percentage");
        Grid.Columns.Add(new DataGridTemplateColumn { Header = Loc.T("Optional %"), CellTemplate = new DataTemplate { VisualTree = probability }, Width = 85 });
        Grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("Outfit part"), Binding = new Binding(nameof(WearableRecord.Name)), IsReadOnly = true, Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        Grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("Content"), Binding = new Binding(nameof(WearableRecord.ContentLabel)), IsReadOnly = true, Width = 140 });
        Grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("Color / variant"), Binding = new Binding(nameof(WearableRecord.Variant)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        Grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("Body slots"), Binding = new Binding(nameof(WearableRecord.Slots)), IsReadOnly = true, Width = 110 });
        var meshStyle = new Style(typeof(TextBlock)); meshStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(nameof(WearableRecord.MeshEvidence))));
        Grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("Worn meshes"), Binding = new Binding(nameof(WearableRecord.MeshEvidence)), ElementStyle = meshStyle, IsReadOnly = true, Width = 210 });
        Children.Add(Grid);
        foreach (var piece in config.Pieces) piece.PropertyChanged += (_, args) =>
        {
            if (updating || args.PropertyName is not (nameof(WearableRecord.PartMode) or nameof(WearableRecord.OptionalChance))) return;
            if (piece.IsLewd && piece.Include && !config.AllowLewdParts)
            {
                updating = true; piece.PartMode = "Excluded"; updating = false;
                notes.Text = Loc.T("Marked lewd parts stay excluded. Use the explicit lewd-variant button to opt in."); return;
            }
            if (config.MatchVariants && piece.Include)
            {
                updating = true;
                foreach (var other in config.Pieces.Where(p => p != piece && p.Include && OutfitVariantPlan.Family(p) == OutfitVariantPlan.Family(piece)))
                { other.PartMode = piece.PartMode; other.OptionalChance = piece.OptionalChance; }
                updating = false;
            }
            config.PartPreset = "Custom";
            Dispatcher.BeginInvoke(new Action(() => { Refresh(); save(); ActivityHistory.Add(config.OutfitName + ": " + piece.Name + " · " + piece.InclusionLabel); }));
        };
        Refresh();
    }
    private void Change(Action action)
    {
        Grid.CommitEdit(DataGridEditingUnit.Cell, true); Grid.CommitEdit(DataGridEditingUnit.Row, true);
        updating = true;
        try { action(); } finally { updating = false; }
        Refresh(); save(); ActivityHistory.Add(config.OutfitName + ": " + OutfitPartPresets.Summary(config));
    }
    internal void ShowPartWarnings() => Refresh();
    private void Refresh()
    {
        updating = true;
        if (variants is not null) variants.IsChecked = config.MatchVariants;
        updating = false;
        summary.Text = OutfitPartPresets.Summary(config);
        var errors = OutfitPartPresets.Errors(config);
        notes.ShowMessages(errors.Concat(OutfitPartPresets.Warnings(config.Pieces)),errors.Count > 0 ? Loc.T("Check these parts before continuing") : Loc.T("Outfit notes"),OutfitMeshPolicy.Details(config));
    }
}
