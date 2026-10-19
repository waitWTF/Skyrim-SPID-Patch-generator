# Error reporting and repatching — 0.4.6

## Sending a bug report

1. Reproduce the problem in the current version.
2. Click **Error reports** at the top of the main window.
3. Click **Save report…** and attach the resulting text file, or use **Copy latest error** to paste the latest recorded warning/error. Preview is available before sharing.
4. Describe the action that failed in your own words. The app does not add your description or other free text to its logs.

Nothing is sent automatically. The same window is available through the diagnostics section of Settings. **Open log folder** opens only the new `SupportLogs` folder. If disk logging fails, recent events remain in memory where possible; save/copy them before closing the app. Fatal UI errors display the error code and the new log location when disk logging succeeds.

Older `Logs/application-*.log` files may contain local paths. They are preserved locally, never imported by the new report exporter, and should not be treated as privacy-safe attachments. The new app no longer writes that raw format. Already-running older executables continue to use their own logger until replaced/restarted.

## Privacy contract

SupportLog accepts a **DiagnosticEvent enum**, an Exception for classification, and a fixed severity enum. There is no arbitrary-message/context parameter. Typed records contain only:

- schema and four-part app version;
- random per-launch session GUID (not a persistent user/device identifier);
- event sequence and relative app uptime, with no calendar timestamps;
- fixed event, severity, failure-kind and failure-reason enums;
- numeric HRESULT and own-assembly method metadata tokens/IL offsets;
- a bounded list of similarly projected inner exceptions.

Exception messages are examined only to select a known reason enum, such as DistributionIncomplete or OutputBusy. Messages themselves, Exception.Data, ToString(), parameter values, source-file information and external stack text are never serialized. Application frames use StackTrace with file information disabled. The text exporter resolves method names from the current compiled assembly only; it never trusts names from disk. Older-build tokens stay numeric for later analysis against the matching app build.

Paths, usernames, profile names, machine/hardware identifiers, account details, environment values, source plugin names/mod lists, outfit/NPC names, settings, screenshots, scan text, game logs and user comments are not support-log fields. Export re-deserializes/revalidates the typed schema and projects known fields; unknown extra JSON fields cannot be copied into a report. Build strings are restricted to numeric version syntax; enums and frame shapes are checked. Unreadable or truncated records are skipped. The app does not copy raw log files into a ZIP or attach arbitrary files.

This contract applies to error logs/reports. Working settings necessarily remember chosen folders, generated patches describe outfits, recovery snapshots contain output metadata, and intentional **Export settings** / **Export patch contents** exports contain user-selected data. Those files are not included in error reports. Local UI messages and the on-screen scan panel can show paths/names for the user's own troubleshooting.

## Coverage and retention

Captured boundaries include UI/process unhandled managed exceptions, unobserved task exceptions, WPF binding errors, caught scanning/parser/read failures, invalid master dependencies, location-discovery failures, preference load/save/import/export, patch generation, inventory/health, removal, backup/restore, packaging, rollback, cleanup, and failed outfit/audience validation. Expected cancellation is not an error. Diagnostics-code maintenance deliberately does not recursively log failures inside its own fallback/formatting routines.

Files roll at about 1 MiB; twenty managed files are retained. Reports and the memory fallback contain at most 2,000 recent entries. Older entries may therefore have expired. Records flush synchronously when written to reduce loss on ordinary crashes. The log location is the existing local application settings directory plus `SupportLogs`; the development settings-directory override remains supported. Location paths are not logged as record data.

A missing .NET runtime, a native crash before application startup, forced process termination, power loss or unrecoverable memory/disk exhaustion may prevent logging. This is not a Windows crash-dump or game-crash collector. Reports can identify the app operation and failing code without a mod list, but cannot always identify a particular third-party data file; ask for narrowly scoped extra information when necessary rather than silently expanding collection.

## Repatching changes

- **Edit / repatch selected** opens only the chosen named outfit. Other variants of that plugin remain installed.
- **Repatch all** loads the names recorded in My patch. When every saved setup restores and validates, the app goes directly to a combined review. Missing/stale/incompatible setups open the wizard at the affected page. Back to setup remains available from review.
- A saved existing output folder is reused. Review shows the destination and update scope before the final Generate click. Change folders in Settings when a different output is wanted. The redundant second merge confirmation was removed.
- Replacements are keyed by source plugin and outfit name, with case-insensitive matching. Skips and untouched entries remain. Removing a saved wizard setup does not uninstall its generated outfit; My patch → Remove outfit is explicit removal.
- Existing readable records are reused, including a recognized legacy record, preserving FormIDs. New allocations keep the original allocation history.
- The generator no longer requires a selected entry's old distribution section to be intact before rebuilding it. It replaces matching sections and recognizable rows referencing the selected outfit IDs, then validates the **complete staged** ESP/INI/inventory/merchandise set before backup/commit. It does not disable the final integrity checks.
- Incomplete selected rules, duplicated selected sections, missing selected records where reconstruction is possible, and lost selected comment headers can be repaired. If the shared NPC INI is absent, all entries needing it must be regenerated together. Corruption of untouched entries still blocks the update. Missing/unreadable ESPs and unparseable inventories require restoring a backup or generating into a new mod folder.
- Unowned or malformed rows are retained for validation to reject rather than guessed/deleted. Duplicate inventory identities, wrong outfit references, missing helper keywords and extra managed outfit rows are checked. Recovery is not a promise to repair every manually edited INI.
- GeneratedDistributionIndex reads/partitions each INI once per operation, providing direct section lookups. Health checks reuse the index. Legacy migration reads the ESP once. Closing setup saves the complete batch in one preferences write; individual page changes still save immediately. No performance benchmark is claimed.

The historical repatching error recovered during this work was an InvalidDataException reporting missing/duplicated/incomplete distribution rows during old-output validation. The log alone does not establish why the rows differed or that guard edits caused the discrepancy. The new repair path addresses that class of selected-entry failure; regenerating old outputs also adopts current uniform protection and sex settings only for the entries actually rebuilt.

## Maintainer notes

Relevant files: SupportLog.cs, SupportReportWindow.cs, App.xaml.cs, MainWindow.xaml/.cs, GeneratedDistributionIndex.cs, PatchGenerator.cs, PatchHealthService.cs and PatchInventoryService.cs. New catch handlers must call the enum-based logger; never append raw exceptions or diagnostics text to SupportLogs. Additional context requires a reviewed, fixed enum/number field, not an open-ended string dictionary. Do not bundle legacy logs, settings or game data into support exports.

The .NET 10 Windows project builds successfully. No new automated tests, UI/runtime checks, installed-output repairs or in-game tests were run for this update. Existing game output was not modified during development. Documentation screenshots still show an older interface.
