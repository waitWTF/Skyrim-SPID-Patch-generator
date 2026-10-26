using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace WearableDistributor;

internal sealed class SupportReportWindow : Window
{
    public SupportReportWindow()
    {
        UiTheme.Apply(this);
        Title = Loc.T("Error reports · Skyrim SPID Patch Generator");
        Width = 900; Height = 650; MinWidth = 650; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(22) };
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = Loc.T("Error reports"), FontSize = 24, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(UiTheme.Caption(Loc.T("Save this report and attach it to your bug report. Nothing is uploaded automatically. Paths, usernames, mod lists, settings and raw error messages are excluded.")));
        heading.Children.Add(UiTheme.Caption(Loc.T("Older application logs can contain personal paths and are not included here. Use Save report for sharing.")));
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var preview = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 12 };
        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var status = UiTheme.Caption("");
        var actions = new WrapPanel();
        var save = UiTheme.Button(Loc.T("Save report…"),true);
        var copy = UiTheme.Button(Loc.T("Copy latest error"));
        var refresh = UiTheme.Button(Loc.T("Refresh"));
        var folder = UiTheme.Button(Loc.T("Open log folder"));
        var close = UiTheme.Button(Loc.T("Close")); close.IsCancel = true; close.Click += (_, _) => Close();
        save.Click += (_, _) =>
        {
            var dialog = new SaveFileDialog { Filter = "Shareable error report (*.txt)|*.txt", FileName = "SkyrimSPIDPatchGenerator-error-report.txt" };
            if (dialog.ShowDialog(this) != true) return;
            try { SupportLog.SaveReport(dialog.FileName); status.Text = Loc.T("Report saved. You can attach this text file to a bug report."); }
            catch (Exception error) { App.WriteDiagnostic(DiagnosticEvent.SupportReportSaveFailed, error); status.Text = Loc.T("Could not save the report. Try another folder or Copy latest error."); }
        };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(SupportLog.CreateReport(latestErrorOnly: true)); status.Text = Loc.T("Latest error copied. Paste it into your bug report."); }
            catch (Exception error) { App.WriteDiagnostic(DiagnosticEvent.SupportReportCopyFailed, error); status.Text = Loc.T("Clipboard unavailable. Try Save report instead."); }
        };
        refresh.Click += (_, _) => preview.Text = SupportLog.CreateReport();
        folder.Click += (_, _) =>
        {
            try { System.IO.Directory.CreateDirectory(SupportLog.DirectoryPath); Process.Start(new ProcessStartInfo(SupportLog.DirectoryPath) { UseShellExecute = true }); }
            catch (Exception error) { App.WriteDiagnostic(DiagnosticEvent.SupportFolderOpenFailed, error); status.Text = Loc.T("Could not open the log folder. Try Save report instead."); }
        };
        foreach (var button in new[] { save, copy, refresh, folder, close }) actions.Children.Add(button);
        footer.Children.Add(actions); footer.Children.Add(status); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(preview); Content = root;
        preview.Text = SupportLog.CreateReport();
    }
}
