<p align="center">
  <img src="docs/images/v041-banner.png" alt="Skyrim SPID Patch Generator: choose outfit mods, choose NPC audiences, generate a SPID or SkyPatcher patch" width="100%">
</p>

# Skyrim SPID Patch Generator

**Give your installed outfit mods a place in Skyrim.**

Skyrim SPID Patch Generator is a Windows desktop app for Skyrim Special Edition / Anniversary Edition. It scans installed plugins for wearable armor, clothing and accessories, groups them by source plugin, and guides you through choosing **who wears what**. It generates an outfit ESP and the matching **SPID or SkyPatcher INI** without making you write distribution rules yourself.

**0.4.7 development preview** · Windows x64 · .NET 10 Desktop Runtime · MIT license

Previously called **Wearable Distributor**. Start `SkyrimSPIDPatchGenerator.exe`. Existing settings, backups and generated `WearableDistributor.*` patch filenames are retained for compatibility; they do not need renaming.

[Downloads](https://github.com/waitWTF/Skyrim-SPID-Patch-generator/releases) · [Issues and ideas](https://github.com/waitWTF/Skyrim-SPID-Patch-generator/issues) · [Changelog](RELEASE_NOTES.md) · [Source code](https://github.com/waitWTF/Skyrim-SPID-Patch-generator)

> **Preview status:** generated files have validation checks, but successful outfit distribution has not yet been confirmed in a live Skyrim session. Use a separate test profile before relying on the output in a main playthrough. Images below use synthetic demo records; they are not in-game results or a list of bundled outfit mods.

## New in 0.4.7: a tidy application folder

The Windows ZIP now extracts into one main folder:

```text
Skyrim SPID Patch Generator/
├── SkyrimSPIDPatchGenerator.exe
├── START-HERE.txt
├── Runtime/          Required application libraries and configuration
└── Documentation/    README, changelog, license and guides
```

Start the EXE at the top, or point MO2/Vortex to it. Keep the whole folder together. Extract this update into a **fresh folder** rather than mixing it into an older flat installation. Existing settings, logs, backups and generated patches keep their current locations. The source-and-app ZIP has its own enclosing folder with the ready-to-run app in `app/` and developer files alongside it.

The root EXE directly loads the app from `Runtime/`; it does not launch a second wrapper process. The existing app-specific MO2 CET compatibility setting is retained. This release was built and packaged; the changed layout has **not** been launch-tested directly or through MO2/Vortex. See [application layout details](docs/APP_LAYOUT.md).

## Also included: shareable error reports and easier repatching

**Error reports** is always available at the top of the main window. Open it to preview and **Save report**, **Copy latest error**, refresh the report, or open the safe log folder. Reports contain app version, fixed error/reason codes, HRESULT values and application method tokens. They exclude personal paths, usernames, exact timestamps, installed mod names/lists, settings, raw exception messages, screenshots and game logs. Nothing is uploaded automatically. Previous `application-*.log` files can contain paths and are never included. Local scan details stay on screen; the former scan-report export now opens the shareable error-report window. Settings exports and **Export patch contents** are separate user-data features and are not support logs.

**My patch → Edit / repatch selected** opens just that named outfit. **Repatch all** uses saved choices and goes straight to a combined review when they remain valid; setups needing attention open in the wizard. The saved output folder is reused, and the review shows its destination before writing. Other variants from the same plugin are retained. Removing a saved setup does not uninstall an existing outfit—use **My patch → Remove outfit** for that.

Selected entries with missing, duplicate or incomplete distribution sections can now be rebuilt without the old pre-merge validation rejecting the repair itself. A missing shared NPC INI requires regenerating all entries that need it. Untouched damaged entries still block the final update. Existing readable outfit records keep their FormIDs, and the full staged ESP/INI/inventory/merchandise set must validate before backup and commit. Unsupported corruption or an unreadable/missing ESP still requires restoring a backup or using a new output folder. INI validation indexes each file once per operation, and legacy migration reads the generated ESP once rather than once per outfit.

See [report privacy, retention and repatching details](docs/ERROR_REPORTS_AND_REPATCHING.md). Build verification only; no new automated, runtime UI or in-game checks were run for 0.4.6. Native failures before .NET starts, forced termination and unrecoverable resource exhaustion may not reach a managed logger.

## New in 0.4.5: outfit discovery and distribution presets

- **Cleaner defaults:** CC names with hyphens, Ordinator/perk overhauls, magic systems, body frameworks, followers and world expansions are held back. Small standalone sets stay eligible; no author allowlist or minimum set size is required. Large clothing collections with a few vendor NPCs can remain eligible.
- **Existing distributions:** inspects actual supplied item/outfit operands and follows outfit/leveled-item references. Comments and NPC target filters no longer hide unrelated outfit plugins. Coverage can be partial; reveal a candidate and include it manually if appropriate. The app does not hide an outfit simply because an optional SPID addon exists online.
- **Selection controls:** use the visible **Enable selected**, **Disable selected** and **Hide selected** buttons, or right-click selected rows. These control patch setup, not the game's plugin activation. Disabled and manually hidden choices are remembered.
- **Three separate hidden views:** smart-filter candidates, your own exclusions, and already-patched outfits. Successfully patched outfits automatically leave the standard library; **My patch** keeps their details and editing controls. Entries needing review are not treated as completed.
- **Quick distribution presets:** one searchable, grouped dropdown for common audiences and each installed NPC-adding plugin. Examples include Bandits and their Archers/Chiefs/Mages, Forsworn, Farmers, Mages, Necromancers, Adventurers, traders and workers. Mod sections, including Immersive Wenches/Flower Girls when usable, are built from actual eligible NPCs in the scan. Empty presets are omitted. Counts follow the selected NPC sex.
- **Outfit sex safeguards:** inspects effective ArmorAddon worn-model paths and armor templates. Confirmed one-sex parts lock NPC sex; mixed incompatible parts block generation. A user-confirmed Lunar Claw family hint is used when model support cannot be resolved. Unknown support is labelled, never inferred just from CBBE/HIMBO/3BA names. Model paths do not prove mesh, race or body compatibility.
- **Startup:** the embedded app icon appears in a small loading window before the workspace opens. Saved-path scanning continues to discover installed NPC populations at launch.

**Upgrade note:** old manual smart-filter inclusion overrides are cleared once so broad overrides cannot bring CC/frameworks back automatically. Saved outfit setups, generated patches and personal hiding choices are preserved. Re-include any legitimate candidates you still want. Existing patches require rescan/regeneration to adopt new recipient/sex settings; opening the new EXE does not rewrite them.

The 0.4.5 code builds successfully. New UI interactions, classification results on a full installed modlist, and in-game distribution have **not** been runtime-tested in this update. See [research, policy and limitations](docs/SMART_SCAN_AND_PRESETS.md). Documentation screenshots below show 0.4.1.

## Smart folder detection (0.4.4)

Open **Settings → Smart detect…**. The app looks for MO2 instances and their configured game/profile paths, checks for `Skyrim.esm`, and reports how many enabled profile plugins are visible. Select the matching location to fill the scan folder and profile list together. Detection runs automatically when a saved scan folder is missing, or an MO2 launch would scan a non-Data folder.

**MO2:** launch the app through the correct MO2 instance first. A standalone program cannot see virtual mods simply because it found an MO2 configuration. Detection reads the profile saved in MO2's configuration; if you use a command-line profile override or another active profile, select its actual `plugins.txt` manually. Multiple candidates require a choice instead of silently switching installations.

**Vortex:** deploy first and choose the installed game's Data location. Manual staging scans remain available; use **Game Data folder override** if base-game discovery fails. The override must contain `Skyrim.esm`. Missing and unreadable base masters now produce separate errors instead of an apparently successful zero-NPC scan.

No output folder is guessed, and detection does not change MO2, load order or game files. The 0.4.4 build compiled successfully; its new discovery flow has not been tested against the reporting user's setup or in-game. Earlier screenshots show the 0.4.1 interface.

## New in 0.4.3: protect uniformed NPCs

Guards, soldiers, officers/commanders, housecarls/bodyguards, Thalmor, Penitus Oculatus, Dawnguard and Vigilants are excluded from NPC outfit audiences when identified from scanned record evidence. This also applies to broad **Warriors / All NPCs**, inherited alternatives and restored saved audience choices. The **Guards** group is removed; the wizard requires a scanned NPC roster. Redguard and Imperial civilian races, and ordinary civilian crime factions, are not by themselves protection evidence.

**Updating an existing patch:** rescan, then regenerate **all existing outfit entries** to refresh their recipient lists. Incremental merging keeps untouched entries, so merely installing the new app or patching one additional outfit does not repair older rules. Review **My patch** for the entries that need updating.

Protection is based on record identity/class/faction/keyword/outfit evidence, not visual inspection or a guarantee for every script-driven mod NPC. This update **does not automatically inspect male/female armor mesh compatibility**. Full integration checks passed; no in-game validation was performed for this release. The 0.4.2 MO2 startup compatibility fix is retained. Existing documentation screenshots show the 0.4.1 interface.

## The workflow

**Scan → Choose outfits → Configure → Review → Generate → Install**

1. **Choose outfit plugins.** Scan your installed mods, search the list, and tick the outfits you want to distribute.
2. **Choose who wears them.** Pick an NPC collection, a group such as Bandits or Farmers, and optional roles or races. Add that audience to the outfit.
3. **Choose parts and chance.** Leave out hoods, wigs or other pieces, select a suitable color/set, and set the chance supported by your engine.
4. **Skip mistakes.** Press **Skip outfit** to leave an accidentally selected outfit out of this patch session.
5. **Review and generate.** Check recipient estimates and warnings, then build into a separate output mod folder.
6. **Install.** Export an installable ZIP from **My patch**, install it in Vortex/MO2, and enable the mod and its ESP.

Generation is manual. The app does not watch folders continuously or change your load order automatically.

## A closer look

### Outfit library

Outfits are grouped by source plugin. Search, select all/none, invert selection, reveal filtered candidates, or remember exclusions.

![Outfit library with synthetic example plugins](docs/images/v041-library.png)

### Guided distribution setup

An installer-style sidebar keeps your outfit steps within reach. Choose a collection and group, then narrow the audience with optional role and race selections. Live counts help identify audiences that are too broad or match nobody.

![Current distribution wizard with audience controls and Skip outfit](docs/images/v041-wizard.png)

**Selected the wrong outfit?** Press **Skip outfit** beside Back/Next. Its saved choices and any previously generated version are kept. Select it in the sidebar and press **Include outfit** to undo. Skips remain in effect when returning from review, but reset for a new patch session. Skipping every outfit closes setup without generating anything.

The page scrolls while navigation stays visible. Further down, choose pieces, color variants, audience presets or separately named outfits for the same plugin. **Choose compatible parts** uses slots and color names to help select a set; it cannot inspect how meshes fit together.

<details>
<summary>See outfit parts and color controls</summary>

![Outfit-part checkboxes, color grouping and compatible-parts controls](docs/images/v041-parts.png)

</details>

### Review before writing

Inspect audiences, included parts, chance and possible overlaps. **Back to setup** lets you make changes before generating.

![Review page showing a synthetic example of competing outfit warnings](docs/images/v041-review.png)

### My patch

See which outfits have already been patched, their engine, chance, audience and file-health status. Inspect details, edit/remove outfits, restore a backup, or export an installable mod ZIP.

![Patch overview with synthetic example inventory and details](docs/images/v041-patch-library.png)

## Main features

| Feature | What it does |
| --- | --- |
| Wearable discovery | Reads `.esp`, `.esm` and `.esl` with Mutagen and groups new wearable records by source plugin. |
| Smart filtering | Holds back likely official content, replacers, bodies/hair/tattoos, followers, quests, frameworks and already distributed candidates. Reveal and manually include missed outfits. |
| Installed NPC audiences | Uses effective factions, classes, keywords, races and resolvable templates, including NPC records added by mods. |
| Readable targeting | Collection → group → optional roles/races. Choices come from that group's scanned population. |
| Saved setups | Remembers parts, audiences, sex and chance; supports reusable audience presets and named outfit variants. |
| Incremental patching | Add another source plugin while retaining previous plugins' outfits. Repatching preserves stable generated outfit IDs. |
| Progress and diagnostics | Background scans, cancellation, progress indicators, local scan details and shareable error reports. |
| Recovery and packaging | Output validation, checksummed backups, patch-health information and generated mod ZIP export. |

Smart filtering is a convenience, not a perfect classifier. Reveal filtered candidates if an outfit is missing. Manual inclusion cannot bypass missing/inactive master requirements.

## Optional trader merchandise

Tick **Also sell selected outfit parts through traders** in each outfit's Distribution section. Then tick the trader types you want:

- **Blacksmiths**
- **General goods**
- **Clothing traders**

Selected outfit pieces are added as merchandise. If the outfit plugin also contains playable weapons, **Include weapons from this plugin** makes them available through blacksmith/general goods stock. **Only weapons are excluded from clothing traders**: the clothing and armor pieces still go to clothing traders when selected. A shared stock container also used by a clothing trader is kept free of these weapon additions.

Merchant stock targets are derived from encountered NPCs' vendor factions and linked container records in the scan. Unknown trader types and containers also used by non-vendor placements are held back. This does not automatically make every mod-added trader supported.

**Merchandise requires SkyPatcher, including when NPC outfit distribution uses SPID.** Enable its container patching option (`iEnableContainerPatching=1` in your installed `SkyPatcher.ini`). The app writes a separate container file at `SKSE/Plugins/SkyPatcher/container/WearableDistributor/WearableDistributor_Merchandise.ini`. Keep it installed with the patch. The selected NPC chance does not affect shop stock; new stock entries add one copy of each item if it is not already stocked. Existing saves may need a normal merchant restock.

This checkbox adds merchandise alongside the outfit's NPC distribution. Choices are remembered, included in merge/removal/backup handling, and packaged by **Installable ZIP**. Shop visibility, vendor buy/sell rules and other mods can still affect results; live-game verification remains outstanding.

![Trader merchandise checkboxes with synthetic demo containers](docs/images/v041-merchandise.png)

## Requirements

| Purpose | Requirement |
| --- | --- |
| Run the app | Windows x64 and [Microsoft .NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/10.0). Choose **Desktop Runtime**, not only the base runtime. |
| Scan | Installed Skyrim SE/AE plugins exposed through deployed `Data`, MO2 virtual `Data`, or a mod staging folder. |
| Use the patch | Source outfit mods, required NPC/target plugins, and the selected engine with its own listed dependencies. |
| SPID output | [Spell Perk Item Distributor](https://github.com/powerof3/Spell-Perk-Item-Distributor) and its requirements for your Skyrim/SKSE runtime. |
| SkyPatcher output | [SkyPatcher](https://github.com/Zzyxz/SkyPatcher) and its requirements for your Skyrim runtime. |
| Build source | Windows, .NET 10 SDK and NuGet access for dependency restoration. |

Skyrim does not need to be running while you use the app. Legendary Edition and VR support are not established by this preview.

## Download and first setup

1. Download the Windows app ZIP from [Releases](https://github.com/waitWTF/Skyrim-SPID-Patch-generator/releases) when an asset is available. Source-and-app bundles contain the EXE in `app/`. If no compiled asset has been uploaded, build from source below.
2. **Extract the entire ZIP** into a normal folder. Open `Skyrim SPID Patch Generator` and keep its `Runtime` and `Documentation` subfolders beside the EXE. Do not run inside the archive or copy only the EXE. For an upgrade, use a fresh folder and update any MO2/Vortex shortcut to the new top-level EXE.
3. Start `SkyrimSPIDPatchGenerator.exe`.
4. In **Settings**, choose the scan folder, distribution engine, default chance and a separate output mod folder.
5. Supply your active profile's `plugins.txt` when available. It identifies enabled plugins and order. `loadorder.txt` supplies order but cannot identify disabled plugins.
6. Scan and follow the workflow above.

<details>
<summary>See Settings with example folder paths</summary>

![Settings with synthetic scan, profile and output paths](docs/images/v041-settings.png)

</details>

### Mod Organizer 2

Add the EXE to MO2's executable list; set **Start in** to its containing folder. Launch through MO2 to expose virtual `Data`. Choose that folder and the active profile's plugin list in Settings.

Export **My patch → Installable ZIP**, install through MO2's archive installer, and enable the new mod and `WearableDistributor.esp`.

### Vortex

Deploy first. Scan deployed Skyrim `Data` for the closest view of what the game sees. A staging folder also works, but duplicate plugin copies may be ambiguous. You can add the app as an executable tool in Vortex.

Export **My patch → Installable ZIP**, drop it onto Vortex's Mods page or use **Install From File**, enable it, and deploy. Enable `WearableDistributor.esp` in **Plugins**. A generated folder on disk is not automatically registered as a managed Vortex mod.

The app does not directly manage either manager's profiles, deployment or plugin activation.

## SPID or SkyPatcher?

| | SPID | SkyPatcher |
| --- | --- | --- |
| Chance | Configurable 1–100% per named outfit | 100% assignment in this app |
| Rule file | `WearableDistributor_DISTR.ini` | `SKSE/Plugins/SkyPatcher/npc/WearableDistributor/WearableDistributor.ini` |
| Shared output | `WearableDistributor.esp` and `WearableDistributor.patch.json` | Same |
| Behavior | Overlapping audiences for one outfit are combined before writing its chance rule. Different outfits can still compete. | Default outfit assignment for matching NPCs; does not give exclusive control over other mods. |

**Bandits + Archers + 25%** means a per-NPC chance for the resolved bandit archer audience, not exactly one quarter of every encounter. Multiple selected roles/races are alternatives within their category; different categories narrow the audience together.

**100% does not replace an outfit mod everywhere.** It applies to the configured audience. Outfit distribution replaces the affected NPC's outfit, so include a suitable complete set if you want them fully dressed.

## Add more outfits later

Use the **same output folder**. Later generations merge new source plugins into the existing ESP, rules and inventory. You do not need to select every previous plugin again.

Repatching replaces only the selected named outfits. Other variants from the same source plugin and explicitly skipped outfits are preserved. To delete an existing outfit, use **My patch → Remove outfit**.

Keep the ESP, selected engine's INI and manifest together. The installable ZIP includes generated files and instructions; it does **not** bundle third-party meshes, textures or source plugins.

**Use one engine per output folder. Enable only one Skyrim SPID Patch Generator output mod at a time:** all output folders use `WearableDistributor.esp`.

## Limits and troubleshooting

- **No outfits found:** check that the folder exposes installed plugins. Launch through MO2 when applicable, check the profile list, reveal filtered candidates, and read **Settings → Scan diagnostics**.
- **An audience is missing:** only usable scanned targets are offered. Missing masters, unreadable plugins, unresolved templates or insufficient encounter evidence can exclude candidates. Farmers use occupation/faction/package evidence, not an NPC name containing “farm.”
- **Counts differ in game:** counts describe candidate NPC base records, not spawned actors. SPID can also match template/original bases. Static records cannot fully establish quest activation, script-only spawning or runtime changes.
- **Changed NPC mods/profile?** Rescan and repatch. Recipient lists are a scan snapshot, not a live monitor.
- **Outfits look wrong:** check source mods, BodySlide/body/skeleton requirements and selected pieces. There is no 3D mesh preview; slot/color assistance cannot prove visual compatibility.
- **Other outfits take priority:** other distributors and game behavior can compete. Template-inherited inventory, especially with SkyPatcher, still needs in-game verification.
- **Output is damaged:** inspect patch-health details and backup recovery. Old backups without checksum metadata cannot be restored automatically by this preview.

Settings are under `%LOCALAPPDATA%\WearableDistributor`; generated-output backups use its `Backups` folder. New automatic error logs use `SupportLogs`. Click **Error reports → Save report** for sharing. Older `Logs/application-*.log` files, settings exports and patch-content exports can contain private paths or mod details and are excluded from the shareable report.

## For modders and contributors

Corrections, improvements, forks and alternative implementations are welcome. [Open an issue](https://github.com/waitWTF/Skyrim-SPID-Patch-generator/issues) with ideas or problems. Describe what you clicked and attach **Error reports → Save report**. You can also state your manager and game version if relevant. Do not attach another author's plugin/assets without permission.

Useful contribution areas: NPC/template resolution, wearable classification, runtime verification, accessibility, manager integration and UI usability.

### Build

```powershell
dotnet restore src/WearableDistributor.csproj
dotnet build src/WearableDistributor.csproj -c Release
dotnet run --project src/WearableDistributor.csproj
```

**`src/` is the authoritative WPF application.** Older prototype folders are not the current build target. Published apps are framework-dependent, not self-contained single-file EXEs.

| Location | Contents |
| --- | --- |
| `src/` | UI, scanner, targeting, rule compiler, patch generation, inventory, recovery and settings services |
| `tests/` | Synthetic integration checks for generation, merging, targeting, backups and packaging |
| `tools/UiPreview/` | Isolated UI preview/documentation renderer and layout checks |
| `tools/Publish-Release.ps1` | Windows/source ZIP packaging and SHA256 checksums |
| `docs/` | Architecture, verification guidance, review notes and images |

See [architecture](docs/ARCHITECTURE.md), [verification guidance](docs/TESTING.md), [development review](docs/REVIEW-0.4.1.md) and [project context](PROJECT.md). Automated checks and static scans do not establish in-game behavior; live-game testing remains outstanding.

## AI assistance, credits and license

**This project was developed with substantial AI assistance**, including architecture, implementation, UI iteration, documentation and verification tooling. Human review, testing and contributions remain essential. Please judge and improve the code on its actual behavior.

Thanks to the authors/contributors of [Mutagen](https://github.com/Mutagen-Modding/Mutagen), [SPID](https://github.com/powerof3/Spell-Perk-Item-Distributor), [SkyPatcher](https://github.com/Zzyxz/SkyPatcher), MO2, Vortex and the Skyrim modding community. MO2/Vortex installer workflows inspired the interface. This independent project is not affiliated with those tools or Bethesda.

Licensed under [MIT](LICENSE). Dependency licenses remain applicable. Outfit mods/assets belong to their authors and are not redistributed by this tool.


### MO2 startup compatibility (0.4.2)
Version 0.4.2 fixes an immediate startup crash reproduced with MO2 2.5.2 / USVFS 0.5.6.1. The app uses the [documented .NET CET compatibility opt-out](https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/9.0/cet-support), also [used for this MO2 problem by Synthesis](https://github.com/Mutagen-Modding/Synthesis/issues/521). This disables CET hardware stack protection for this app host only; it does not alter Windows-wide security settings. Launch was verified with MO2's virtual filesystem DLL attached. Distribution still has the preview limitations described above.
