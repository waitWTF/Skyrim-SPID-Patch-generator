# Merchants, activity and audience updates — 0.4.8

## Shop stock without NPC outfits

**Only want traders to sell these items? Tick only “Sell selected parts through traders.”** Leave “Distribute outfits for NPCs to wear” unticked. No NPC audience, NPC sex or chance is required. Both destinations start unticked for new setups; saved destination choices are preserved. Available trader categories default on, and can be changed individually. Empty/unavailable categories do not default on. An older saved category that has become unavailable remains visible for review.

The selected armor/clothing/accessories go to the confirmed vendor containers for selected blacksmith, general-goods and clothing traders. Included source weapons go only to blacksmiths/general goods; any container shared with clothing traders is protected from weapon additions. These are **real SkyPatcher container rules**, not a draft JSON plan or outfits equipped onto shopkeeper NPCs. SkyPatcher and `iEnableContainerPatching=1` are required even if the output folder's NPC engine is SPID. SPID alone cannot process these container rules. Merchant restocks and runtime behavior still depend on the installed game/mod setup.

NPC audiences and one-sex compatibility validation are used only when the NPC checkbox is ticked. Shop stock may include alternative slots or male/female-only garments together because it is merchandise. If both destinations are unticked, generation explains that a destination is needed. At least one armor/clothing/accessory part and actual stock targets remain required; this is not a general weapon-only distributor.

Shop-only entries retain a generated OTFT record for stable inventory identity and later repatching, plus a comment-only section in the selected engine's shared NPC INI. They have **zero NPC assignment rows**. The separate SkyPatcher container INI contains the shop rules. Thus keeping the shared ESP/manifest is still part of the managed output, but SPID is unnecessary for an output containing only shop stock. Installable archives describe actual required engines from active destinations.

Repatching an existing named outfit from NPC mode to shop-only removes that entry's old managed NPC rules, retains unrelated entries and rebuilds shop stock from the merged manifest. The inverse is supported too. Final validation permits an unreferenced OTFT only for a valid shop-only manifest entry with actual stock rules and no NPC rows. Health, removal, backup restoration and ZIP export use the same validation. The manifest and settings advance to schema 4 when shop-only data appears, so older apps reject it rather than dropping the new flag. Missing flags in older files mean NPC mode for compatibility; normal new wizard setups explicitly start with no active destination.

## Live local activity

The main-window **Activity** button and wizard **Activity history** button open a live, resizable view. It includes scan stages, generation/validation/backup stages, setup navigation/edits, selected parts, audience changes, check/enable/disable/hide/unhide actions and outcomes. Follow latest activity can be turned off; Clear history clears the local session view.

This is a **memory-only activity history**, bounded to 1,000 entries, with local time of day. It resets when the app closes. It can show installed plugin names, source item names and paths already shown in the UI. It does not write a log file, export user activity, upload anything or enter the SupportLogs/report export. The separate typed privacy-safe Error reports service is unchanged. Do not confuse screenshots of the activity view with a privacy-safe report.

## Checked and highlighted rows

The action scope menu applies consistently to visible library buttons and right-click actions:

- **Checked or highlighted rows** (default): union of checked visible rows and highlighted visible rows, counted once.
- **Checked rows only**: checked visible rows.
- **Highlighted rows only**: highlighted visible rows; Ctrl/Shift multi-selection is supported.

The visible target count updates before actions. Search/filter-hidden rows are never swept into a bulk list action. Select all, None and Invert change the visible checkboxes and remember those choices; None also clears highlights. Enabling/disabling changes patch selection only, not game plugins. Invalid/missing-master source plugins cannot be made patchable by enabling them.

Hide records a manual exclusion and clears the checkbox. Unhide removes the manual exclusion, restores a smart-filter inclusion when applicable and records an explicit visible override for already-patched entries. That override remains across scans; Hide clears it. The separate hidden-category view toggles remain available. Generate operates on checked eligible sources as before; highlighting alone is a list-action selection, not permission to patch that source.

## Factions, guilds and protected members

`FactionAudienceCatalog` maps readable group names to primary membership faction EditorIDs observed in local official master data, resolved to installed FormIDs during each scan. It does not guess by NPC names, a quest title or a plugin filename. Examples include ThievesGuildFaction, CollegeofWinterholdFaction, DLC1DawnguardFaction, DLC1HunterFaction and the civil-war membership factions. Installed mods adding NPCs to these factions participate through their effective winning records. Unrecognized custom faction naming may need the existing collection/group/advanced selectors; no complete compatibility database is claimed.

Quick presets offer all members plus available class/role subgroups, including leaders, archers, mages, melee roles, recruits and shamans where the population has matching records. Roles are generated from actual subgroup members, not invented empty archetypes. Empty presets are omitted. Group/role/race choices still narrow an audience together, and current-scan recipient lists remain snapshots.

**Only existing uniform protection is marked Protected.** There is no blanket protected flag on all guilds. A group label/preset warns when it contains members already covered by `UniformedNpcPolicy`. The current filtered count appears in setup, and protected selections carry a warning in final review. Those members are available only through an explicit recognized faction group. Broad groups such as All NPCs or Warriors keep the existing protection. Guards, bodyguards and housecarls remain excluded even from explicit faction groups; mixed template variants containing one remain excluded. The user's explicit faction choice is the opt-in to replacing those uniforms; it is not silently selected by outfit-name suggestions.

## Unique NPCs

The **Unique NPCs** group uses the winning NPC base's `Unique` flag. It does not classify an NPC as unique from their name. Its optional individuals list uses names, source plugins and FormIDs to distinguish same-name records. Multiple checked individuals are alternatives, with race/sex narrowing still available. Leaving the individuals list empty means the whole eligible unique population for the selected collection.

This group carries a separate quest/script caution in its selector, setup and review. Unique NPCs can have scripted clothing changes or quest scenes, and distribution may conflict with those changes. The warning does not claim every unique NPC will break a quest, or that static analysis can guarantee compatibility. The group's normal uniform protection still applies; it does not bypass guard protection or faction warnings.

## Source map and verification

- Models/PreferencesService/PatchInventoryService: destination flags, schema compatibility and explicit-unhide preferences.
- PatchOptionsWindow/PatchReviewWindow: destination controls, activity access, warnings, individual selection and stock-only review.
- PatchGenerator/DistributionRuleCompiler/MerchantStockService/PatchPackageService: zero-NPC shop-only output, merged regeneration, validation and installation requirements.
- ActivityHistory/ActivityHistoryWindow: bounded memory history, UI-thread dispatch and live display; never called by the support-report exporter.
- FactionAudienceCatalog/NpcTargetingService/DistributionPresetCatalog/WearableScanService/UniformedNpcPolicy: installed membership, role presets, selective protection and Unique metadata. Repeated collection populations are cached.

Build/package verification only. No automated tests, UI/MO2/Vortex launch checks or game checks were run for this update. In-game vendor restocks, scripted factions, late-spawned NPCs and quest outfit behavior still need field reports.
