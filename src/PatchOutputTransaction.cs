using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WearableDistributor;

// All generated output mutations use the same snapshot and rollback mechanism.
// The generated files cannot be atomically exchanged by Windows as one unit. A ready
// snapshot is retained if the process exits between exchanges, so it can be restored.
internal static class PatchOutputTransaction
{
    internal const string SkyPatcherIniPath = "SKSE/Plugins/SkyPatcher/npc/WearableDistributor/WearableDistributor.ini";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private static readonly HashSet<string> ManagedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        PatchGenerator.OutputPluginName, PatchGenerator.OutputIniName, PatchGenerator.ManifestName, SkyPatcherIniPath, MerchantStockService.RelativePath
    };

    internal static string NormalizeRoot(string directory)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        BuildInfo.CheckOutput(root);
        return root;
    }

    internal static string ManagedPath(string directory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException("A generated file path must be relative to the selected output folder.");
        var normalized = relativePath.Replace('\\', '/');
        if (!ManagedPaths.Contains(normalized))
            throw new InvalidDataException($"The patch contains an unrecognized generated file path: {relativePath}");
        var root = NormalizeRoot(directory);
        var path = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A generated file path escapes the selected output folder.");
        RejectLinkedPath(root, path);
        return path;
    }

    internal static IDisposable Acquire(string directory)
    {
        var root = NormalizeRoot(directory);
        Directory.CreateDirectory(root);
        RejectLinkedPath(root, root);
        try
        {
            return new FileStream(Path.Combine(root, ".WearableDistributor.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException exception)
        {
            App.WriteDiagnostic(DiagnosticEvent.OutputLockFailed, exception);
            throw new IOException("This output folder is busy. Close other patch operations using it and try again.", exception);
        }
    }

    internal static string BackupRootFor(string directory)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeRoot(directory).ToUpperInvariant())))[..16];
        var localData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (string.IsNullOrWhiteSpace(localData) || !Path.IsPathFullyQualified(localData))
            localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localData, "WearableDistributor", "Backups", hash);
    }

    internal static void EnsureNoInterruptedWrite(string directory)
    {
        var latest = LatestBackup(directory);
        if (latest is not null && ReadSnapshot(latest) is { SchemaVersion: 2, State: "Ready" } snapshot && snapshot.Files.Count > 0)
            throw new InvalidOperationException("An earlier output update was interrupted. Use Restore last backup in Patch Overview before generating another patch.");
    }

    // Caller holds Acquire() for the entire read/modify/write operation.
    internal static void Commit(string directory, IReadOnlyList<string?> staged, IReadOnlyList<string> destinations, string operation)
    {
        var root = NormalizeRoot(directory);
        if (staged.Count != destinations.Count || staged.Count == 0)
            throw new ArgumentException("Each staged file needs exactly one output destination.");
        var normalizedDestinations = destinations.Select(destination => ManagedPath(root, Path.GetRelativePath(root, destination))).ToList();
        if (normalizedDestinations.Distinct(StringComparer.OrdinalIgnoreCase).Count() != destinations.Count)
            throw new InvalidDataException("An output transaction contains duplicate destinations.");
        for (var i = 0; i < staged.Count; i++)
        {
            if (staged[i] is { } source && (!File.Exists(source) || !Path.GetFullPath(source).StartsWith(root + Path.DirectorySeparatorChar + ".WearableDistributor-", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("A generated file was not staged inside the selected output folder.");
            ProbeDestination(normalizedDestinations[i]);
        }
        var backupRoot = BackupRootFor(root);
        var backupPath = Path.Combine(backupRoot, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(backupPath);
        var snapshot = new OutputSnapshot { OutputDirectory = root, Operation = operation };
        try
        {
            for (var i = 0; i < destinations.Count; i++)
            {
                var entry = new SnapshotFile { RelativePath = Path.GetRelativePath(root, normalizedDestinations[i]).Replace('\\', '/'), Existed = File.Exists(normalizedDestinations[i]), BackupFile = i.ToString(CultureInfo.InvariantCulture) + ".bin" };
                if (entry.Existed)
                {
                    var savedPath = Path.Combine(backupPath, entry.BackupFile);
                    File.Copy(normalizedDestinations[i], savedPath, false);
                    entry.Length = new FileInfo(savedPath).Length;
                    entry.Sha256 = HashFile(savedPath);
                }
                snapshot.Files.Add(entry);
            }
            SaveSnapshot(backupPath, snapshot);
        }
        catch (Exception failure)
        {
            App.WriteDiagnostic(DiagnosticEvent.SnapshotCreateFailed, failure);
            // No destination has been touched while preparing the snapshot.
            try { Directory.Delete(backupPath, true); } catch (Exception cleanup) { App.WriteDiagnostic(DiagnosticEvent.TemporaryCleanupFailed, cleanup, DiagnosticLevel.Warning); }
            throw;
        }
        try
        {
            for (var i = 0; i < normalizedDestinations.Count; i++)
            {
                var destination = normalizedDestinations[i];
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (staged[i] is { } source) File.Move(source, destination, true);
                else if (File.Exists(destination)) File.Delete(destination);
            }
            snapshot.State = "Committed";
            SaveSnapshot(backupPath, snapshot);
        }
        catch (Exception failure)
        {
            App.WriteDiagnostic(DiagnosticEvent.OutputCommitFailed, failure);
            try
            {
                ValidateSnapshot(root, backupPath, snapshot);
                ApplySnapshot(root, backupPath, snapshot);
                snapshot.State = "RolledBack";
                SaveSnapshot(backupPath, snapshot);
            }
            catch (Exception rollbackFailure)
            {
                App.WriteDiagnostic(DiagnosticEvent.OutputRollbackFailed, rollbackFailure);
                throw new IOException($"The output update failed and automatic rollback could not finish. Close programs using the output files, then use Restore last backup. Backup: {backupPath}", new AggregateException(failure, rollbackFailure));
            }
            throw new IOException("The output update failed. The previous generated files have been restored.", failure);
        }
        // Retention is maintenance, never a reason to undo a successful patch.
        try
        {
            foreach (var old in Directory.GetDirectories(backupRoot).OrderByDescending(Path.GetFileName, StringComparer.Ordinal).Skip(10))
                try { Directory.Delete(old, true); } catch (Exception maintenance) { App.WriteDiagnostic(DiagnosticEvent.BackupMaintenanceFailed, maintenance, DiagnosticLevel.Warning); }
        }
        catch (Exception maintenance) { App.WriteDiagnostic(DiagnosticEvent.BackupMaintenanceFailed, maintenance, DiagnosticLevel.Warning); }
    }

    internal static void Restore(string directory, Action<string> validateStagedOutput)
    {
        var root = NormalizeRoot(directory);
        using var lease = Acquire(root);
        var latest = LatestBackup(root) ?? throw new InvalidOperationException("No Skyrim SPID Patch Generator backup was found for this output folder.");
        var snapshot = ReadSnapshot(latest);
        // Validate EVERY file and path before changing ANY destination.
        ValidateSnapshot(root, latest, snapshot);
        var tempRoot = Path.Combine(root, ".WearableDistributor-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var staged = new List<string?>();
            var destinations = new List<string>();
            foreach (var file in snapshot.Files)
            {
                var destination = ManagedPath(root, file.RelativePath);
                destinations.Add(destination);
                if (!file.Existed) { staged.Add(null); continue; }
                var stage = ManagedPath(tempRoot, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(stage)!);
                File.Copy(Path.Combine(latest, file.BackupFile), stage, false);
                staged.Add(stage);
            }
            validateStagedOutput(tempRoot);
            var merchandise = ManagedPath(root, MerchantStockService.RelativePath);
            if (!destinations.Contains(merchandise, StringComparer.OrdinalIgnoreCase) && File.Exists(merchandise))
            { destinations.Add(merchandise); staged.Add(null); }
            Commit(root, staged, destinations, "Restore");
            // A successfully restored interrupted snapshot is no longer pending.
            if (snapshot.State == "Ready")
            {
                snapshot.State = "Recovered";
                try { SaveSnapshot(latest, snapshot); } catch (Exception maintenance) { App.WriteDiagnostic(DiagnosticEvent.BackupMaintenanceFailed, maintenance, DiagnosticLevel.Warning); }
            }
        }
        finally { try { Directory.Delete(tempRoot, true); } catch (Exception cleanup) { App.WriteDiagnostic(DiagnosticEvent.TemporaryCleanupFailed, cleanup, DiagnosticLevel.Warning); } }
    }

    private static void ValidateSnapshot(string root, string backupPath, OutputSnapshot snapshot)
    {
        if (snapshot.SchemaVersion != 2 || snapshot.Files.Count == 0)
            throw new InvalidDataException("This backup predates checksum verification and cannot be safely restored automatically. Its files remain available in the backup folder.");
        if (!NormalizeRoot(snapshot.OutputDirectory).Equals(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("This backup belongs to a different output folder.");
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in snapshot.Files)
        {
            if (!destinations.Add(ManagedPath(root, entry.RelativePath))) throw new InvalidDataException("The backup contains duplicate generated file destinations.");
            if (Path.GetFileName(entry.BackupFile) != entry.BackupFile || string.IsNullOrWhiteSpace(entry.BackupFile))
                throw new InvalidDataException("The backup contains an invalid saved file path.");
            if (!entry.Existed) continue;
            var saved = Path.Combine(backupPath, entry.BackupFile);
            if (!File.Exists(saved) || new FileInfo(saved).Length != entry.Length || !HashFile(saved).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Backup integrity verification failed for {entry.RelativePath}. No output files were changed.");
        }
    }

    private static void ApplySnapshot(string root, string backupPath, OutputSnapshot snapshot)
    {
        foreach (var entry in snapshot.Files)
        {
            var destination = ManagedPath(root, entry.RelativePath);
            if (entry.Existed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(Path.Combine(backupPath, entry.BackupFile), destination, true);
            }
            else if (File.Exists(destination)) File.Delete(destination);
        }
    }

    private static void ProbeDestination(string path)
    {
        if (Directory.Exists(path)) throw new IOException($"A directory is blocking a generated file: {path}");
        if (!File.Exists(path)) return;
        if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0)
            throw new IOException($"The generated file is read-only: {path}. Choose a writable output mod folder.");
        using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    private static void RejectLinkedPath(string root, string path)
    {
        // Validate output ancestry and every existing descendant. Vortex hardlinks
        // are normal files; junctions/symlinks can redirect writes outside the output.
        for (var current = path; !string.IsNullOrWhiteSpace(current); current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"The output path passes through a symbolic link or junction: {current}. Choose a regular output mod folder.");
        }
    }

    private static string HashFile(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static string? LatestBackup(string root) => Directory.Exists(BackupRootFor(root))
        ? Directory.GetDirectories(BackupRootFor(root)).Where(path => File.Exists(Path.Combine(path, "backup.json"))).OrderByDescending(Path.GetFileName, StringComparer.Ordinal).FirstOrDefault() : null;
    private static OutputSnapshot ReadSnapshot(string path) => JsonSerializer.Deserialize<OutputSnapshot>(File.ReadAllText(Path.Combine(path, "backup.json")), JsonOptions)
        ?? throw new InvalidDataException("The backup metadata could not be read.");
    private static void SaveSnapshot(string path, OutputSnapshot snapshot)
    {
        var temp = Path.Combine(path, "backup.json.tmp");
        File.WriteAllText(temp, JsonSerializer.Serialize(snapshot, JsonOptions), new UTF8Encoding(false));
        File.Move(temp, Path.Combine(path, "backup.json"), true);
    }

    private sealed class OutputSnapshot
    {
        public int SchemaVersion { get; set; } = 2;
        public string OutputDirectory { get; set; } = "";
        public string Operation { get; set; } = "";
        public string State { get; set; } = "Ready";
        public List<SnapshotFile> Files { get; set; } = [];
    }
    private sealed class SnapshotFile
    {
        public string RelativePath { get; set; } = "";
        public string BackupFile { get; set; } = "";
        public bool Existed { get; set; }
        public long Length { get; set; }
        public string Sha256 { get; set; } = "";
    }
}
