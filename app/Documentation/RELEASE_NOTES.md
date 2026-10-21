# 0.4.11 preview — 2026-10-21

- Added opt-in Random outfit pools: a shared overall NPC chance, 2–20 outfit members, weights 1–10 and live selection percentages. Equal weights provide equal complete-set odds rather than competing individual SPID rules.
- Added a pool editor to Distribution. Choose which outfit supplies the shared NPC groups/sex; apply copies that scope and overall chance to checked members while preserving their parts and trader settings. Remove checked membership explicitly.
- The generated wardrobe OTFT points to one weighted LVLI selector of complete UseAll item bundles; no LVLI references an OTFT. Member color/garment selectors and optional-part chances remain nested within their own bundle.
- Saved pool membership/weights across sessions and exposed the wardrobe, weight and shared chance in My patch/review. Standalone setups remain unchanged unless opted in.
- Incremental repatching rebuilds pools from the merged inventory, retaining unselected members and stable FormIDs. Removing a rule-owning member transfers its one shared rule; empty pools and orphaned lists are pruned. Transactional backups cover the complete patch.
- Added validation for mismatched audiences/sex/coverage, unsupported SkyPatcher NPC pools, invalid weights, broken selectors, competing individual member rules and missing pool inventories. Pool patches/settings use schema 7; older schemas remain readable, but pool output needs 0.4.11+ to edit.
- Daily clothing rotation is not included. Use SPID 7.2+ for per-actor outfit distribution; other distributors/scripts may still compete. Trader merchandise is independent of wardrobe weights/coverage.
- Automated binary/merge/removal/settings and WPF workflow checks cover the new feature. No in-game wardrobe claim is made.

# 0.4.10 preview — 2026-10-20

- Matching variants: generate one color selector, complete color bundles, and one-choice garment lists. Recognized normal/torn/ripped/tattered versions become alternatives; one Required version is guaranteed after a successful overall outfit roll. The same garment's colored version is preferred in its branch; shared unmarked components remain available without mixing marked colors.
- Keep all recognized colors available by default. Explicit color selection is retained. Recognized belts/ponchos/accessories get the chosen preset's optional chance; unknown coverage-critical garments stay required. Incomplete required color sets and unknown overlaps are blocked for review.
- Prefer separate garments over combined Outfit/Full records when a matching modular alternative exists. Do not hide a sole combined outfit. Saved automatic presets can migrate for review; Custom choices stay explicit.
- Recognize Rogue's square-bracketed Dark labels and normalize parenthesized/plain multi-word colors. Marked lewd variants and wigs remain excluded unless explicitly enabled.
- Add a matching-variants checkbox, explain Required alternative groups, and synchronize the state/chance of included versions when editing a garment group. Review and patch health understand mutually exclusive variants.
- Preserve nested lists and stable FormIDs on incremental patching; recursively prune only unused managed lists. Validate list flags, chance, entries, links and saved group membership before committing output. Schema 6 supports matching-variant metadata; schemas 1–5 remain readable.
- Replace dense yellow error paragraphs with spaced summary messages, a readable heading and expandable/copyable part/file details. Detect built mesh files mistakenly placed under meshes/meshes and explain the output-root correction. Missing meshes still block unsafe generation.
- General installed-data investigation found the same wrong BodySlide output root affecting 13 outfit mods. Their already-built files were repaired locally with backups; this asset/config repair is not bundled or applied automatically by the app.
- Tests cover garment/color plans, actual generated list topology, corruption detection, merges, removals, saved state, missing meshes and WPF workflow/layout. See docs/RELEASE_VERIFICATION_0410.md for scope and limits. No new in-game visual test is claimed.

---

# 0.4.9 release build — 2026-10-20

