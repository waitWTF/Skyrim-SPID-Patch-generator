# Architecture and implementation notes

## Entry and interface

`src/WearableDistributor.csproj` is the only app project. WPF runs on .NET 10 Windows. `App` loads `UiTheme.xaml` and records startup/exception diagnostics. `MainWindow.xaml` defines the library, generated-patch inventory and settings shell. `PatchOptionsWindow` implements a scrollable FOMOD-style wizard; `PatchReviewWindow` summarizes target estimates, conflicts and slots before writing. `UiTheme.EnableScrollChaining` lets nested lists/grids hand wheel events to the containing page at their edges.

## Scan snapshot

`WearableScanService.Scan` performs one background discovery pass, skips large asset trees, validates source master dependencies, determines plugin order, and reads Mutagen binary overlays. It stages all records from each plugin before committing that plugin's effective snapshot; an unreadable plugin cannot leave partial overrides behind. Winning NPC/race/target/package/placed/quest/leveled records are retained by FormKey.

With a profile list, implicit official masters and installed content listed by Skyrim.ccc precede enabled plugins.txt entries. Without a profile list, master dependencies and filenames supply an explicitly inferred order. Vortex staging duplicates are diagnosed; this app does not control manager priority or virtual filesystem internals.

Direct NPC templates and nested leveled templates resolve conservatively. Every possible branch must be adult and humanoid; an optional group/role/race/sex filter must match every possible branch before an undifferentiated NPC base can be included. Missing/cyclic/unbounded branches are excluded with diagnostics. Positive placed, reachable leveled or quest links establish candidate encounter evidence. This is an inference: quest links do not prove an actor will spawn, and scripts can create actors without a discoverable record reference.

## Audiences

`NpcTargetingService` constrains groups and role/race menus to the selected collection's effective population. Classes/keywords/factions are evidence for familiar groups; exclusion factions do not positively establish an occupation. Farm work uses occupation/faction/package evidence. Human-readable class/race labels retain underlying record identities in tooltips and persistence.

Alternatives within one category are OR; categories within an audience are AND; separate audiences are OR. NPC collection refers to the plugin defining the base NPC, independent of the plugin defining its class/faction or the plugin supplying a winning override. The wizard saves semantic collection/group/refinement choices and repairs stale selections rather than silently dropping required filters. Resolved recipient lists are regenerated before patch review/generation.

## Output compiler

`DistributionRuleCompiler` accepts normal resolved NPC lists or bounded native SPID filter configurations for legacy/API use. Normal SPID output unions audiences and emits one Outfit rule per named outfit at the chosen chance. It adds a runtime adult trait (-C). Native fallback uses namespaced helper keywords to represent boolean groups without a Cartesian product of chance rolls. SkyPatcher uses Plugin.esp|hexID identifiers and explicit NPC base lists, with 100% assignment.

SPID can match NPC filter forms against some original/template base forms too. Therefore recipient counts remain estimates. SkyPatcher template inventory/outfit inheritance remains a runtime testing area. Native JSON settings, generated game rules, and actual game state are separate: settings are not a deployable patch.

## Integrity and recovery

`PatchGenerator` preserves stable outfit records, updates selected source plugins, removes stale variants, merges generated INI sections, and validates staged ESP/INI/manifest together. `PatchInventoryService` imports manifest/legacy inventory, removes individual variants and restores backups. `PatchHealthService` detects missing generated files, records, distribution sections and removed source armor parts. `PatchPackageService` exports only generated files and installation instructions to a mod-manager ZIP, with byte-hash verification.

`PatchOutputTransaction` provides an output-folder lease, managed path checks, preflight file readiness, checksummed recovery snapshots, staged replacements and rollback. All restore sources are verified before touching any destination. Multi-file atomicity and power-loss recovery are not guaranteed by Windows; interrupted operations retain a recoverable snapshot. Only one Wearable Distributor output mod should be enabled, since output folders share the same ESP filename.

`PreferencesService` normalizes/imports settings, writes atomically, and keeps .bak. Existing corrupted settings are preserved until an explicit user save/import. UI tests override the settings directory and disable auto-scan to avoid changing real preferences. Integration checks override LOCALAPPDATA for isolated output backups.

## Development tools

- `tests/WearableDistributor.IntegrationChecks.csproj`: synthetic binary/file integrity regressions, including independent scan/output audits.
- `tools/UiPreview`: sample-data interactive wizard, minimum-window layout checks, WPF preview rendering and optional read-only installed-mod scan audit.
- `tools/Publish-Release.ps1`: source+app packaging, archive verification and SHA256 files; no publishing or network transmission.

No source changes are committed/pushed automatically. The repository contains historical staged deletions/build folders from earlier iterations; preserve unrelated local work. Build artifacts are ignored, and release packaging copies only source/docs/tests/tools and fresh published companion files.
