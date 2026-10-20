using System.IO;

namespace WearableDistributor;

public static class PatchFileDiscovery
{
    private static readonly HashSet<string> Skip = new(["meshes", "textures", "scripts", "source", "sound", "music", "interface", "CalienteTools", "Backups", "_BuildBackups", "_BuildUpdates", "Runtime", "Documentation", ".git"], StringComparer.OrdinalIgnoreCase);
    public static IReadOnlyList<string> Find(IEnumerable<string> locations, CancellationToken cancellationToken = default)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        foreach (var location in locations.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            if (File.Exists(location)) { if (DetectedPatchReader.IsGeneratedFile(location)) files.Add(Path.GetFullPath(location)); }
            else if (Directory.Exists(location)) pending.Push(Path.GetFullPath(location));
        }
        while (pending.TryPop(out var root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(root)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(root)) if (DetectedPatchReader.IsGeneratedFile(file)) files.Add(file);
                foreach (var child in Directory.EnumerateDirectories(root))
                    if (!Skip.Contains(Path.GetFileName(child)) && !Path.GetFileName(child).StartsWith('.') &&
                        (File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            { App.WriteDiagnostic(DiagnosticEvent.FolderReadFailed, failure, DiagnosticLevel.Warning); }
        }
        return files.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