- Added worn-mesh availability checks for effective ArmorAddon paths, weighted _0/_1 pairs, loose files and BSA archive directories. Missing/empty/unsafe mesh paths block affected selected parts. Trader stock checks supported mesh paths independently of the NPC sex filter. Existing managed output health flags unavailable worn meshes. Staging/unreadable archives remain uncertain, rather than treating all absent loose files as missing.
- Corrected color grouping for ordinary suffixes such as Armor Black / Boots Black, with the main garment determining the default color. Prevents mixing alternative colors into an NPC outfit. Existing slot conflict checks reject competing garments.
- Legacy all-parts setups without explicit part choices select a matching Balanced set for review, retaining recipients/chance. The EXE cannot build/deploy missing outfit assets itself.
- Compatibility fix: an older merchants-only entry may lack an empty NPC comment block. Validated shop rules and zero NPC references remain mergeable; unexpected NPC rules still fail validation.
- Promoted the preset branch into the released application/output family. EXE name is SkyrimSPIDPatchGenerator.exe; existing WearableDistributor settings, ESP/INI names, backups and stable records are retained. Experimental outputs remain read-only and cannot be merged into released output.
- Outfit library tabs: Armor & clothing, Mixed / hidden, Already patched. Classification uses equipment/support records; official/DLC/CC content is excluded. Discover generated patch contents independently of the selected output folder and inspect multiple output locations.
- Scans start unchecked. Right-click Configure this outfit opens only the clicked plugin regardless of other checks. Configure checked (N) and the outside-view counter make batch scope explicit.
- Clearer Add NPC group, Save & close, and Review & generate workflow; skipping and saved choices retained. Wizard pages are lazy; cached population/prepared dropdown evidence avoids repeated classification. Repatch handlers await setup before restoring temporary scope.
- Part presets: Complete set, Balanced, Light accessories, Essentials only, color selection, headgear/wig exclusions, explicit lewd opt-in and editable required/optional/excluded states. Optional parts use generated outfit leveled items. Merchant stock percentages remain independent.
- Loading icon startup correction and animated preparation indicator. Removed the experimental resize-cursor hook after the issue was traced to the user's Windows pointer scheme; normal Windows cursors are used.
- Hardened damaged/null inventory and truncated-ESP discovery so one broken output does not hide other patch libraries. Strict write validation preserves previous outputs on failure.
- Verification details and limits: docs/RELEASE_VERIFICATION_049.md. The reported invisibility was investigated using installed records/assets and a copy of an existing patch. No claim of a new in-game distribution test is made.

## Local development iterations included in 0.4.9

The following entries record the separate test builds, not public stable releases. Their historical verification notes apply to those iterations.

# 0.4.9 preset test 7 — 2026-10-20 — UNRELEASED EXPERIMENT

- Fixed all eligible plugins being checked automatically: scans now start with an empty batch.
- Added right-click Configure this outfit, scoped to that row regardless of other checks, highlighting or search.
- Configure checked (N) explicitly shows batch scope; the sidebar flags checked plugins outside the current search/tab.
- Renamed Add audience to Add NPC group, with an explanation of existing NPC recipients. Added clearer Save & close and Review & generate actions.
- Removed the unsuccessful test-6 cursor workaround after the user confirmed repairing the Windows pointer scheme resolved the issue in both versions.
- Cached immutable NPC record-name evidence and collection/faction labels; protection labels no longer expand full recipient rules. Repatch handlers await setup before clearing temporary scope.
- Test results and measurements: docs/TEST_RESULTS_TEST7.md. Kept as a separate local test build.

---

# 0.4.9 preset test 6 — 2026-10-20 — UNRELEASED EXPERIMENT

- Candidate fix for the reported cursor disappearing only at resize borders/corners: explicitly select the corresponding Windows sizing cursor for each of the eight native sizing hit regions.
- Fall back to the arrow if the resize resource cannot load; leave Windows handling intact if neither resource loads.
- Ignore client controls, title bar, child-window messages, maximized/minimized windows and nonresizable windows. Detach hooks when windows close. No global cursor overrides or Windows settings changes.
- Interface, distribution, patch-generation and scanning behavior unchanged. Visual confirmation of the reported disappearance remains pending.

---

# 0.4.9 preset test 5 — 2026-10-20 — UNRELEASED EXPERIMENT

- Bug-fix/testing iteration; the test-4 interface, features and workflow remain unchanged.
- Fixed null inventory collections/entries interrupting discovery of other output libraries. Incomplete metadata is flagged and inspected read-only; strict write loading rejects empty/incomplete entries before committing changes.
- Fixed truncated/generated ESP parser errors escaping the per-library discovery boundary and hiding healthy patch locations.
- Restored the UI harness's existing isolated-settings override and corrected assembly-qualified theme/icon resource paths. Source packages now include the regression tests.
- Existing integration suite passes, plus 33 current regression scenarios, 18 WPF workflow checks and four small-window layout/accessibility checks. Direct desktop launch and read-only installed-data scan/cache measurement passed. Details and limits: docs/TEST_RESULTS_TEST5.md.
- Live MO2 launch check was canceled/skipped at user request. Synthetic active-profile/implicit-master checks pass; existing CET compatibility preserved. No Windows security changes or Skyrim/SKSE/in-game tests.

