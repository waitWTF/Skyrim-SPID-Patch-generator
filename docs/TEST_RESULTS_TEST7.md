# Test build 7 — focused configuration and setup performance

20 October 2026. This local experimental iteration addresses the report that Configure opened every outfit, setup took too long, and “Add audience”/completion steps were unclear.

## Changes

- New scans leave all outfit batch boxes unchecked. Existing distribution settings, generated patches and exclusion preferences are preserved.
- Right-click **Configure this outfit** opens only the clicked plugin, independently of other checked/highlighted rows, search results and bulk-action scope.
- The main button reads **Configure checked (N)**. The checked-count summary also reports checked plugins outside the current search/tab; search does not silently change a deliberate batch.
- **Add NPC group** replaces Add audience, with a tooltip explaining that it chooses existing NPC recipients rather than adding NPCs to the game.
- **Save & close** stores outfit settings and returns to the library. **Review & generate** proceeds toward file generation. Saving setup is distinct from generating patch files; merchant-only output remains supported.
- NPC record-name evidence and collection/faction labels are cached for each scan, avoiding repeated tokenization/classification work. Dropdown protection labels no longer construct full expanded NPC rules just to display a warning.
- Existing cached audience preparation, loading animation and lazy outfit pages are retained. Repatch handlers now await the complete setup task before clearing their temporary scope flags.

## How to use

For one outfit: search → right-click its row → **Configure this outfit** → select NPC distribution and/or trader stock → choose parts → **Save & close**, or **Review & generate** to create the patch after review.

For several outfits: tick only their **Batch** boxes → **Configure checked (N)**. Each chosen plugin has its own setup step. Named variants saved for that plugin may also appear; unrelated plugin mods are not opened.

NPC distribution: choose the collection/group and any role/race refinements, then click **Add NPC group**. Choose a chance and review the resulting recipient list. Trader-only setup needs only the trader checkbox and a valid trader category, with no NPC group required.

## Verification results

- Existing integration suite passed, plus **34 current regression scenarios**. Added an equivalence check between the indexed protection policy and the original policy, including inherited variants, and verified scans do not auto-check eligible plugins.
- **29 WPF workflow assertions** passed, including single-row setup while 500 other plugins are checked, search/batch scope, one lazy outfit page, plain NPC-group wording, and Save & close persisting only the intended outfit without approving generation.
- All four small-window layout/accessibility checks passed: wizard 980x600, review 940x580, main and settings 1000x660.
- Read-only before/after audit used the same 1,138 installed plugins / 13,969 NPC profiles. First audience preparation measured **7,850.98 ms before** and **6,598.36 ms after**, approximately **16% less time** in these single runs. This measures audience preparation, not complete window rendering; timings vary with machine load.
- Both audits produced the same 136 source candidates (37 equipment-only / 99 mixed), 5,706 eligible NPC bases, 36 patched sources, and preset counts (Any 862 / Female 523 / Male 674). No game files written.
- The unnecessary test-6 cursor hook was removed after the user confirmed repairing their Windows custom pointer scheme fixed both versions.

 This file does not claim the reporter's exact modlist or 2–3 minute delay was reproduced. Tests use synthetic outputs and a separate read-only installed-data audit; no game patch is generated in the real installation.

The preceding test build 6 was verified as an MO2 2.5.2 tool: real launch, attached USVFS, correct detected profile and 128/128 active plugin visibility, including virtual-only files. See the local MO2 report for details. No Skyrim/SKSE launch is needed for those checks.
