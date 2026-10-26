# 0.4.12 language source preview verification — 26 October 2026

## Scope and result

The Windows Release build and integration checks passed on .NET SDK 10.0.401 / .NET and Windows Desktop Runtime 10.0.12. These are synthetic, isolated application tests. They do not load or modify a user's game/modlist, settings or installed patch output.

| Check | Result |
| --- | --- |
| Release application/test build | 0 warnings, 0 errors |
| Complete non-desktop regression run | 190 passing assertions, including 65 localization assertions |
| Opt-in live WPF language/workflow run | 128 passing assertions |
| Visual inspection | Simplified Chinese Settings renders, text wraps, headings have readable contrast |

## Localization checks

- All five embedded catalogs load and every main-workspace XAML key resolves: English, German, French, Spanish and Simplified Chinese.
- Regional language selection resolves de-AT, fr-CA, es-MX, zh-CN and zh-SG appropriately; Traditional Chinese and unsupported languages retain English fallback instead of silently selecting Simplified Chinese.
- Missing/invalid preferences, an unavailable imported language, unknown plugin/Unicode names, placeholders and malformed community files are covered. Valid overlays merge into embedded fallback; mismatched placeholder indices are rejected.
- Every language preserves regional number culture, original gender/part-mode enum values, FormIDs and source record names through preferences serialization.
- The synthetic SPID ESP/INI output is **byte-identical across all five languages**. The equivalent SkyPatcher ESP/INI output is also byte-identical across all five. This checks fixture serialization/distribution semantics; it does not claim in-game behavior for arbitrary mods.

## Live WPF checks

- Repeated language selection, including Automatic, applies and persists without Save or restarting the process.
- Rebuilt workspaces preserve the exact scanned objects, checked/highlighted outfits, search/tab/action scope, column width/sort order, cached NPC preparation and unfinished Settings fields. Changing language performs no scan or patch discovery, and does not save unrelated unfinished fields.
- Setup and review windows open under every language. Enabling NPC distribution and selecting Female + Bandits keeps the original gender/group/target FormIDs in the saved configuration.
- A simulated active inventory read delays switching and displays the existing loading window. Rapid selections apply the last request; returning to the current language cancels it.
- Protected settings-recovery mode and an exclusively locked settings file allow changing language for the session while retaining original file bytes and showing a translated warning.
- Closing while a request waits cancels it without changing settings or leaving an owned loading window.
- Section-heading contrast and the empty scan-diagnostics message are regression checked.

## Fixes found during verification

- Added an explicit light text foreground to the section-heading style; explicit WPF styles had allowed headings to appear black against the dark cards.
- Refresh the default scan-diagnostics message in the newly selected language while keeping actual technical scan reports intact.
- The test runner now exits unsuccessfully when its existing counted regression suite reports failures, rather than printing overall success unconditionally.

Initial test authoring issues (an incomplete synthetic target and a test-only dispatcher shutdown) were corrected before the successful final runs. These were test-harness issues; they were not changes to game distribution code.

## Reproduce

Requires Windows and the .NET 10 SDK. Restore dependencies first if necessary.

```powershell
dotnet run --project tests/WearableDistributor.IntegrationChecks.csproj -c Release
dotnet run --project tests/WearableDistributor.IntegrationChecks.csproj -c Release -- localization
dotnet run --project tests/WearableDistributor.IntegrationChecks.csproj -c Release -- language-ui
```

`language-ui` opens and closes synthetic WPF windows. It belongs in an interactive desktop session, separate from headless CI. `language-ui-preview` runs the same checks and pauses the final Chinese Settings window for 30 seconds for visual inspection. Test evidence is retained under the ignored `test-results/` directory; review local paths before sharing raw logs. No raw test logs or screenshots containing local paths are part of this source update.

## Limits and publishing status

No Skyrim, SKSE, MO2 or Vortex gameplay/launch compatibility checks were performed for this translation change. Initial translations were AI-assisted and need community linguistic/game-terminology review. Technical evidence and Windows-native text can still remain English or follow Windows language settings.

The GitHub update is **source only**. A new downloadable binary release remains blocked by the dependency/source-distribution review in `DEPENDENCY_REVIEW.json`. That guard is retained; passing tests does not complete licensing review or Nexus clearance. No Nexus upload or page change is authorized by this update.
