using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace WearableDistributor;

/// <summary>Readable local feedback; detailed filenames are shown only on request and never logged.</summary>
internal sealed class ValidationNotice : Border
{
    private readonly StackPanel body = new();
    private bool detailsExpanded;
    private string text = "";
    public string Text { get => text; set => ShowMessages([value], "Setup note"); }

    public ValidationNotice()
    {
        Background = UiTheme.Brush("SurfaceBrush"); BorderBrush = UiTheme.Brush("BorderBrush");
        BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(8); Padding = new Thickness(14);
        Margin = new Thickness(0, 5, 0, 12); Child = body; Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(this, "Outfit setup messages");
    }

    public void ShowMessages(IEnumerable<string> messages, string heading, IEnumerable<string>? details = null)
    {
        var list = messages.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
        text = string.Join("\n\n", list); body.Children.Clear();
        Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (list.Count == 0) return;
        body.Children.Add(new TextBlock { Text = heading, FontSize = 15, FontWeight = FontWeights.SemiBold,
            Foreground = UiTheme.Brush("WarningBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 7) });
        foreach (var message in list)
            body.Children.Add(new TextBlock { Text = "• " + message, FontSize = 14, LineHeight = 22,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 5) });
        var expanded = details?.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList() ?? [];
        if (expanded.Count == 0) return;
        var detailText = new TextBox { Text = string.Join("\n\n", expanded), IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            FontSize = 13, Padding = new Thickness(10), MaxHeight = 240,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetName(detailText, "Affected parts and mesh file details");
        var more = new Expander { Header = "Show affected parts and file details", Content = detailText,
            IsExpanded = detailsExpanded, Margin = new Thickness(0, 8, 0, 0) };
        more.Expanded += (_, _) => detailsExpanded = true; more.Collapsed += (_, _) => detailsExpanded = false;
        body.Children.Add(more);
    }
}
