using System.IO;

namespace WearableDistributor;

/// <summary>One read/parse per INI per operation, rather than one full read for every outfit.</summary>
internal sealed class GeneratedDistributionIndex
{
    internal sealed record Section(string Plugin, string Outfit, int Start, int End, IReadOnlyList<string> Lines);
    private readonly Dictionary<string, List<Section>> sections = new(StringComparer.OrdinalIgnoreCase);
    internal IReadOnlyList<string> Lines { get; }
    internal GeneratedDistributionIndex(string path) : this(File.ReadAllLines(path)) { }
    internal GeneratedDistributionIndex(IReadOnlyList<string> lines)
    {
        Lines = lines;
        for (var start = 0; start < lines.Count; start++)
        {
            if (!TryHeader(lines[start], out var plugin)) continue;
            var end = start + 1;
            while (end < lines.Count && !TryHeader(lines[end], out _)) end++;
            var name = start + 1 < end && lines[start + 1].StartsWith("; Outfit: ", StringComparison.Ordinal)
                ? lines[start + 1][10..] : Path.GetFileNameWithoutExtension(plugin);
            var key = Key(plugin, name);
            if (!sections.TryGetValue(key, out var group)) sections[key] = group = [];
            group.Add(new Section(plugin, name, start, end, lines.Skip(start + 1).Take(end - start - 1).ToList()));
            start = end - 1;
        }
    }
    internal IReadOnlyList<Section> Find(string plugin, string outfit) => sections.GetValueOrDefault(Key(plugin, outfit)) ?? [];
    internal static string Key(string plugin, string outfit) => plugin + "\0" + (string.IsNullOrWhiteSpace(outfit) ? Path.GetFileNameWithoutExtension(plugin) : outfit);
    internal static bool TryHeader(string line, out string plugin)
    {
        plugin = "";
        if (!line.StartsWith("; ", StringComparison.Ordinal) || !line.EndsWith(" piece(s)", StringComparison.Ordinal)) return false;
        var delimiter = line.LastIndexOf(" — ", StringComparison.Ordinal);
        if (delimiter < 3) return false;
        plugin = line[2..delimiter];
        return Path.GetExtension(plugin).ToLowerInvariant() is ".esm" or ".esl" or ".esp";
    }
}
