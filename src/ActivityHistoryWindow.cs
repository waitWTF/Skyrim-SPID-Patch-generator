using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace WearableDistributor;

public sealed class ActivityHistoryWindow : Window
{
    public static void ShowFor(Window owner)
    {
        var existing = Application.Current.Windows.OfType<ActivityHistoryWindow>().FirstOrDefault();
        if (existing is not null)
        {
            if (existing.Owner == owner)
            {
                if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
                existing.Activate(); return;
            }
            // Reopen under the wizard if a pre-existing window was disabled by its modal loop.
            // The shared memory history survives this window closing.
            existing.Close();
        }
        new ActivityHistoryWindow { Owner = owner }.Show();
    }

    private ActivityHistoryWindow()
    {
        UiTheme.Apply(this);
        Title = Loc.T("Skyrim SPID Patch Generator · Activity history");
        Width = 980; Height = 620; MinWidth = 650; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(20) };
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = Loc.T("Activity history"), FontSize = 24, FontWeight = FontWeights.SemiBold });
        header.Children.Add(UiTheme.Caption(Loc.T("Live activity for this app session · latest 1,000 entries. May show plugin names and local paths. Kept in memory only; never saved, uploaded or included in Error reports.")));
        var tools = new WrapPanel { Margin = new Thickness(0, 10, 0, 12) };
        var follow = new CheckBox { Content = Loc.T("Follow latest activity"), IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
        var clear = UiTheme.Button(Loc.T("Clear history")); clear.Click += (_, _) => ActivityHistory.Clear();
        tools.Children.Add(follow); tools.Children.Add(clear); header.Children.Add(tools);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var grid = new DataGrid { ItemsSource = ActivityHistory.Entries, AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single };
        grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("TIME"), Binding = new Binding(nameof(ActivityEntry.Time)), Width = 88 });
        var textStyle = new Style(typeof(TextBlock)); textStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("ACTIVITY"), Binding = new Binding(nameof(ActivityEntry.Action)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), ElementStyle = textStyle });
        void FollowLatest() { if (follow.IsChecked == true && ActivityHistory.Entries.Count > 0) grid.ScrollIntoView(ActivityHistory.Entries[^1]); }
        NotifyCollectionChangedEventHandler changed = (_, _) => { if (IsLoaded) FollowLatest(); };
        ((INotifyCollectionChanged)ActivityHistory.Entries).CollectionChanged += changed;
        Closed += (_, _) => ((INotifyCollectionChanged)ActivityHistory.Entries).CollectionChanged -= changed;
        follow.Checked += (_, _) => FollowLatest(); Loaded += (_, _) => FollowLatest();
        root.Children.Add(grid); Content = root;
    }
}