---

# 0.4.9 preset test 4 — 2026-10-20 — UNRELEASED EXPERIMENT

- Added Armor & clothing, Mixed / hidden and Already patched tabs. Equipment-only plugins remain visible after patching. Mixed mods can be explicitly included; manual exclusions remain reversible.
- Replaced selection-list title heuristics with record-content classification. Equipment, textures/keywords, addons and connected crafting/enchantment/list/effect support are allowed; unrelated records and NPC/quest/world/gameplay additions hide a plugin by default. Bundled weapons are supported; weapon-only plugins are not outfit candidates.
- Official Skyrim/update/DLC/CC/resource files are excluded from selection, including hidden views. Existing third-party SPID distribution coverage is informational and no longer hides an otherwise equipment-only plugin.
- Generated patch discovery is independent of the current output destination. Inspect known generator ESP/INI/manifest files found in scan roots, game Data, detected Vortex staging locations and discovered MO2 instance mod libraries. Find patch files supports custom locations and remembers explicit files.
- My patch aggregates separate output libraries and shows each patch location. Already patched sources are established from actual generated ESP outfit and optional leveled-item links, not remembered UI choices or master references. Manifestless outputs can be recovered for read-only viewing; incomplete file sets are flagged.
- Released/recovered patch files are read-only in the separate experimental app. Editable entries route actions to their own experimental output location; Repatch all is scoped to that library. File presence does not establish profile activation, deployment or in-game behavior.
- Added installed Vortex staging candidates to folder discovery. When Data contains only official content and no active profile is configured, scanning can fall back to a detected staging root, with explicit staging/disabled-content diagnostics.
- Patch discovery/reading runs off the UI thread; reuse the scan's file discovery on initial inventory loading. Retained audience caches, animated loading indicator, startup correction and experimental part presets. Build labels now display the actual experimental version.
- Compiled/packaged only. No automated tests, runtime app launches or in-game checks performed.

---

# 0.4.9 preset test 3 — 2026-10-20 — UNRELEASED EXPERIMENT

- Fixed the startup InvalidCastException in LoadingIcon reported in build 0.4.9.2 (APP-1002, HRESULT 0x80004002). Geometry.Parse can return StreamGeometry; explicitly construct the PathGeometry required by the branch animation instead of casting.
- Recoverable animation-start errors now stop the animation and record UI icon warning APP-1601, leaving the static icon available.
- Experimental build script now installs completed builds into the requested the local experimental installation folder folder automatically. Previous application files are backed up, failed replacement attempts restore moved files, and a running installed test build is not overwritten. `-SkipInstall` supports packaging only.
- Retained the first-opening audience preparation, shared per-scan cache, deferred outfit controls and all experimental preset features from test 2.
- Compiled and packaged only; no automated tests or app/game launches performed.

---

# 0.4.9 preset test 2 — 2026-10-20 — UNRELEASED EXPERIMENT

- Prepare NPC groups, roles, races, all three sex-specific preset lists, and readable advanced targets on a background worker when outfit setup first opens. Reuse the prepared data across pages and reopened setup windows for the same scan. A successful rescan invalidates it; no persistent disk cache is used.
- Added an animated version of the app icon: light travels from the garment along the three distribution branches. Preparation displays the actual stage and elapsed time. Startup and main scan/generation progress use the same icon; reduced motion settings are respected.
- Create outfit controls only when a page is visited. Saving unvisited pages uses their restored model data without constructing controls. Advanced target lists attach only when expanded.
- Cache group membership, collection group labels, roles/races and audience matches. Match cache signatures include the current filters, group, collection and sex; selecting explicit NPCs starts matching from that bounded population while retaining all protection/template/collection checks.
- Named group saves omit their expanded NPC audience IDs, keeping required role/race/individual refinements. Groups are resolved dynamically on restore, as before. Custom audiences retain explicit targets. This reduces repeated settings serialization during setup.
- Reuse the prepared targeting service for patch review and overlap counting. Initial scanning is scheduled after the workspace renders; baseline-folder discovery runs on the scan worker.
- Build/package only: no automated tests, app launches or measured timing comparisons. Large modlists can still take time during first preparation, scans and generation. Experimental separation from v0.4.8 remains unchanged.

---

# 0.4.9 preset test 1 — 2026-10-20 — UNRELEASED EXPERIMENT

