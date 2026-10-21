# Architecture and implementation notes

## 0.4.1 branding and merchandise

Public application name: Skyrim SPID Patch Generator; AssemblyName: SkyrimSPIDPatchGenerator. The source project/namespace and legacy saved/output filenames remain compatible with older versions. Project-reference assets must be restored after the AssemblyName change.

The scanner stages vendor factions, form-list identifiers, container records and placed-container links with the rest of each effective plugin snapshot. Eligible NPC faction membership establishes a used vendor faction. Its merchant reference resolves to a CONT base, classified from faction/buy-sell-list/container EditorIDs. Unrecognized categories, fences marked only-buy-stolen and known bases shared with non-vendor references are excluded. This is static inference, not a guarantee of a working shop in game.

Source-owned playable WEAP records are separate from ARMO pieces. MerchantStockService builds per-entry stock metadata; clothing traders never receive weapons, while their outfit pieces remain included. A known shared clothing stock base also excludes weapon additions. The NPC outfit compiler never sees weapon pieces.

The manifest stores merchant/container/item IDs and categories, enabling complete regeneration across merged sessions and removal. Deterministic addOnceToContainers rules target container bases in a separate SkyPatcher container INI, with one copy for new entries and deduplication across outfit variants. This optional feature requires SkyPatcher even when the NPC engine is SPID. Its output is included in managed-path validation, transactions, backups, older-snapshot restoration cleanup, health and ZIP packaging. Merchandise settings/manifests require schema 3; older consumers refuse them.

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

## Matching garment variants (0.4.10)

OutfitVariantPlan groups exact slot sets by conservative normalized names (color annotations and recognized damage words removed). Unknown overlapping families fail review. One selector picks a color; its UseAll bundle references one selector per garment. Required selectors have ChanceNone=0. Optional selectors share a group chance, then select one version. Color-specific members take priority over unmarked fallbacks; shared accessory families are available in every compatible branch.

VariantPartLists writes deterministic hashed EditorIDs, reuses existing records, clears chance globals and validates the exact topology against the saved branch/part metadata. OptionalPartLists traverses nested links for pruning and validates old fixed/optional outfits alongside new plans. Managed matching plans require manifest schema 6. Merchant stock continues to use the flat included item set independently of NPC roll logic.

## Random outfit pools (0.4.11)

WardrobePoolWindow explicitly copies a shared resolved audience, sex and coverage to selected PluginPatchOptions. WardrobeName and WardrobeWeight persist through SavedDistributionOptions. Names are Unicode-normalized, case-insensitive pool identities scoped to one output. Audience presets never implicitly join an unrelated outfit to a wardrobe. UI views are discarded after a pool edit so stale controls cannot overwrite updated models.

WardrobePools builds one hashed SSPG_Wardrobe_ OTFT per pool. Its single non-UseAll LVLI selects one ticket, with each complete member UseAll LVLI repeated according to weight. These bundles reference member outfit item links (ARMO or nested LVLI), never OTFT records. All entries use level/count 1, zero ChanceNone and no chance globals. At most 20 members × 10 tickets keeps the selector below Skyrim's 255-entry limit. Existing member OTFT records remain editable inventory anchors; they receive no independent NPC outfit rule.

Schema 7 member entries store name, weight, resolved target rules and a deterministic owner flag. The lexicographically first member EditorID owns the sole shared rule in its ordinary generated section; other member sections contain comments. Every generation merges member entries first, rebuilds retained pools from the complete manifest, reuses FormIDs and revalidates exact graph/rules before the normal transaction. Partial scope changes fail closed. Removing a member repeats pool rebuilding so ownership transfers; one surviving member remains valid, no survivors removes the pool. Generic recursive pruning handles both garment and wardrobe lists.

Validation checks exact selector flags/entries/weights, complete member bundles, owner uniqueness, individual-rule absence, complete shared rule/audience/chance and orphaned managed lists. File discovery follows member graphs and marks the shared OTFT recorded so it is not recovered as duplicate source outfits. Missing schema-7 inventories with wardrobe comments cannot be migrated using the legacy heuristic; restore matching backups. Merchant stock remains flat and independent. No daily scheduler or runtime code is added.
