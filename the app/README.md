# Wearable Distributor

Wearable Distributor is an open source Windows desktop tool for Skyrim Special Edition mod users. It scans installed plugin records for wearable armor and clothing, groups pieces by the plugin they came from, lets users choose NPC groups and outfit parts, and generates an outfit plugin plus SPID or SkyPatcher distribution configuration.

The interface is designed around a familiar mod installer flow. A user selects plugins, then configures each plugin on its own page, chooses NPC targeting rules and distribution chance, excludes unwanted pieces such as wigs or hoods, and moves to the next plugin.

## Project status

This is an early release. The Generate Patch action now writes `WearableDistributor.esp` with OTFT outfit records and the selected distribution file. The app reads the generated plugin back through Mutagen and checks that the expected outfit records and master references were written; it also checks the distribution row count. The patch has not been validated in a live Skyrim runtime across different mod lists, so users should test it in a separate mod-manager profile and report problems.

Active-profile load order discovery and in-game verification across different mod lists remain future work. The scanner and its exclusions are heuristics and should be reviewed against real mod setups.

## Included features

- WPF Windows desktop application targeting .NET 10.
- Mutagen Bethesda Skyrim package for reading Skyrim plugin records.
- Remembers the selected Skyrim `Data` folder and preferences under `%LOCALAPPDATA%\WearableDistributor\settings.json`.
- Attempts a common Steam or GOG Data folder discovery on startup.
- Scans `.esp`, `.esm`, and `.esl` files found recursively under the selected folder and reads armor records with wearable slots.
- Presents candidates grouped by plugin, including the candidate record count, filter note, and scanned path.
- Smart filtering heuristics for base game and DLC, Creation Club, LOTD, replacers, hair, tattoos, body mods, magic, followers, quests, frameworks, creature mods, NPC overhauls, and other known false positives.
- Allows showing candidates held back by filters and remembering a manually included candidate. Users can also remember exclusions and restore them.
- Plugin list controls for select all, select none, invert selection, enable selected, and disable selected.
- A per-plugin FOMOD-style wizard with Back/Next navigation, progress, chance and sex choices, and outfit piece checkboxes. Color and other detected variants are grouped in the piece list.
- NPC targeting choices from Skyrim and scanned plugin records: keywords, factions, classes, and races. The wizard groups choices by category, provides a specific group dropdown and search, and lets users add/remove several target groups per plugin.
- Generates one new OTFT outfit per selected source plugin, containing that page's checked armor pieces.
- Generates a SPID `_DISTR.ini` or a SkyPatcher NPC INI alongside the output ESP. Selected categories are combined as required filters; multiple choices within a category are emitted as alternative rules.
- SPID output supports the selected 1–100% chance. SkyPatcher's NPC outfit rules do not support per-NPC random chance; generation requires 100% and assigns the outfit to every matching NPC.
- Lets the user choose a new MO2 mod folder or Skyrim `Data` folder for output, then remembers that output path.

## How NPC targeting is represented

Mutagen reads keyword, faction, class, and race records from the plugins found under the selected Data folder. These populate the wizard's group type and specific group dropdowns. Examples include Skyrim keywords such as `JobFarmer` and mod-added records whose editor IDs may identify a necromancer, adventurer, or bandit group.

This is a record browser, not a complete semantic NPC database. A faction, class, or keyword only matches NPCs that actually carry that record. Plugin records found in a folder are not necessarily all active in the user's mod manager profile. Duplicate plugin names and disabled mods can therefore produce confusing or duplicate choices. Using the active profile's virtual Data view is recommended when a mod manager offers one.

SPID's `Outfit` rule references the generated OTFT form and uses string filters for keywords, form filters for factions/classes/races, trait filters for gender, and the chance field for probability. SkyPatcher output writes NPC filters and an `outfitDefault` assignment to `SKSE\Plugins\SkyPatcher\npc\WearableDistributor\WearableDistributor.ini`. SkyPatcher cannot honor the chance setting, so the app rejects values below 100% when that framework is selected. The generated ESP declares the source armor plugins as masters; those source plugins must remain installed and enabled.