- Separate experimental app based on released 0.4.8. The release checkout and installed release are not replaced.
- Added Complete set, Balanced (default), Light accessories, Essentials only and Custom buttons above the outfit parts list.
- Added Required / Optional / Excluded states, a required checkbox, per-part inclusion percentage and a bulk optional-chance control.
- Required parts are direct outfit items. Optional parts use one-entry Skyrim leveled lists with Chance None = 100 minus inclusion chance. Excluded parts are absent. The overall SPID distribution chance is unchanged and rolled once per generated outfit; no extra SPID outfit rules are generated per optional part.
- Presets choose one compatible color/slot combination, retain main and uncertain clothing as required, exclude wigs and recognized lewd alternatives, and warn about modular clothing, missing main garments and omitted overlapping alternatives.
- Added No headgear, No wigs, matching-color selection and Reset to Balanced controls.
- Clearly named lewd variants are marked in the parts list. Normal presets/reset do not select them. An extra Use lewd variant button appears only where such records were detected, and requires explicit confirmation. Saved explicit opt-in is remembered for that setup. Detection uses names/EditorIDs; unmarked items are not certified non-lewd.
- Saved setups and named duplicates retain part states, percentages, preferred color and explicit lewd opt-in. Preset/part changes appear in live Activity and My patch details.
- Output read-back validates optional records, links and percentages. Repatching reuses stable optional records and prunes stale ones; removal and backup/export paths retain the complete ESP.
- Independent EXE name, settings/log/backup root and output filenames prevent overwriting the released patch. Released output folders are refused; importing released settings clears a detected released output path.
- Shop stock still gets one copy of every included part. Part percentages apply to NPC outfits only. SkyPatcher NPC assignment still requires 100% overall; optional piece lists do not add per-actor SkyPatcher distribution chance.
- Built and packaged. No automated tests or app/MO2/Vortex/Skyrim launches were performed for this experimental build. File validation runs when users generate a patch.

## Limits of this experiment

Optional leveled-list items may be re-evaluated by game outfit/inventory resets. This experiment does not promise permanent per-actor optional choices or exact loot rates. Clothing dependencies and lewd appearance cannot be established from names/slots alone. Review warnings and source-mod requirements. Disable a released generated distribution mod while trying experimental output on the same recipients.

---

# Released baseline history (unchanged)

# 0.4.8 development preview — 2026-10-19

- Added independent tickboxes for NPC outfits and trader sales. Tick only trader sales to generate merchants-only stock, with no NPC audience, sex or chance required. The explanatory note is beside the checkbox. New setups start with neither destination selected; existing saved setups keep their destination choices. Available shop categories default on; unavailable categories are disabled unless an older saved choice needs attention.
- Merchants-only entries generate actual SkyPatcher container rules and zero NPC distribution rows. Review, inventory, merging, validation, removal and installable archives support them. Switching an existing outfit to shop-only removes its managed NPC rules. Requires SkyPatcher container patching; clothing traders still receive armor/clothing but never weapons. Settings/inventory schema 4 protects merchants-only data from older app versions.
- Added a live Activity window accessible from the main window and wizard. Shows app actions, scan/generation stages, setup edits, list-checkbox/hide/unhide actions and outcomes. Latest 1,000 entries in memory only, with clear/follow controls. It may show plugin names/paths locally; it is separate from safe Error reports and is never saved or uploaded.
- List buttons and right-click actions now use a visible scope: checked-or-highlighted rows (default), checked-only, or highlighted-only. Only visible/search results are targeted; target counts are shown. Bulk check changes are remembered; None clears highlights too. Added a visible Unhide action and remembered explicit-unhide overrides for filtered/already-patched entries. Does not alter game plugin activation.
- Replaced the context menu's default outer chrome and separator template with matching dark templates to remove the white gutter/box. Completion now reports NPC and shop-stock rule counts separately and links to the actual merchant INI.
- Added populated faction/guild presets and role subcategories from current NPC data, including Thieves Guild, Dark Brotherhood, Companions, colleges, Dawnguard, Volkihar, Blades, Greybeards, civil-war factions, Vigilants, Thalmor, Penitus Oculatus, Silver Hand, Morag Tong and Skaal. Membership uses effective faction links and scanned FormIDs; empty presets are omitted. Collection populations are cached for repeated audience matching.
- Existing uniform-protected members can be selected through their explicit faction preset, with Protected labels/counts and setup/review warnings. Only existing protection is marked; other guild members are not newly marked protected. Broad groups retain uniform protection, and guards/bodyguards/housecarls remain excluded even from faction presets.
- Added a Unique NPCs group using the winning NPC Unique flag, with optional individual selection and warnings about scripted outfits/quest scenes. Existing uniform protection remains in force for this group.
- Build/package verification only. No new automated tests, runtime UI/MO2/Vortex launches or Skyrim checks were run. Static faction/role evidence and the Unique flag cannot establish quest activation, mesh compatibility, merchant restock timing or compatibility with every scripted mod.

