# Test build 5 — verification results — 20 October 2026

This iteration fixes errors while retaining the test-4 interface and workflow.

## Passed

| Area | Result |
|---|---|
| Existing integration suite | Passed: merging, stable IDs, sibling preservation, variant removal, backups, output locks, corruption/interruption handling, boolean targeting, chance rules, scanner overrides/templates, guard protection, settings, archive export and output health. |
| Current regression scenarios | 33 passed, 0 failed. Includes optional leveled-item percentages, incremental updates/pruning, merchant-only generation, weapons excluded only from clothing traders, shared trader containers, sex restrictions, lewd opt-in/reset, overlapping slots, multi-location discovery, malformed/missing metadata, truncated ESP isolation, official/CC exclusion, implicit masters, mutable audience filters, settings round-trips and log privacy. |
| WPF workflow checks | 18 passed: animated icon loading, lazy pages, prepared targeting reuse, sex locking, audience addition, trader-only checkbox, Next/Skip behavior, saved choices, optional percentages, unvisited-page saves and checked/highlighted hide/unhide actions. |
| Minimum-window layout/accessibility | Wizard 980x600, review 940x580, main/settings 1000x660: all four passed. Scrollability, navigation placement and main-page accessibility checked. |
| Installed desktop app | Direct launch succeeded; live scan showed 37 equipment-only, 93 mixed/hidden, and 36 patched source mods across two generated patch locations. Library/overview inspected through Windows UI Automation. |
| Installed data, read-only audit | 998 successfully parsed plugins, 12,999 NPC profiles / 5,690 eligible bases, 130 source candidates, 2 patch files / 36 source mods. No game files generated or modified. |
| Cache timing on that audit | Approximately 15.4 seconds scanning and 7.9 seconds first audience preparation. Twenty repeated preset lookups returned the same cached list in under 1 millisecond. This measures data lookup, not full window rendering. |

## Confirmed problems fixed

- Null inventory collections or null entries could throw during health inspection and prevent other patches from appearing. Incomplete inventories are now normalized for read-only inspection and flagged for review; the writer rejects empty/incomplete entries safely.
- A truncated generated ESP could throw a Mutagen parser exception outside the reader's narrow catch. Inspection now isolates failures per patch location and preserves healthy results from other locations.
- The existing UI harness's settings override was ignored by the experimental build, and its theme URI still used the former assembly name. Test isolation and resource references are corrected. The startup icon URI explicitly identifies its containing assembly.
- An inherited test expected repatching to delete an unselected sibling variant. The current intended behavior preserves it. Updated the test expectation; no product behavior was changed to satisfy that obsolete expectation.

## Limits

The user requested skipping the live MO2 launch check. MO2 reached its Steam prompt; that pending launch was canceled. A restricted secondary launcher also produced MO2 error 0xc0000022, which is distinct from an app startup failure. Do not describe MO2 launching as passed. The synthetic implicit-master/profile checks passed, and the existing CET compatibility setting was retained without changing Windows security settings.

No Skyrim/SKSE launch, in-game visual check, SPID engine run, SkyPatcher restock check or save-file compatibility check was performed. File-level validation and synthetic fixtures do not prove every third-party mesh, quest or runtime distribution.

## Reproduce

Run `dotnet run --project tests/WearableDistributor.IntegrationChecks.csproj -c Release` for integration/regression checks. Test artifacts are retained in a unique `test-results/integration-*` directory for debugging; logs/settings/backups are isolated there.

Run the UI harness with `workflow-checks`, or with `wizard --small --check`, `review --small --check`, `main --small --check` and `settings --small --check`. Its settings live under the harness's `preview-settings` directory.

The integration runner also accepts `audit <Data path> <report JSON>` for a read-only installed-mod scan and audience-cache measurement. Reports contain summary counts/timings; normal scan diagnostics and screenshots can contain local paths and should be reviewed before sharing.
