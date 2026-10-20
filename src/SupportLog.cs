using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WearableDistributor;

// Only these compile-time identifiers and numeric metadata may enter a support log.
// Never add paths, exception messages/Data/ToString, arguments, settings, plugin names or user text.
internal enum DiagnosticEvent
{
    ApplicationStarted = 1000, ApplicationStopped = 1001, UnhandledUiError = 1002, UnhandledProcessError = 1003, UnobservedTaskError = 1004,
    PreferencesLoadFailed = 1100, PreferencesSaveFailed = 1101, PreferencesImportFailed = 1102, PreferencesExportFailed = 1103,
    RegistryDiscoveryFailed = 1200, SteamDiscoveryFailed = 1201, FolderDiscoveryFailed = 1202, GameLocationProbeFailed = 1203, Mo2ModuleProbeFailed = 1204, Mo2ConfigProbeFailed = 1205,
    MeshArchiveReadFailed = 1310,
    ScanFailed = 1300, PluginHeaderReadFailed = 1301, PluginReadFailed = 1302, FolderReadFailed = 1303, IniReadFailed = 1304, MissingActivePlugin = 1305, InvalidMasterDependency = 1306, LoadOrderIssue = 1307,
    PatchGenerationFailed = 1400, PatchLibraryReadFailed = 1401, PatchRemoveFailed = 1402, PatchRestoreFailed = 1403, PatchPackageFailed = 1404,
    PatchReportExportFailed = 1405, OutputFolderOpenFailed = 1406, OutputLockFailed = 1407, SnapshotCreateFailed = 1408, OutputCommitFailed = 1409, OutputRollbackFailed = 1410, BackupMaintenanceFailed = 1411, TemporaryCleanupFailed = 1412,
    PatchHealthFailed = 1413, LegacyRuleReadFailed = 1414, OutfitValidationFailed = 1415, AudienceValidationFailed = 1416, OutfitSexMismatch = 1417, PatchSetupFailed = 1418,
    SupportReportSaveFailed = 1500, SupportReportCopyFailed = 1501, SupportFolderOpenFailed = 1502, SupportStorageFailed = 1503, SupportFileReadFailed = 1504,
    UiBindingFailed = 1600, UiIconLoadFailed = 1601, UiThemeUnavailable = 1602
}

internal enum DiagnosticLevel { Information, Warning, Error, Fatal }
internal enum FailureKind { None, Cancelled, FileMissing, AccessDenied, InvalidData, InvalidOperation, Argument, Io, NativeInterop, OutOfMemory, Unexpected }
internal enum FailureReason { Unspecified, DistributionIncomplete, MissingMaster, OutputBusy, InterruptedUpdate, RollbackFailed, PreviousOutputRestored, BackupIntegrity, FrameworkConflict, OutfitSexRestriction }
internal sealed record SafeFrame(int MethodToken, int IlOffset);
internal sealed record SafeFailure(FailureKind Kind, FailureReason Reason, int HResult, List<SafeFrame> Frames);
internal sealed record SafeDiagnostic(int Schema, string Build, Guid Session, long Sequence, long ElapsedMilliseconds,
    DiagnosticEvent Event, DiagnosticLevel Level, List<SafeFailure> Failures);

/// <summary>Privacy by construction: no arbitrary text is accepted, persisted or re-exported.</summary>
internal static class SupportLog
{
    private const int MaxFiles = 20, MaxEntries = 2000, MaxRecordBytes = 24_000;
    private const long MaxFileBytes = 1_048_576;
    private static readonly object Gate = new();
    private static readonly Stopwatch Uptime = Stopwatch.StartNew();
    private static readonly Guid Session = Guid.NewGuid();
    private static readonly string Build = typeof(SupportLog).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
    private static readonly List<SafeDiagnostic> Memory = [];
    private static long sequence;
    private static int part;
    private static bool storageFailureRecorded;
    internal static bool PersistenceAvailable { get; private set; } = true;
    internal static string DirectoryPath => Path.Combine(BuildInfo.SettingsRoot, "SupportLogs");
    private static string CurrentFile => Path.Combine(DirectoryPath, $"support-{Session:N}-{part:0000}.jsonl");