## Build and run from source

Requirements:

- Windows 10 or newer
- .NET 10 SDK
- Internet access on the first restore, unless the NuGet dependencies are already cached

From this directory:

```powershell
dotnet restore
dotnet build -c Release
dotnet run
```

The project file is `WearableDistributor.csproj`. The principal files are:

- `MainWindow.xaml` and `MainWindow.xaml.cs`: plugin list, preferences, scan, filtering, and settings persistence.
- `PatchOptionsWindow.cs`: per-plugin distribution wizard.
- `Models.cs`: scanner, preference, and wizard data models.
- `App.xaml` and `App.xaml.cs`: WPF application startup.

## Creating a standalone EXE

The project is configured as a Windows GUI app (`WinExe`). A framework-dependent publish creates an app executable but requires the matching .NET Desktop Runtime on the user's machine:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

For a self-contained single-file distribution that does not require a separately installed .NET runtime:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The output will be in `bin\Release\net10.0-windows\win-x64\publish\`. Test the published executable on a clean Windows account before releasing. The target user should be able to add its full path as a tool in Mod Organizer 2 and start it. For MO2, configure the executable's **Start in** directory to the app folder if needed, and point the app at the correct Skyrim Data view. Vortex users can launch the executable directly or add it to their dashboard/tool list if supported by their setup.

When using **Generate Patch**, choose an empty output folder or a new MO2 mod folder. The output folder contains `WearableDistributor.esp` and either `WearableDistributor_DISTR.ini` (SPID) or the SkyPatcher INI at its required subfolder path. Install that folder as a mod in MO2 and enable the generated ESP, the selected source armor plugins, and SPID or SkyPatcher. If generating into Skyrim's real `Data` folder, use the matching plugin manager to enable the ESP.

The current app scans a folder path itself. It does not yet integrate with MO2's profile state, Vortex deployment state, or virtual file system automatically, so users must confirm that the chosen folder exposes the plugins they intend to scan.

## Source and contributions

The source code and project updates are hosted at [Skyrim SPID Patch Generator on GitHub](https://github.com/waitWTF/Skyrim-SPID-Patch-generator). Nexus Mods users can visit the repository to download the source, report issues, and contribute improvements.

Contributions are welcome, especially for:

- In-game validation of generated ESPs and SPID/SkyPatcher distributions across representative load orders.
- Correctly ordering masters from MO2/Vortex load orders and supporting very large master sets.
- Using active load orders and resolving winning/overridden records.
- Better classification of armor, clothing, accessories, outfit sets, and variants.
- Better false-positive filtering with user-editable rules and transparent explanations.
- NPC role discovery and validation, including keywords and factions added by mods.
- Configuration persistence per plugin and safe output previews.
- Automated parser and output validation using representative plugins and game setups.
- Packaging, signing, and testing the standalone Windows release and MO2 launch workflow.

When opening issues, include the app version, Skyrim runtime, mod manager, scan folder type (real Data folder or virtual profile), relevant plugin names, and a redacted log or error message. Do not attach copyrighted mod assets or plugin files unless their authors permit redistribution.

## AI assistance disclosure

This project was developed with AI assistance. AI helped draft the initial architecture, implement parts of the WPF interface and Mutagen scanning prototype, refine filtering and wizard behavior, and prepare documentation. The project owner should review, test, and take responsibility for all code and releases. Contributions and corrections from human modders are encouraged.

## License and release note

No license has been selected yet. Before making the GitHub repository public or accepting outside contributions, the project owner should choose a license and add its full text as `LICENSE`. Also document third-party dependency licenses and the terms for any screenshots or bundled assets. Do not describe the app as generating working patches until a game-ready output writer has been implemented and verified.