# 0.4.7 development preview — 2026-10-19

- Reorganized the Windows download into one `Skyrim SPID Patch Generator` folder: the EXE and START-HERE are at the top, required libraries/configuration are in Runtime, and README/changelog/license/guides are in Documentation.
- The root EXE is a direct .NET apphost targeting Runtime/SkyrimSPIDPatchGenerator.dll, with the original icon/version resources and MO2 CET compatibility opt-out retained. No second-process launcher, scripts or administrator requirement were added. Requires .NET 10 Desktop Runtime x64 as before.
- Updated release packaging to produce enclosing folders for both Windows and source ZIPs, include MSBuild layout targets in source archives, and check required archive members. Ordinary developer build/publish output keeps its normal layout.
- Included the root executable's folder in bounded location discovery so the library relocation does not remove an existing nearby-MO2 discovery origin. Settings, backups, error logs and generated patch filenames/locations are unchanged.
- Updated installation/upgrade guidance and corrected outdated README wording about retaining other named variants when repatching.
- Extract into a fresh folder and update the EXE path in MO2/Vortex. Keep Runtime beside the EXE; copying the EXE alone will not work. Build/package verification only; no automated tests, direct app launches, MO2/Vortex launch checks or Skyrim tests were run for this layout update.

# 0.4.6 development preview — 2026-10-19

- Added a main-window Error reports button with preview, Save report, Copy latest error and Open log folder. Automatic bounded logs cover app-level handled/unhandled failures, background-task exceptions, WPF binding errors, scan parsing, preferences, output transactions/rollback, cleanup and validation failures.
- Replaced arbitrary-text diagnostic logging with a typed allowlist: fixed event/reason enums, app build, random per-launch session ID, sequence/relative uptime, error categories, HRESULTs and own-assembly method tokens/IL offsets. No raw exception messages, paths, usernames, installed mod lists, settings, exact dates or automatic uploads. Exports rebuild known fields and never import older raw application logs or scan/game text. Logs roll at about 1 MiB, retain twenty files, and reports include up to 2,000 recent entries; memory fallback is available if disk logging fails.
- Changed the old scan-report export to open the shareable report UI. Detailed scan information remains local on screen; settings and patch-content exports are clearly separate features.
- Fixed the repatching dead end where pre-existing incomplete rules blocked regeneration of the selected broken entry. Selected sections are rebuilt; headerless rows can be replaced using known generated outfit IDs. The complete staged output must validate before any original files change. Unknown/untouched damage is rejected rather than guessed or discarded.
- Repatching now updates individual named outfits and retains other variants from the same plugin. Reuses recognized existing/legacy FormIDs. A missing shared INI can be rebuilt when all affected entries are selected; an unreadable/missing ESP or malformed inventory still needs backup recovery.
- Added My patch → Repatch all: valid saved choices go straight to review; new/stale/incompatible choices require setup. Single-entry editing opens only that named outfit. The remembered output folder is reused, review shows the destination, and the redundant merge-confirmation step is removed. Completion reports updated and retained counts.
- Indexed distribution sections once per file for validation/health checks; legacy inventory migration reads the ESP once. Added checks against duplicate inventory identities and generated outfit rows outside their managed sections.
- Build-only verification. No new automated tests, app launch checks or Skyrim tests were run for this update. Windows/native failures before managed startup or forced termination cannot always be captured.

# 0.4.5 development preview — 2026-10-19

