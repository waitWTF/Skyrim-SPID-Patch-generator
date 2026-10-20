# v0.4.9 verification â€” 2026-10-20

## Checks performed

- Release source and UI harness compile without warnings/errors.
- Integration suite passes, including 35 current regression scenarios.
- 20 worn-mesh safety assertions pass: female/male distinction, both enabled weight partners, late deployment/removal, zero-byte files, path normalization, staging uncertainty, unreadable archive recovery, generation rejection before overwriting, detected-patch health, and plain-suffix color matching.
- 29 WPF workflow assertions pass, including one-row configuration with 500 other checked plugins, out-of-view batch counts, saved setup scope and Save & close behavior.
- Four minimum-window layout/accessibility checks pass: wizard 980x600, review 940x580, main/settings 1000x660.
- The native release apphost launches into the v0.4.9 workspace with its icon and scan progress indicator, using isolated preferences. Its full scan completed (37 equipment-only / 99 mixed candidates, 5,706 eligible NPC bases); the repaired outfit was marked Patched. Right-click opened exactly one setup page with eight matching parts and available-mesh evidence. Save & close returned to the library without generating live output.

## Investigation evidence

A user's existing patch assigned all 37 pieces from Adventurers Armor Bubblegum, including conflicting colors and full-body/modular alternatives. All 37 female worn-model references were unavailable in effective Data, although the BodySlide projects and generated meshes existed in separate output folders. The equivalent vanilla male fallback was found in a BSA archive. This is consistent with the reported female NPC having a visible head but missing body; a female-only audience does not make absent female assets appear.

An isolated copy of that output was repaired and validated: 36 outfit records retained, the other 35 unchanged, the affected outfit's FormID retained, 37 parts replaced by eight matching black parts, 1,289 existing recipient bases/33% chance/female filter retained, and trader stock preserved. Existing generated meshes/morphs were restored to the correct local mod/Data paths with backups. A follow-up read-only scan found zero unavailable female pieces in this armor and both detected copies of its patch passed file health.

This installation also revealed a legacy merchants-only entry without an empty NPC comment section. Compatibility handling now accepts separately validated shop stock only when no NPC rule references that outfit; an unexpected NPC rule still fails. A regression scenario covers both cases.

Personal patches, asset payloads, settings and detailed local audit files are excluded from this public package. The application does not bundle or redistribute the investigated armor.

## Performance and MO2 scope

On the same pre-release installed-data comparison (1,138 plugins / 13,969 NPC profiles), test 7 reduced NPC preparation from about 7.85 to 6.60 seconds, with identical recipient/preset counts. This measures one preparation stage, not total startup/scan time or the reporter's exact 2â€“3 minute stall. This release keeps that cache and opens only explicitly chosen outfits. BSA directories are indexed once per scan; archive metadata is checked again before generation.

A prior experimental build was successfully started as a tool through MO2 2.5.2, detected its virtual filesystem/profile and scanned virtual-only plugins without launching Skyrim. v0.4.9 preserves the direct apphost's CET compatibility opt-out and path-discovery implementation. This release was not separately rerun through MO2; no compatibility claim is made for every installation.

## Limits

No new Skyrim/SKSE or in-game visual test was performed for this release. File availability/valid ESP+INI output does not prove NIF geometry, body/skeleton/race compatibility, archive activation, every template outcome, optional-part rolls or merchant restock behavior. Existing NPCs may require normal cell unloading/reloading; updating the EXE alone does not rewrite an installed patch. Other installed outfits with unavailable assets still need their own author requirements and BodySlide output checked.

Scans of the investigated loadout skipped one malformed quest plugin through the existing per-plugin boundary. That is not an error-free scan, nor evidence that its NPCs/records can be targeted. Staging-only scans cannot establish deployment or prove absent runtime assets.

## Reproduce

Use the commands in TESTING.md. All fixture output, preferences and support logs are isolated from normal user settings; backup locations are overridden in test processes. Generated fixtures and private installed-data reports are not included in release archives.
