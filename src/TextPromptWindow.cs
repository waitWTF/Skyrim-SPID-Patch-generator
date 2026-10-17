using System.Windows;
using System.Windows.Controls;

namespace WearableDistributor;

internal sealed class TextPromptWindow : Window
{
    private readonly TextBox input = new() { MinWidth = 340, Padding = new Thickness(7) };
    private TextPromptWindow(string title, string label)
    {
        UiTheme.Apply(this);
        Title = title; Width = 470; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 7) });
        panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = UiTheme.Button("Cancel"); cancel.IsCancel = true; cancel.MinWidth = 85;
        var save = UiTheme.Button("Save", true); save.IsDefault = true; save.MinWidth = 85; save.IsEnabled = false;
        input.TextChanged += (_, _) => save.IsEnabled = !string.IsNullOrWhiteSpace(input.Text);
        save.Click += (_, _) => { DialogResult = true; Close(); };
        buttons.Children.Add(cancel); buttons.Children.Add(save); panel.Children.Add(buttons); Content = panel;
        Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
    }
    public static string? Show(Window owner, string title, string label)
    {
        var dialog = new TextPromptWindow(title, label) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.input.Text : null;
    }
}