    internal static string? Record(DiagnosticEvent eventId, Exception? exception = null, DiagnosticLevel level = DiagnosticLevel.Error)
    {
        // Logging must never replace the original error, including during shutdown or recovery.
        lock (Gate)
        {
            try
            {
                if (!Enum.IsDefined(eventId) || !Enum.IsDefined(level)) return null;
                var entry = new SafeDiagnostic(1, Build, Session, ++sequence, Uptime.ElapsedMilliseconds, eventId, level, Describe(exception));
                Remember(entry);
                Directory.CreateDirectory(DirectoryPath);
                var newPart = !File.Exists(CurrentFile);
                if (!newPart && new FileInfo(CurrentFile).Length >= MaxFileBytes) { part++; newPart = true; }
                using (var stream = new FileStream(CurrentFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.WriteThrough))
                {
                    var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry) + "\n");
                    stream.Write(bytes); stream.Flush(true);
                }
                PersistenceAvailable = true;
                if (newPart) TrimFiles();
                return CurrentFile;
            }
            catch (Exception failure)
            {
                PersistenceAvailable = false;
                if (!storageFailureRecorded)
                {
                    storageFailureRecorded = true;
                    Remember(new SafeDiagnostic(1, Build, Session, ++sequence, Uptime.ElapsedMilliseconds,
                        DiagnosticEvent.SupportStorageFailed, DiagnosticLevel.Error, Describe(failure)));
                }
                return null;
            }
        }
    }

    private static void Remember(SafeDiagnostic entry)
    {
        Memory.Add(entry);
        if (Memory.Count > MaxEntries) Memory.RemoveRange(0, Memory.Count - MaxEntries);
    }

    private static List<SafeFailure> Describe(Exception? error)
    {
        var result = new List<SafeFailure>();
        var pending = new Queue<Exception>(); if (error != null) pending.Enqueue(error);
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        while (pending.TryDequeue(out var current) && result.Count < 8)
        {
            if (!seen.Add(current)) continue;
            try
            {
                var frames = (new StackTrace(current, false).GetFrames() ?? []).Select(frame => (Frame: frame, Method: frame.GetMethod()))
                    .Where(item => item.Method?.Module.Assembly == typeof(SupportLog).Assembly)
                    .Take(12).Select(item => new SafeFrame(item.Method!.MetadataToken, item.Frame.GetILOffset())).ToList();
                result.Add(new SafeFailure(Kind(current), Reason(current), current.HResult, frames));
                if (current is AggregateException aggregate) foreach (var inner in aggregate.InnerExceptions.Take(8)) pending.Enqueue(inner);
                else if (current.InnerException is { } inner) pending.Enqueue(inner);
            }
            catch { result.Add(new SafeFailure(FailureKind.Unexpected, FailureReason.Unspecified, 0, [])); }
        }
        return result;
    }

    private static FailureKind Kind(Exception error) => error switch
    {
        OperationCanceledException => FailureKind.Cancelled,
        FileNotFoundException or DirectoryNotFoundException => FailureKind.FileMissing,
        UnauthorizedAccessException or System.Security.SecurityException => FailureKind.AccessDenied,
        InvalidDataException => FailureKind.InvalidData,
        IOException => FailureKind.Io,
        InvalidOperationException => FailureKind.InvalidOperation,
        ArgumentException => FailureKind.Argument,
        System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException => FailureKind.NativeInterop,
        OutOfMemoryException => FailureKind.OutOfMemory,
        _ => FailureKind.Unexpected
    };

    private static FailureReason Reason(Exception error)
    {
        // Inspect known phrases solely to select an enum; the message itself never leaves this method.
        var message = error.Message;
        if (message.Contains("missing, duplicated or incomplete", StringComparison.Ordinal)) return FailureReason.DistributionIncomplete;
        if (message.Contains("Missing or inactive master", StringComparison.OrdinalIgnoreCase) || message.Contains("Skyrim.esm was not found", StringComparison.Ordinal)) return FailureReason.MissingMaster;
        if (message.Contains("output folder is busy", StringComparison.Ordinal)) return FailureReason.OutputBusy;
        if (message.Contains("earlier output update was interrupted", StringComparison.Ordinal)) return FailureReason.InterruptedUpdate;
        if (message.Contains("rollback could not finish", StringComparison.Ordinal)) return FailureReason.RollbackFailed;
        if (message.Contains("previous generated files have been restored", StringComparison.Ordinal)) return FailureReason.PreviousOutputRestored;
        if (message.Contains("Backup integrity verification failed", StringComparison.Ordinal)) return FailureReason.BackupIntegrity;
        if (message.Contains("other distribution framework", StringComparison.Ordinal)) return FailureReason.FrameworkConflict;
        if (message.Contains("only parts", StringComparison.Ordinal) || message.Contains("female-only and male-only", StringComparison.Ordinal)) return FailureReason.OutfitSexRestriction;
        return FailureReason.Unspecified;
    }

    internal static string CreateReport(bool latestErrorOnly = false)
    {
        lock (Gate)
        {
            var entries = ReadEntries();
            if (latestErrorOnly) entries = entries.Where(entry => entry.Level != DiagnosticLevel.Information).TakeLast(1).ToList();
            var text = new StringBuilder("Skyrim SPID Patch Generator — shareable error report\nSchema: 1\n");
            text.AppendLine("Current app build: " + Build);
            text.AppendLine("Privacy: fixed event/reason codes, relative uptime and app method tokens only. No paths, usernames, exact dates, mod lists, settings, free text or raw exception messages.");
            text.AppendLine("Recent bounded history only. Older application-*.log files, scan text and game logs are never imported.");
            text.AppendLine(PersistenceAvailable ? "Log storage: available" : "Log storage: unavailable; this report also includes errors retained in memory.");
            if (entries.Count == 0) text.AppendLine("No matching errors were recorded in the available history.");
            foreach (var entry in entries)
            {
                text.AppendLine();
                text.AppendLine($"APP-{(int)entry.Event} {entry.Event} [{entry.Level}] · build {entry.Build} · session {entry.Session:N} · event {entry.Sequence} · uptime {entry.ElapsedMilliseconds} ms");
                foreach (var failure in entry.Failures)
                {
                    text.AppendLine($"  {failure.Kind} · {failure.Reason} · HRESULT 0x{unchecked((uint)failure.HResult):X8}");
                    foreach (var frame in failure.Frames)
                        text.AppendLine($"    App method 0x{frame.MethodToken:X8} · IL {frame.IlOffset}" + MethodLabel(entry.Build, frame.MethodToken));
                }
            }
            return text.ToString();
        }
    }

    private static string MethodLabel(string build, int token)
    {
        if (build != Build) return "";
        try
        {
            var method = typeof(SupportLog).Module.ResolveMethod(token);
            // Names are regenerated from our compiled metadata, never trusted strings from a log file.
            return method?.Module.Assembly == typeof(SupportLog).Assembly ? " · " + method.DeclaringType?.FullName + "." + method.Name : "";
        }
        catch { return ""; }
    }

    private static List<SafeDiagnostic> ReadEntries()
    {
        var entries = new List<SafeDiagnostic>();
        try
        {
            if (Directory.Exists(DirectoryPath))
                foreach (var file in Directory.EnumerateFiles(DirectoryPath, "support-*.jsonl").Where(IsManagedFile).OrderBy(File.GetLastWriteTimeUtc).TakeLast(MaxFiles))
                {
                    try
                    {
                        if (new FileInfo(file).Length > MaxFileBytes + MaxRecordBytes) continue;
                        using var reader = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
                        while (reader.ReadLine() is { } line)
                        {
                            if (line.Length > MaxRecordBytes) continue;
                            try { if (JsonSerializer.Deserialize<SafeDiagnostic>(line) is { } entry && Valid(entry)) entries.Add(entry); }
                            catch (JsonException) { /* Incomplete final records after a crash are skipped, not echoed. */ }
                            if (entries.Count > MaxEntries * 2) entries.RemoveRange(0, entries.Count - MaxEntries);
                        }
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { Record(DiagnosticEvent.SupportFileReadFailed, error, DiagnosticLevel.Warning); }
                }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Record(DiagnosticEvent.SupportFileReadFailed, error, DiagnosticLevel.Warning); }
        entries.AddRange(Memory);
        return entries.DistinctBy(entry => (entry.Session, entry.Sequence)).TakeLast(MaxEntries).ToList();
    }

    private static bool Valid(SafeDiagnostic entry) => entry.Schema == 1 && Regex.IsMatch(entry.Build ?? "", @"^\d{1,5}\.\d{1,5}\.\d{1,5}\.\d{1,5}$") &&
        entry.Session != Guid.Empty && entry.Sequence > 0 && entry.ElapsedMilliseconds >= 0 && Enum.IsDefined(entry.Event) && Enum.IsDefined(entry.Level) &&
        entry.Failures is { Count: <= 8 } && entry.Failures.All(failure => failure != null && Enum.IsDefined(failure.Kind) && Enum.IsDefined(failure.Reason) &&
            failure.Frames is { Count: <= 12 } && failure.Frames.All(frame => frame != null && (frame.MethodToken & unchecked((int)0xFF000000)) == 0x06000000 && frame.IlOffset >= -1));

    private static bool IsManagedFile(string path) => Regex.IsMatch(Path.GetFileName(path), @"^support-[0-9a-f]{32}-\d{4,}[.]jsonl$", RegexOptions.CultureInvariant);
    private static void TrimFiles()
    {
        foreach (var old in Directory.EnumerateFiles(DirectoryPath, "support-*.jsonl").Where(IsManagedFile).OrderByDescending(File.GetLastWriteTimeUtc).Skip(MaxFiles))
            try { File.Delete(old); } catch { /* Keep logging if retention is temporarily unavailable. */ }
    }

    internal static void SaveReport(string destination) => File.WriteAllText(destination, CreateReport(), new UTF8Encoding(false));
}
