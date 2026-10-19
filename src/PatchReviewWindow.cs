using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace WearableDistributor;

public sealed class PatchReviewWindow : Window
{
    public PatchReviewWindow(IEnumerable<PluginPatchOptions> configurations, Func<DistributionTargetRule, int> countMatches,
        Func<DistributionTargetRule, PluginPatchOptions, int> countConflicts, Func<DistributionTargetRule, int> countInstalledConflicts, string framework, string? outputDirectory = null)
    {
        UiTheme.Apply(this);
        Title = "Skyrim SPID Patch Generator · Review patch";
        Width = 1140; Height = 780; MinWidth = 940; MinHeight = 580;
        MaxHeight = Math.Max(580, SystemParameters.WorkArea.Height);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var configs = configurations.ToList();
        var rows = BuildRows(configs, countMatches, countConflicts, countInstalledConflicts);
        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(new TextBlock { Text = "03  /  REVIEW", Foreground = UiTheme.Brush("AccentBrush"), FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) });
        header.Children.Add(new TextBlock { Text = "Ready to generate?", FontSize = 26, FontWeight = FontWeights.SemiBold });
        header.Children.Add(UiTheme.Caption("Review NPC recipients and trader stock. Select a row to inspect all its details."));
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            header.Children.Add(UiTheme.Caption("Output: " + outputDirectory));
            header.Children.Add(UiTheme.Caption("Only the listed named outfits are updated. Other outfits are kept. Selected broken rules are rebuilt, and the complete output must validate before a backed-up update is installed."));
        }
        var stats = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        stats.Children.Add(Statistic(configs.Count.ToString(CultureInfo.InvariantCulture), "outfit setups"));
        stats.Children.Add(Statistic(configs.Select(config => config.Source.Plugin).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString(CultureInfo.InvariantCulture), "source plugins"));
        stats.Children.Add(Statistic(configs.Sum(config => config.Pieces.Count(piece => piece.Include)).ToString(CultureInfo.InvariantCulture), "included parts"));
        stats.Children.Add(Statistic(rows.Count(row => row.HasNotes).ToString(CultureInfo.InvariantCulture), "audiences with notes"));
        header.Children.Add(stats); root.Children.Add(header);

        var body = new Grid(); body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.3, GridUnitType.Star) }); body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 120 });
        var grid = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, Margin = new Thickness(0, 0, 0, 12) };
        grid.Columns.Add(new DataGridTextColumn { Header = "Outfit / plugin", Binding = new Binding(nameof(ReviewRow.Outfit)), Width = new DataGridLength(1.5, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Parts", Binding = new Binding(nameof(ReviewRow.Pieces)), Width = 66 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Audience", Binding = new Binding(nameof(ReviewRow.Group)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "NPCs", Binding = new Binding(nameof(ReviewRow.Matches)), Width = 70 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Chance", Binding = new Binding(nameof(ReviewRow.Chance)), Width = 76 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Review notes", Binding = new Binding(nameof(ReviewRow.Status)), Width = new DataGridLength(1.4, DataGridLengthUnitType.Star) });
        var detail = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.Brush("MutedBrush"), Margin = new Thickness(0, 4, 0, 0) };
        var detailHeading = new TextBlock { FontSize = 16, FontWeight = FontWeights.SemiBold };
        var detailPanel = new StackPanel(); detailPanel.Children.Add(detailHeading); detailPanel.Children.Add(detail);
        var detailsCard = UiTheme.Card(new ScrollViewer { Content = detailPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        grid.SelectionChanged += (_, _) =>
        {
            if (grid.SelectedItem is not ReviewRow row) return;
            detailHeading.Text = row.Outfit + " · " + row.Group; detail.Text = row.Details;
        };
        body.Children.Add(grid); Grid.SetRow(detailsCard, 1); body.Children.Add(detailsCard);
        Grid.SetRow(body, 1); root.Children.Add(body);

        var footer = new Grid { Margin = new Thickness(0, 18, 0, 0) }; footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var notes = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) };
        notes.Children.Add(new TextBlock { Text = (configs.Any(config => config.DistributeToNpcs) ? framework + " NPC outfits" : "Shop stock only · SkyPatcher containers") + " · Merges with your existing patch.", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        notes.Children.Add(UiTheme.Caption(configs.Any(config => config.DistributeToNpcs) ? "NPC counts are base-record estimates. Multiple spawned actors may use one record, and the game applies other installed distribution rules too." : "No NPC outfits are assigned by these setups. Shop stock requires SkyPatcher with container patching enabled."));
        footer.Children.Add(notes);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var back = UiTheme.Button("← Back to setup"); back.IsCancel = true; back.Click += (_, _) => DialogResult = false;
        var generate = UiTheme.Button("Generate patch", true); generate.IsDefault = true; generate.MinWidth = 140;
        generate.IsEnabled = configs.Count > 0 && rows.Count > 0 && configs.All(config => config.Pieces.Any(piece => piece.Include) && (config.DistributeToNpcs ? config.TargetRules.Count > 0 : config.MerchantCategories.Count > 0)) && rows.All(row => !row.BlocksGeneration);
        generate.ToolTip = generate.IsEnabled ? "Write the outfit plugin, distribution INI, and patch manifest." : "Return to setup and fix the audiences with no matches or missing parts.";
        generate.Click += (_, _) => DialogResult = true;
        actions.Children.Add(back); actions.Children.Add(generate); Grid.SetColumn(actions, 1); footer.Children.Add(actions); Grid.SetRow(footer, 2); root.Children.Add(footer);
        Content = root;
        grid.SelectedIndex = rows.FindIndex(row => row.HasNotes) is var firstNote && firstNote >= 0 ? firstNote : rows.Count > 0 ? 0 : -1;
    }

    private static List<ReviewRow> BuildRows(List<PluginPatchOptions> configs, Func<DistributionTargetRule, int> countMatches,
        Func<DistributionTargetRule, PluginPatchOptions, int> countConflicts, Func<DistributionTargetRule, int> countInstalledConflicts)
    {
        var rows = new List<ReviewRow>();
        foreach (var config in configs)
        {
            var included = config.Pieces.Where(piece => piece.Include).ToList();
            if (!config.DistributeToNpcs)
            {
                var stock = MerchantStockService.Build(config);
                var blocked = included.Count == 0 || stock.Count == 0;
                rows.Add(new ReviewRow(config.OutfitName, $"{included.Count}/{config.Pieces.Count}", "Merchants only", "—", "N/A",
                    blocked ? "Select parts and traders" : "Ready · shop stock only",
                    $"Plugin: {config.Source.Plugin}\nTrader categories: {string.Join(", ", config.MerchantCategories)}\n" +
                    $"Stock containers: {stock.Select(rule => (rule.ContainerPlugin, rule.ContainerId)).Distinct().Count()}\n" +
                    $"Stock items: {stock.Select(rule => (rule.ItemPlugin, rule.ItemId)).Distinct().Count()}\n\n" +
                    "No NPC outfits are assigned. NPC chance, sex and audience choices are ignored. Different body slots/sex variants may be sold together. Requires SkyPatcher with container patching enabled; clothing traders receive no weapons.\n\nSelected parts:\n" +
                    string.Join("\n", included.Select(piece => "• " + piece.Name)), true, blocked));
                continue;
            }
            var slotWarnings = FindSlotOverlaps(config);
            if (config.TargetRules.Count == 0)
            {
                rows.Add(new ReviewRow(config.OutfitName, $"{included.Count}/{config.Pieces.Count}", "No audience", "0", config.Chance + "%", "Audience required", "No audience is configured. Return to setup and add an audience.", true, true));
                continue;
            }
            foreach (var rule in config.TargetRules)
            {
                var matches = rule.NpcResolutionComplete ? rule.ResolvedNpcFormIds.Count : countMatches(rule);
                rule.EstimatedNpcCount = matches;
                var overlaps = countConflicts(rule, config); var installed = countInstalledConflicts(rule);
                var notes = new List<string>();
                if (rule.ProtectedNpcCount > 0) notes.Add($"⚠ Protected NPCs: {rule.ProtectedNpcCount} member(s) have existing uniform protection. This explicit faction selection replaces their outfits and can remove their recognizable uniforms. Guards stay excluded.");
                if (rule.Group == NpcTargetingService.UniqueGroup) notes.Add("⚠ " + NpcTargetingService.UniqueWarning);
                if (config.MerchantCategories.Count > 0)
                    notes.Add("Merchandise: " + string.Join(", ", config.MerchantCategories) + ". Requires SkyPatcher even with SPID NPC outfits. Stock gets one of each selected part; NPC chance does not apply. Clothing traders receive no weapons.");
                if (included.Count == 0) notes.Add("No outfit parts included.");
                if (matches == 0) notes.Add("No eligible NPCs matched. Replace or remove this audience.");
                if (matches > 500) notes.Add("Broad audience. Check that this outfit belongs on all these NPCs.");
                if (overlaps > 0) notes.Add($"May overlap {overlaps} other configured outfit audience(s). Competing outfits do not guarantee the chance split you expect.");
                if (installed > 0) notes.Add($"Potential overlap with {installed} installed distribution rule(s). The scan cannot fully simulate their runtime filters or priority.");
                if (slotWarnings.Count > 0) notes.Add("Possible incompatible outfit parts:\n" + string.Join("\n", slotWarnings.Take(12)) + (slotWarnings.Count > 12 ? "\nMore overlapping combinations exist. Choose a compatible subset of parts." : ""));
                var refinements = rule.Filters.Where(filter => filter.TargetKind != "Npc" || filter.Category == "NPC role").GroupBy(filter => filter.Category)
                    .Select(group => group.Key + ": " + string.Join(" OR ", group.Select(filter => NpcTargetingService.Humanize(filter.EditorId)).Distinct(StringComparer.OrdinalIgnoreCase)));
                var details = $"Plugin: {config.Source.Plugin}\nCollection: {rule.Collection}\nNPC sex: {config.Gender}\nAudience: {rule.Group}\n" +
                    string.Join("\n", refinements) + $"\n\nIncluded parts ({included.Count}):\n" + string.Join("\n", included.Select(piece => "• " + piece.Name + " · " + piece.Slots)) +
                    (notes.Count == 0 ? "\n\nNo issues found by the available checks." : "\n\nReview notes:\n" + string.Join("\n\n", notes));
                var status = matches == 0 ? "No matching NPCs" : included.Count == 0 ? "No parts included" : rule.ProtectedNpcCount > 0 ? "⚠ Protected uniforms" : rule.Group == NpcTargetingService.UniqueGroup ? "⚠ Unique NPCs · quest/script caution" : slotWarnings.Count > 0 ? "Check overlapping body slots" : overlaps + installed > 0 ? "Check competing outfits" : matches > 500 ? "Broad audience" : config.MerchantCategories.Count > 0 ? "Ready + merchandise" : "Ready";
                rows.Add(new ReviewRow(config.OutfitName, $"{included.Count}/{config.Pieces.Count}", rule.Group, matches.ToString(CultureInfo.InvariantCulture), config.Chance + "%", status, details, notes.Count > 0, matches == 0 || included.Count == 0));
            }
        }
        return rows;
    }

    private static Border Statistic(string value, string label)
    {
        var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = value, FontSize = 23, FontWeight = FontWeights.SemiBold, Foreground = UiTheme.Brush("AccentBrush") });
        panel.Children.Add(UiTheme.Caption(label)); var card = UiTheme.Card(panel); card.MinWidth = 150; card.Margin = new Thickness(0, 0, 10, 0); card.Padding = new Thickness(14, 10, 14, 6); return card;
    }

    private static List<string> FindSlotOverlaps(PluginPatchOptions config)
    {
        var pieces = config.Pieces.Where(piece => piece.Include).ToList(); var result = new List<string>();
        var parsed = pieces.Select(piece => OutfitPartSelection.BodySlots(piece.Slots)).ToList();
        var bySlot = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < pieces.Count; index++)
            foreach (var slot in parsed[index])
            { if (!bySlot.TryGetValue(slot, out var members)) bySlot[slot] = members = []; members.Add(index); }
        var pairs = new HashSet<(int, int)>();
        foreach (var members in bySlot.Values.Where(members => members.Count > 1))
        for (var first = 0; first < members.Count; first++)
        for (var second = first + 1; second < members.Count; second++)
        {
            var a = members[first]; var b = members[second];
            if (!pairs.Add((a, b))) continue;
            result.Add($"• {pieces[a].Name} / {pieces[b].Name} ({string.Join(", ", parsed[a].Intersect(parsed[b], StringComparer.OrdinalIgnoreCase))})");
            // Large armor packs can contain thousands of variants. A bounded preview avoids quadratic UI work.
            if (result.Count >= 25) return result;
        }
        return result;
    }

    private sealed record ReviewRow(string Outfit, string Pieces, string Group, string Matches, string Chance, string Status, string Details, bool HasNotes, bool BlocksGeneration);
}
