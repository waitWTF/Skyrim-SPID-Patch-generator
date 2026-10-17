# 0.4.1 development preview

17 October 2026. New application branding: Skyrim SPID Patch Generator. Executable/assembly: SkyrimSPIDPatchGenerator.exe/.dll. Internal source namespace/project file and existing settings, output ESP/INI/manifest names remain compatible with Wearable Distributor installations.

## Added

- Per-outfit opt-in trader merchandise checkbox, with Blacksmiths, General goods and Clothing traders checkboxes.
- Optional weapons from the selected outfit plugin, scanned separately from ARMO records. Weapons are never put into OTFT outfit records and never sent to clothing traders. Clothing/armor pieces remain eligible for those traders.
- Effective vendor-faction → placed merchant chest → CONT base resolution from usable scanned records. Requires an eligible encountered NPC using the faction. Unrecognized merchant types and bases with non-vendor placements are excluded.
- Separate SkyPatcher container output using explicit Plugin|hex identifiers and addOnceToContainers=item~1. This feature needs SkyPatcher even when SPID handles NPC outfits. It does not distribute items to a merchant's worn inventory.
- Merchant choices persist per outfit/variant. Merged manifest-based regeneration deduplicates container/item entries, removal updates stock, backup/restore includes stock rules, and installable ZIP includes stock config and dependency instructions.
- Merchandise manifests/settings use schema 3 so older versions refuse incompatible stock metadata rather than silently dropping it. Output transaction snapshot schema remains 2.

## Verification status

Build and direct UI rendering are the verification scope for this follow-up. No new test suite or live Skyrim test was run. Prior 0.4.0 synthetic checks and read-only scan results are historical evidence for that baseline, not a claim that the new merchant path is runtime-tested. See [0.4.0 review](REVIEW-0.4.0.md).

Merchant category inference uses vendor buy/sell list, faction and container EditorIDs; unrecognized naming or script-driven shops may be omitted. Static faction/placement evidence does not prove a shop opens during play. Container base rules affect all instances of that base: known non-vendor sharing is screened out, but scripts and runtime modifications cannot be fully predicted. Vendor buy/sell restrictions and restock timing still require in-game checks.

The current implementation is an addition to NPC outfit distribution, not a merchandise-only wizard mode. Shop stock uses one copy for new entries and no outfit-chance roll. Existing entries are not cleared or replaced.

## Format source

[SkyPatcher container implementation](https://github.com/Zzyxz/SkyPatcher/blob/main/container.cpp) defines filterByContainers, addOnceToContainers and item~count parsing. Its current main branch contained conflict markers when inspected; this design uses the documented parser keys, not a recommendation to build that upstream snapshot.
