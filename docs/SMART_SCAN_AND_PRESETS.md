# Outfit discovery, presets and sex safeguards — 0.4.5

Research and implementation date: 19 October 2026. This is a curated sample of public mod descriptions, not an exhaustive Nexus survey or statistical ranking. No third-party mod assets or INIs are bundled. Some adult-marked pages, including Lunar Claw's original Nexus page, did not expose their descriptions to the research tool. Lunar Claw's fallback sex restriction comes from the project owner's explicit requirement, not a claimed independent inspection of its downloaded plugin.

## Research informing the defaults

| Primary source | Observation | Implementation decision |
| --- | --- | --- |
| [Traveling Mage HDT-SMP Armor](https://www.nexusmods.com/skyrimspecialedition/mods/69394) | A standalone unisex mage set, clothing/light-armor options, cape variants; links separate distribution addons. | Single sets remain candidates. Optional online addons do not automatically disqualify the base outfit. Do not classify every mage outfit as female-only. |
| [QND SPID Clothing Pack 1](https://www.nexusmods.com/skyrimspecialedition/mods/93345) | Combines clothing collections with general bandit/mage audiences and specific named NPC outfits; notes overlaps with other INIs. | Common audiences plus explicit scanned recipients; existing-distribution checks follow supplied outfits rather than filenames/comments. Large wardrobe/store mods can remain useful despite including vendor NPCs. |
| [SPID – Maiden Farmers](https://www.nexusmods.com/skyrimspecialedition/mods/68315) | Uses the Farmer class for clothing distribution. | Farmers use occupation records/AI work evidence, rather than farm animals, ownership factions or outfit names. |
| [More Appropriate Outfits for NPCs](https://www.nexusmods.com/skyrimspecialedition/mods/72635?tab=description) | Generic NPC outfit distribution includes bandit targeting. | Supply familiar group presets; resolve installed NPC records locally. |
| [Immersive Wenches](https://www.nexusmods.com/skyrimspecialedition/mods/595) | Adds several roles, including inn staff and travelling NPCs, with bodyguards. | Show mod-origin sections only for actual scanned eligible NPCs; keep roles separate when their metadata supports it. Existing bodyguard/uniform protection remains in force. |
| [Ordinator](https://www.nexusmods.com/skyrimspecialedition/mods/1137) | A perk overhaul, even if helper wearables exist in its records. | Hide perk/gameplay systems using known identities and owned-record content evidence. |

These sources inform policy; the app does not scrape Nexus on launch or claim which audiences are globally most popular. Armor creation tools use per-sex worn models; installed Mutagen 0.54.4 APIs were inspected to read ArmorAddon.WorldModel, armor armatures and templates. The public catalog is not a substitute for examining installed plugin records.

## Scan policy

1. Discover the configured installed/virtual Data and active plugin list using the existing MO2/Vortex location flow. Start with effective load-order records. Without a profile list, a staging scan can include disabled mods; diagnostics already flag that limitation.
2. Identify source-owned, non-deleted, playable ARMO records with wearable slots. Replacer-only plugins do not become new outfit sets. Retain original plugin identity for grouping. Effective deleted records are removed from candidates.
3. Hide official/DLC/CC and Skyrim.ccc resource entries; known body/hair/skin/replacer, follower/quest, perk/magic and framework families. Explicit garment titles avoid a number of ambiguous generic-name exclusions. User custom exclusions remain available.
4. Apply content heuristics to unfamiliar mods: at least ten perks exceeding wearable count, or at least eight spells exceeding twice wearable count, suggest gameplay systems. New worldspaces with NPCs suggest expansions. NPC-bearing plugins without garment titles are held back unless a larger wardrobe predominates (at least twelve pieces and at least four pieces per source-owned NPC). These are explainable heuristics, not guaranteed categorization. Manual inclusion is available.
5. Parse external supplied SPID Outfit/Item operands and supported SkyPatcher outfit/item operations. Resolve SPID EditorIDs, OTFT items and nested leveled items to wearable sources, with cycle protection. Ignore the app's own generated distributions. Finding some distributed items can hide a plugin even if coverage is partial; reveal and include it explicitly when needed. Not every possible custom INI syntax is supported.
6. Parse effective ARMA worn-model paths after overrides. If all referenced addons resolve, record whether model paths exist for male, female or both. Resolve armor templates when armatures are absent. Unknown remains unknown. This does not open NIF meshes or inspect packed assets.
7. Build the existing effective adult humanoid NPC population with encounter-reference evidence, template resolution and uniform protection. A placed/quest/leveled-list reference suggests encounterability; script-only actors and runtime conditions may differ.

Filter revision 1 clears old IncludedPlugins once to prevent a previous bulk override restoring everything. It preserves DistributionSettings, IgnoredPlugins and generated files. DisabledPlugins persists patch selection independently of hiding. The application never toggles plugin activation in Vortex/MO2.

## Library visibility

- **Show my exclusions:** manually hidden entries; this is their primary hiding category.
- **Show already patched outfits:** health-ready completed entries not manually hidden. They are deselected and hidden after scan/generation. My patch always provides their details and edit action. Needs-review entries are not considered completed.
- **Show hidden candidates:** remaining smart-filter candidates. Ordinary selection-all does not implicitly include these; use Include filtered candidate, an individual checkbox or Enable selected.

Search applies to every revealed category. Hide/disable do not remove any previously generated distribution; use My patch → Remove outfit to change existing output. This distinction prevents hiding a library row from silently uninstalling a patch.

## Presets and targets

DistributionPresetCatalog creates common groups plus sections per non-official source plugin represented by eligible scanned NPCs. Nothing appears for an absent NPC plugin or a zero-match preset. Immersive Wenches, Flower Girls and unfamiliar NPC mods use the same population mechanism; exact plugin aliases are not required. Wenches/Flower Girls/adventurer roles still depend on usable class/faction/keyword or recognizable NPC identity evidence. Unclassified actors remain accessible through that mod's All eligible NPCs preset and the advanced selection tools.

The dropdown includes common enemy, mage, adventurer, civilian and worker groups, with archers/leaders/mages/melee variants when actual role records exist. It displays a count for the current NPC sex. Its optional search matches preset labels and plugin category names. The common section spans all eligible installed NPC populations; each mod section limits selection to original NPC ownership, so a cosmetic overhaul does not falsely claim every NPC it overrides.

Preset rules are resolved to explicit recipients before generation. Saved familiar groups and refinements are rebuilt from the next scan; faction-based role refinements are retained. Rebuild existing patches after changing NPC mods, load order or sex restrictions: output is a snapshot, and incremental merging deliberately preserves untouched entries.

Guards, soldiers, officers, housecarls/bodyguards and other identified uniformed protectors remain excluded, even though some researched mods distribute to these groups. That preserves the owner's prior protection requirement.

## Sex controls

The selected parts determine the restriction. A male-only and female-only combination blocks generation until incompatible parts are excluded or split into named outfits. A single restriction locks the NPC sex dropdown and shows why. Applying an incompatible reusable preset produces a warning and keeps the compatible sex. An incompatible saved outfit setting shows a restoration note. The generator repeats the validation.

Model support for both sexes takes precedence over Lunar Claw's unknown-data fallback, allowing installed conversions to supply both models. BodySlide labels alone never trigger restrictions. No model-path rule can prove that a male fallback is artistically equivalent to the female outfit, that paths actually exist, that all races render correctly, or that bodies and meshes fit. Review author requirements and verify in game.

## Source map and verification

- WearableFilterService: classification, numeric slots, INI operands.
- WearableScanService: effective armor/addon snapshots, template model metadata, distributed-item graph.
- OutfitGenderPolicy: sex restriction/validation and narrow Lunar Claw fallback.
- NpcTargetingService / DistributionPresetCatalog: effective population, semantic groups, common and installed-mod presets.
- MainWindow: buttons/context actions, persistent disabled/hidden state, completed-outfit reveal.
- PatchOptionsWindow: searchable grouped preset dropdown, sex lock/warnings and part support column.
- StartupWindow / App: existing icon loading window before main workspace construction.

The project compiles on the installed .NET 10 SDK. No new automated tests were added or run, no new UI launch/full-modlist scan checks were performed, and no Skyrim distribution verification is claimed for this change. It remains a development preview. Older documentation images show 0.4.1.
