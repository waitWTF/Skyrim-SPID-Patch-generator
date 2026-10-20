# v0.4.10 preview verification — 2026-10-20

## Automated checks

Final results: zero-warning application/test/UI builds; full integration suite passed, including 35 retained regression scenarios, 24 mesh-safety assertions and 26 matching-variant assertions; 33 WPF workflow assertions; four minimum-window layout checks. The readable error panel was also rendered and visually reviewed.

The integration harness covers real ESP/INI/manifest generation, required/optional parts, color bundles and exclusive alternatives, stable IDs across repatches, recursive pruning, preservation of unrelated outputs, damaged-selector detection, saved settings, scanner targeting, merchant-only output, transaction rollback and privacy-safe diagnostic fields. It retains the earlier regression scenarios.

WPF checks cover lazy outfit pages, selected-row scope, saving drafts, merchant-only destinations, optional chances, skips, checked/highlighted bulk actions and the new readable mesh error summary. Detailed paths are collapsed initially and remain copyable when expanded. Layout checks use the wizard, review, main and settings minimum sizes.

## Installed-record checks

Read-only tests against three installed plugins produced valid Balanced plans:

- Rogue: all ten records are available across Standard, Black and Dark branches. Normal/torn pants are one guaranteed garment group. Dark top and boots stay together; black-belt pants remain alternatives. Hood and poncho use the preset accessory chance.
- Adventurers Armor: 32 modular/shared pieces across five colors; five combined Full armor records are excluded. Shared gloves remain required; the belt uses the preset chance.
- Basic Linen: three modular pieces, with the combined Outfit record excluded.

These counts describe possible pieces, not the number equipped on one NPC. Names/slots are conservative evidence and do not prove every author's intended mesh combination.

## General missing-mesh investigation

A read-only scan found 226 unavailable worn mesh paths across 13 outfit plugins, all with already-built copies beneath an extra meshes folder. BodySlide's configured output root ended in meshes, while projects supplied their own Meshes path. This explained the repeated missing-file reports after building.

A separate, private local repair restored only missing record-referenced outfit files and matching morph files into the correct existing output/Data paths, using verified hardlinks and a configuration/repair backup. Existing valid assets, original nested copies and generated patch contents were retained. BodySlide's local output root was corrected. A follow-up scan found no unavailable worn mesh paths among the equipment candidates and previously patched sources checked by that audit.

The application does not silently change BodySlide settings or repair another user's installation. It diagnoses matching nested copies, summarizes the action needed, and keeps expected relative paths in expandable details. Third-party assets, personal patches, local repair helpers and detailed installation reports are excluded from release archives.

## Compatibility and limits

The direct EXE host, icon, legacy output names, MO2 app-host compatibility setting and existing virtual/profile path discovery are retained. The earlier MO2 2.5.2 tool/VFS check is historical evidence; this iteration was not separately run through MO2 or Skyrim.

No new in-game visual test was performed. File checks cannot verify NIF geometry, body/skeleton/race compatibility, BSA activation, reset behavior or exact rendered coverage. Nested leveled lists follow Skyrim's selection/UseAll structure; the new random combinations still need in-game feedback. Required means one version after the overall outfit assignment, not a permanently guaranteed loot percentage.

Existing Custom selections are preserved. Choose Balanced to adopt the new alternative/color defaults, review exclusions and regenerate; updating the EXE does not rewrite previous patches. Matching plans use schema 6 and need 0.4.10 or newer for editing; keep backups when returning to an older build.

## Implementation references

- [Mutagen Skyrim leveled-item flags](https://github.com/Mutagen-Modding/Mutagen/blob/dev/Mutagen.Bethesda.Skyrim/Records/Major%20Records/LeveledItem.cs): selector lists use no UseAll flag; color bundles use UseAll.
- [Mutagen leveled-item record structure](https://github.com/Mutagen-Modding/Mutagen/blob/dev/Mutagen.Bethesda.Skyrim/Records/Major%20Records/LeveledItem.xml): chance, flags, global and item/list references.
- [BodySlide documentation](https://github.com/ousnius/BodySlide-and-Outfit-Studio/wiki): project-generated assets still need correct installation and mod-manager visibility.

Run the commands in TESTING.md to reproduce fixture checks. Test settings/output/logs are isolated from normal preferences and installed mod data.