- Reworked outfit defaults using Nexus outfit/SPID examples and local plugin evidence. Fixed hyphenated Creation Club filenames; added perk/magic/body system filters, source-content heuristics and numeric body-slot parsing. Source-owned playable clothing/armor can be included even for single-piece sets; large wardrobe collections with vendor NPCs are supported by a content-ratio heuristic.
- Existing SPID/SkyPatcher detection now reads supplied outfit/item operands, including SPID EditorIDs, and follows outfit/leveled-item links. Ignores this app's output, comments and NPC-filter-only references. Detection is evidence of some coverage, not proof of complete coverage.
- Added visible buttons and a dark right-click menu for enabling/disabling patch selection and remembered hiding. Enable/disable does not alter game plugin activation. Older inclusion overrides are reset once under the new filter policy; outfit setups and generated output are retained.
- Restored automatic hiding of successfully patched outfits with a separate Show already patched outfits toggle; manual exclusions and smart candidates have their own reveal controls. My patch remains available for editing; damaged entries are marked for review.
- Added a searchable grouped dropdown of common and installed-mod audience presets, recipient counts and bandit/forsworn/adventurer role variants. Only presets with actual eligible NPC matches appear. Mod-origin populations generate their own sections, including NPC mods with no hardcoded integration. Guards and uniformed protectors stay excluded.
- Added worn-model sex metadata from effective ArmorAddon overrides/templates. Confirmed one-sex selections lock NPC sex; incompatible saved presets warn, mixed-sex-only parts block, and generation enforces the same rule. Unknown support stays explicit. User-confirmed Lunar Claw family restriction is a fallback when model data cannot resolve support.
- Added an icon loading window using the embedded executable icon. Existing startup scanning, MO2 compatibility, patch merging and merchant output are retained.
- Build verification only: no new automated tests, UI launch checks, full-modlist scan checks or in-game validation were run for this update. Model paths cannot establish actual mesh/body/race compatibility; script-only NPC spawns and semantic naming remain limitations.

# 0.4.4 development preview — 2026-10-19

- Added Settings → Smart detect to find MO2 instances, their configured game Data and profile list, and installed Skyrim locations. Multiple choices show their paths and plugin-visibility evidence before selection.
- Detects MO2's attached USVFS DLL. Finding an MO2 configuration while running standalone does not make its virtual mods visible; launch through MO2 to use that location.
- Supports nearby portable MO2 configurations, registered LocalAppData instances, custom base/profile directories and Qt-escaped game paths. Configured profile names can differ from MO2 command-line overrides: review the selected profile list.
- Startup opens detection when the saved scan folder is missing, or points to a non-Data folder while MO2 is attached. Existing valid saved locations remain reusable.
- Added an optional Game Data folder override for staging/custom setups, and separate actionable errors when Skyrim.esm is missing or unreadable. Implicit masters are still included independently of plugins.txt.
- Scan diagnostics now show base-game Data, profile list and whether MO2's virtual filesystem is attached. Choosing a detected location clears stale scan results before rescanning.
- Retains guard protection and the app-specific MO2 startup compatibility setting. Rescan and regenerate existing entries to refresh their recipient rules.
- Release build succeeded. No new tests, commenter-setup reproduction or in-game validation was performed for this update. This addresses a discovered detection gap; the reported missing-master case is not yet confirmed resolved.

# 0.4.3 development preview — 2026-10-18

- Removed guards and related uniformed protectors from NPC distribution audiences, including broad groups, inherited alternatives and restored choices.
- Added eligibility regression checks and required a scanned NPC roster in the wizard.
- Preserved the 0.4.2 MO2 startup compatibility setting.
- Full integration checks, including uniformed-NPC eligibility regressions, passed. No in-game validation was performed.
- Rescan and regenerate all existing outfit entries to apply protection to older output. Incremental merging retains entries that you do not regenerate. This release does not automatically inspect male/female armor mesh compatibility.

# 0.4.2 development preview — 2026-10-18

- Fixed immediate startup crashes when launched through MO2 2.5.2 / USVFS 0.5.6.1. The application host now uses `CETCompat=false`, the documented compatibility workaround for MO2 injection into .NET 9+ applications.
- This opts this application's host out of CET hardware stack protection. It does not change Windows-wide security settings, antivirus, MO2 files or game files.
- Compared the published 0.4.1 EXE (crashes through MO2 with 0xc0000005; launches directly) with the fixed 0.4.2 build (visible window, startup log and MO2 usvfs_x64.dll confirmed loaded).
- This verifies startup compatibility on the tested setup; it is not new in-game validation of outfit or merchant distribution.
- Extract the entire new app ZIP into its own folder, then point MO2's Binary to SkyrimSPIDPatchGenerator.exe and Start in to that folder. Existing patch filenames/settings remain compatible.
# 0.4.1 development preview — 2026-10-17

