using System.Collections.ObjectModel;
using System.Windows;

namespace WearableDistributor;

public sealed record ActivityEntry(string Time, string Action);

/// <summary>Local, memory-only user activity. Never serialized or attached to support reports.</summary>
public static class ActivityHistory
{
    private static readonly ObservableCollection<ActivityEntry> entries = [];
    public static ReadOnlyObservableCollection<ActivityEntry> Entries { get; } = new(entries);
    public static void Add(string action)
    {
        if (string.IsNullOrWhiteSpace(action)) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;
        var time = DateTime.Now.ToString("HH:mm:ss");
        if (!dispatcher.CheckAccess()) { dispatcher.BeginInvoke(new Action(() => Append(time, action))); return; }
        Append(time, action);
    }
    private static void Append(string time, string action)
    {
        if (entries.LastOrDefault()?.Action == action) return;
        entries.Add(new ActivityEntry(time, action.Length > 3000 ? action[..3000] + "…" : action));
        while (entries.Count > 1000) entries.RemoveAt(0);
    }
    public static void Clear() { entries.Clear(); Add("Activity history cleared."); }
}
