using System.Windows;
using System.Windows.Controls;

namespace WearableDistributor;

public sealed class ScanLocationWindow : Window
{
    public ScanLocation? SelectedLocation { get; private set; }
    public ScanLocationWindow(IReadOnlyList<ScanLocation> locations)
    {
        Title = Loc.T("Find your Skyrim mods"); Width = 760; Height = 540; MinWidth = 520; MinHeight = 350;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new TextBlock { Text = Loc.T("Choose your game / mod-manager location"), FontSize = 20, Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap };
        grid.Children.Add(heading);
        var list = new ListBox { ItemsSource = locations, Margin = new Thickness(0, 0, 0, 10) }; Grid.SetRow(list, 1); grid.Children.Add(list);
        var detail = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 12) };
        Grid.SetRow(detail, 2); grid.Children.Add(detail);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = Loc.T("Cancel"), IsCancel = true };
        var use = new Button { Content = Loc.T("Use this location"), IsDefault = true, IsEnabled = false };
        footer.Children.Add(cancel); footer.Children.Add(use); Grid.SetRow(footer, 3); grid.Children.Add(footer);
        list.SelectionChanged += (_, _) => { var location = list.SelectedItem as ScanLocation; detail.Text = location?.Explanation ?? ""; use.IsEnabled = location?.Usable == true; };
        use.Click += (_, _) => { SelectedLocation = list.SelectedItem as ScanLocation; DialogResult = true; };
        Content = grid; UiTheme.Apply(this);
        if (locations.Count > 0) list.SelectedIndex = 0;
        else detail.Text = Loc.T("No verified Skyrim Data folder found. Browse to the Data folder containing Skyrim.esm in Settings. For MO2, launch through MO2 first.");
    }
}
