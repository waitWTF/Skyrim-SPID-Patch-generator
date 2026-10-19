# 0.4.0 development review

Date: 17 October 2026. Based on the local v0.3.0 preview and its subsequent review fixes. This document describes the local development package, not an external upload.

## Delivered changes

- Dark, simple MO2/Vortex-inspired flow with distinct Outfit library, My patch and Settings areas.
- FOMOD-style outfit sidebar, fixed footer, compact controls, keyboard focus, natural nested scrolling, live audience previews and one-click part selection. Main navigation preserves selected-page controls in Windows UI Automation for assistive tools.
- Color grouping, explicit compatible-parts assistance, optional headwear exclusion, presets, persistent named variants and repair of stale audiences.
- Background/cancelable non-reentrant scan with effective overrides, templates, real group populations, readable role/race labels, active profile ordering and installed implicit Creation Club/resource plugins.
- Human-oriented occupation matching, fused mod NPC identifiers, negative faction evidence checks, and source master validation that overrides cannot bypass.
- Correct engine-specific identifier syntax, boolean grouping and audience union before SPID chance; no extra rolls for the same outfit due to overlapping chosen audiences.
- Shared output/backup transaction; checksums and complete-set validation before merge/export/restore; stable IDs and obsolete-variant cleanup.
- Patch-health overview, generated-only installable mod ZIP, robust settings and startup/operation diagnostics.
- Reproducible source/app packages, tests, previews and documentation.

## Evidence and practical limits

Synthetic integration checks cover binary ESP readback, merge/repatch/variant removal, backup integrity and interruption recovery, truncated/wrong rules, profile/CCC imports, effective overrides, direct/nested templates, conservative branch matching, recognizable occupations, portable settings, compatible slots and mod ZIP export. UI layout checks cover wizard at 980x600, review at 940x580, and main/settings at 1000x660. Native interaction verified audience narrowing and adding; the discovered nested-wheel issue was repaired and rechecked before packaging.

A read-only installed-mod audit completed in approximately 22 seconds: 948 plugins, 192 wearable candidates and 7,013 eligible NPC base records. It included 75 installed implicit content/resource plugins. Five diagnostic lines remained: one plugin that Mutagen could not fully parse, population summary, implicit-content summary, template inventory caution, and variable-template explanation. Failed-plugin snapshots were discarded. These counts describe the audit environment and are not a performance promise for other mod lists.

The audit also checked recognizable populations: 97 official Farmers, 78 official Necromancers, and 147 Immersive Wenches NPC bases in that mod's Wenches group. Counts are candidate base records, not guaranteed actor spawns. These are local validation results; raw user reports with mod names/paths are not included in public release archives.

## Remaining work

- No confirmed live Skyrim distribution test. The prior SKSE launch did not bring up Skyrim. Run the disposable-profile checklist before calling this a stable release.
- SPID NPC IDs may also match template/original bases, so a roster is not an exact runtime recipient boundary. Runtime child exclusion is enforced; other dynamic keyword/faction changes or distributors can still affect outfits.
- SkyPatcher on template-inherited inventory requires game verification. Its 100% setting means every eligible rule match, not exclusive control over all other mods.
- No 3D NIF viewer. Slot/color assistance cannot prove mesh/body/skeleton compatibility.
- No native MO2/Vortex API. Virtual Data/profile files improve fidelity, but staging duplicates and script-only spawn behavior remain uncertain.
- Quest-link encounter evidence is conservative inference, not proof of quest activation or availability.
- Framework output folders share an ESP filename; enable only one generated Wearable Distributor output mod at a time.
- Backups created before checksum schema 2 remain on disk but need manual recovery. Multi-file transactions do not guarantee survival of every power-loss scenario.

## Sources checked

- [SPID complete reference](https://raw.githubusercontent.com/powerof3/Spell-Perk-Item-Distributor/master/resources/SPID%20Complete%20Reference.txt): filter grammar, traits, keyword dependencies and chance.
- [SPID NPC lookup implementation](https://github.com/powerof3/Spell-Perk-Item-Distributor/blob/master/SPID/src/LookupNPC.cpp): template/original-base matching.
- [SkyPatcher NPC parser](https://github.com/Zzyxz/SkyPatcher/blob/main/npc.cpp) and [identifier resolver](https://github.com/Zzyxz/SkyPatcher/blob/main/utility.cpp): NPC selection and Plugin|hex syntax. Current upstream files contained merge-conflict markers when inspected; this review does not endorse that upstream build.
- [Mutagen environment docs](https://mutagen-modding.github.io/Mutagen/environment/) and [load-order docs](https://github.com/Mutagen-Modding/Mutagen/blob/dev/docs/loadorder/index.md): implicit listings, profile plugins and Creation Club listings.
- [MO2 FOMOD installer](https://github.com/ModOrganizer2/modorganizer-installer_fomod) and [Nexus FOMOD installer](https://github.com/Nexus-Mods/fomod-installer): step-based installer workflow inspiration.
- [Microsoft WPF keyboard focus guidance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/styling-for-focus-in-controls-and-focusvisualstyle): visible keyboard focus.
