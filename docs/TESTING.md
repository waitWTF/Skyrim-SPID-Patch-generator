# Verification and manual release checklist

## Automated checks

From the repository root on Windows with .NET 10 SDK:

```powershell
dotnet run --project tests/WearableDistributor.IntegrationChecks.csproj -c Release
```

The suite creates disposable synthetic plugins and outputs. It checks merge and stable IDs, repatching/variants, individual removal, empty-plugin allocation, legacy migration, boolean targeting and SPID chance union, SkyPatcher syntax, settings normalization/backups, installed CCC/profile order, effective NPC overrides and nested leveled templates, conservative race/sex combinations, readable mod roles, negative occupation evidence, canceled scans, missing masters, checksummed backup failure/interruption recovery, malformed/truncated output, archive export and patch-health status.

It does not start Skyrim, prove runtime matching, render third-party meshes, or use the real mod output folder. Tests set LOCALAPPDATA to their own integration-work directory for backup isolation.

## Small-window and interaction checks

```powershell
dotnet build tools/UiPreview/WearableDistributor.UiPreview.csproj -c Release
```

Run its EXE with `wizard --small --check`, `review --small --check`, `main --small --check`, and `settings --small --check`. These verify visible workflow controls and scrollable content. The main/settings checks also require their selected page's controls to appear in Windows UI Automation. `--render <path.png>` exports a WPF screenshot using synthetic fixtures; default mode opens the interactive wizard. UI settings are isolated from normal saved preferences.

Manual checks before release:

- Mouse wheel over short roles/races continues scrolling the page. At the edge of a long inner list/grid, scrolling reaches the outer page. Footer stays visible at minimum window size.
- Collection → group updates role/race menus; several roles/races are alternatives. Adding/removing audiences updates live unique counts.
- Part checkboxes toggle on the first click. Color/compatible/headwear actions affect pieces only when explicitly pressed.
- Empty audiences, invalid chance, zero matching sex/race and empty parts have clear inline repair messages. Saved missing targets never broaden the audience.
- Review Back returns to setup and preserves pieces, audiences, variants and output choice.
- Empty library, missing folder, missing master, malformed settings/manifest and canceled scan give usable recovery guidance.
- Main search/select actions, collapsed filters, keyboard focus and scrolling work at smaller window sizes.

## Read-only installed-mod audit

The UI harness accepts:

```powershell
WearableDistributor.UiPreview.exe scan "path/to/Data" "path/to/plugins.txt" "path/to/report.txt"
```

This reads plugin data and writes a report at the specified path. It does not generate or install a patch. Installed reports can contain local paths/mod names; review before sharing. The release audit found 948 plugins and 7,013 eligible candidate bases. One quest plugin failed Mutagen parsing and was skipped without committing partial records.

## Disposable in-game profile

1. Use a separate MO2/Vortex test profile, a few known outfits and a recognizable small audience.
2. Scan effective Data with active profile plugins.txt, review diagnostics, configure compatible pieces and generate into a separate mod folder.
3. Add another outfit in a second session and verify earlier outfit IDs, rules and choices remain.
4. Test one variant removal, empty-patch regeneration, and backup restore. Confirm file-health status reports missing/broken output accurately.
5. Export Installable ZIP, install through the manager, enable the mod/ESP, source armor/NPC plugins and chosen engine. Deploy for Vortex.
6. Launch the game through SKSE in the manager context. Read SPID/SkyPatcher logs for parsing/dependency errors.
7. Check fresh NPCs or a new test game. Inspect matched farmers/bandit archers/mod-added NPCs, optional sex/race restrictions, and named variants.
8. Check templated and leveled enemies separately. SPID can match template/original bases; SkyPatcher outfit inheritance is a known verification area. Do not assume counts equal spawned actors.
9. Compare the selected chance across multiple fresh encounters. Other outfit distributors can compete, and separate outfits do not form a guaranteed percentage partition.
10. Disable the generated mod and verify a test profile can return to its expected baseline.

Previous SKSE start did not reach Skyrim, so steps 6–10 are outstanding. Do not describe this preview as in-game validated until those are observed.

