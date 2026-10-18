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