- Renamed the application, executable, assembly, visible titles, version metadata, documentation and release packages to Skyrim SPID Patch Generator (`SkyrimSPIDPatchGenerator.exe`). Existing patch filenames and saved settings/backups remain compatible.
- Added an opt-in merchandise checkbox for each outfit, with Blacksmiths, General goods and Clothing traders selections.
- Scans actual vendor factions, linked merchant references and container bases; unrecognized categories and known non-vendor shared bases are excluded.
- Scans playable weapons from outfit plugins separately. Optional weapon stock goes to blacksmiths/general goods; clothing traders keep the outfit's selected clothing/armor but receive no weapons.
- Generates an additional SkyPatcher container INI. Merchandise requires SkyPatcher even if SPID distributes the NPC outfits. NPC chance does not affect stock.
- Merchandise selections persist per outfit/variant and participate in merging, removal, recovery, file-health validation and installable ZIP export. Merchandise manifests/settings use schema 3 to prevent older apps silently dropping this metadata.
- Updated images and README for the new name and trader controls. Build/UI rendering completed; no new test-suite run or live-game distribution verification is claimed for this follow-up.

# 0.4.0 development preview — 2026-10-17

- Added Skip outfit beside Back/Next. Skipped outfits bypass validation and generation, can be included again from the sidebar, and remain skipped when returning from review. Existing generated versions are preserved, including skipped variants of a repatched plugin. Skipping all outfits exits without writing a patch.
- Redesigned the app with a dark teal interface, a focused outfit library, patch inventory, and scrollable settings.
- Rebuilt distribution setup around FOMOD-style outfit steps, fixed navigation, natural nested scrolling, one-click parts, live audience counts, readable roles/races and inline validation.
- Added compatible-part assistance, color choices and headwear exclusion; preserved saved choices and added clearer repair for stale saved audiences.
- Made scanning responsive and cancelable, with one discovery pass, profile order, effective overrides, template inheritance, installed implicit Creation Club/resource listings, diagnostics and missing-master guards.
- Corrected mod-added Wenches detection and excluded negative evidence such as BardAudienceExcludedFaction from bard classification.
- Corrected SkyPatcher identifiers and NPC targeting, and prevented overlapping SPID audience selections from multiplying outfit chance rolls. Added runtime child exclusion.
- Unified generation/removal/recovery around staged validation, file locks, checksummed backups and rollback. Complete ESP/INI/manifest sets are validated together.
- Fixed generating after removing the final outfit, retained allocation history and protected stable generated FormIDs.
- Added patch-health statuses and a verified installable mod ZIP export for Vortex/MO2.
- Added atomic settings backups, startup/operation diagnostics, an expanded integration suite, small-window UI checks and reproducible release packaging/checksums.
- Read-only installed-mod audit: 948 plugins, 192 wearable candidates, 7,013 eligible NPC base records in about 22 seconds. Recipient counts remain estimates; no live Skyrim distribution test is claimed.

# 0.3.0 preview — 2026-10-16

- Added a Patch overview listing generated source plugins and named outfits with pieces, audience filters, chance, NPC sex, framework, distribution rows, timestamps, and required masters.
- Added a generated patch manifest and legacy INI migration so earlier patches appear in the overview after the next generation.
- Added edit/repatch and remove actions for selected generated outfits; multiple named outfits from one source plugin can be managed separately.
- Added per-outfit variants for collections containing several sets or color variants.
- Added a pre-generation review with scanned NPC coverage estimates, overlaps with other selected audiences and scanned distribution INIs, and possible slot conflicts.
- Added backup and restore for generated plugin, distribution files, and manifest. Backups are kept under the user's Local AppData rather than in the mod output.
- Added reusable audience presets and settings/preset import and export.
- Added optional filtering by a profile `plugins.txt` or `loadorder.txt`, expanded Steam library detection, and a Find Skyrim control.
- Improved the plugin list with patch status and an at-a-glance scan summary; clarified output warnings and beginner guidance.
- Validates outfit records, item links, masters, SPID rule shape and chance, and SkyPatcher rule shape before committing output.
- Added automated Windows integration checks for merging, repatching, named variants, removal, restore, legacy migration, SPID, and SkyPatcher.
- Attempted an SKSE launch check; the launcher exited without a Skyrim game process, so live-game behavior remains unverified.


---

# Earlier recorded iterations

Historical changes below are superseded by later versions. The old Guards preset was subsequently removed/protected.

## 0.2.5 — historical preview

# 0.2.5 preview — 2026-10-16

- Merges newly generated outfits and distribution rules into the existing patch instead of overwriting previous selections.
- Repatching a source plugin updates its generated outfit and rules while keeping other plugins intact.
- Preserves existing plugin masters and adds masters needed by newly selected outfit pieces.
- Uses stable outfit IDs so merged rules continue referencing the right outfit records.
- Clarifies the merge behavior in the confirmation dialog.

## 0.2.4 — historical preview

# 0.2.4 preview — 2026-10-16

- Saves distribution choices per source plugin between sessions: audience rules and group filters, chance, NPC sex, and excluded outfit pieces.
- Restores saved targets only when they still exist in the current scan.
- Saves the current wizard state when navigating, generating, canceling, or closing the setup window.
- Retains the previous farmer targeting, icon startup, and generated outfit fixes.

## 0.2.3 — historical preview

# 0.2.3 preview — 2026-10-16

- Improved the Farmers audience to resolve dynamically to farm factions and occupation records actually assigned to NPCs in the scanned plugins.
- Excludes animal-only farm factions from the Farmers audience and prefers faction targets when present.
- Filters the farmer role list to farming-related roles so unrelated combat classes are not shown.
- Retains the 0.2.2 startup resource fix, embedded application icon, and Mutagen outfit item-list fix.

## 0.2.2 — historical preview

# 0.2.2 preview — 2026-10-16

- Fixed startup failure by embedding the window icon as a WPF resource as well as the executable icon.
- Keeps the Mutagen outfit item-list initialization fix from 0.2.1.
- Keeps NPC-usage-based target discovery and mod-added group presets.
- Requires the .NET 10 Desktop Runtime x64; this is a framework-dependent package.

## 0.2.1 — historical preview

# 0.2.1 preview — 2026-10-16

- Fixed generated outfit construction by initializing Mutagen's required item list.
- Filters distribution targets to records assigned to NPC records in the scanned plugins, across plugin boundaries.
- Discovers recognizable mod-added NPC groups from actual assignments and exposes them in the audience selector.
- Automatically selects the all-installed-mods collection when a discovered mod group is chosen.
- Embeds the app icon in the Windows executable and main app window.
- Live Skyrim validation across varied load orders is still needed.

## 0.2.0 — historical preview

# 0.2.0 preview — 2026-10-16

- Reworked NPC targeting as a beginner-friendly guided audience picker with collection, familiar group presets, multi-select archetypes, and race filters.
- Added common audiences including Bandits, Forsworn, Guards, Mages, Necromancers, Farmers, Merchants, Adventurers, Warriors, Hunters, and others.
- Added separate audience rules so filters selected for one group do not bleed into another.
- Kept advanced search for mod-added or unusual NPC records and per-plugin targeting.
- Added the Wearable Distributor tunic-and-branches app icon to the app window and executable.
- Live Skyrim validation across varied load orders is still needed.

## 0.1.2 — historical preview

# 0.1.2 preview — 2026-10-16

- Removed the duplicate prototype project that only saved `generation-draft.json`; the repository now has one authoritative build path under `src/`.
- Rebuilt the Windows x64 app from `src/WearableDistributor.csproj`.
- Generate Patch writes `WearableDistributor.esp` and the selected SPID or SkyPatcher distribution file.
- Existing smart scan, per-plugin setup wizard, and structural read-back validation remain included.
- Added plugin-title role matching to preselect installed NPC target records, with a visible suggestion reason in the wizard.
- Clarified that the scan root can be a Vortex staging folder and added a hint when only official Skyrim plugins were found.
- Live Skyrim validation across different load orders is still needed.

## 0.1.1 — historical preview

# 0.1.1 preview — 2026-10-16

- Removed the duplicate prototype project that only saved `generation-draft.json`; the repository now has one authoritative build path under `src/`.
- Rebuilt the Windows x64 app from `src/WearableDistributor.csproj`.
- Generate Patch writes `WearableDistributor.esp` and the selected SPID or SkyPatcher distribution file.
- Existing smart scan, per-plugin setup wizard, and structural read-back validation remain included.
- Live Skyrim validation across different load orders is still needed.

## 0.1.0 — historical preview

# 0.1.0 preview — 2026-10-16

- First Windows x64 preview package with app executable and companion runtime files.
- Smart scan and plugin selection controls.
- Per-plugin distribution wizard for NPC groups, chance, sex, and outfit pieces.
- SPID and SkyPatcher patch file generation.
- Structural read-back checks for generated plugin and distribution files.
- Live Skyrim validation across different load orders is still needed.
