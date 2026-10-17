# Wearable Distributor project

## Purpose and source
Windows WPF app for manually generating NPC outfit ESP + SPID/SkyPatcher configuration from installed Skyrim wearables. Authoritative project: src/WearableDistributor.csproj. Current base: 0.3.0-preview. Public source URL: https://github.com/waitWTF/Skyrim-SPID-Patch-generator . Existing working-tree edits predate this review; preserve them.

## History reviewed
This development conversation and original ChatGPT conversation 6ad13609-4e6c-83ed-bcb6-8b87e5dc27aa (Skyrim SPID Patch Generator) were reviewed. The original conversation's assistant bodies are returned as reference placeholders, so those missing bodies cannot be independently reconstructed. Other listed Skyrim chats concern unrelated troubleshooting and were not treated as requirements. A Wearable Distributor sidebar section groups the two relevant chats. No saved Codex projects were returned and there is no callable project-creation API; this document defines the local development project.

## Requirements carried forward
- Beginner workflow like Pandora: scan, select outfit plugins, guided per-plugin pages, review, generate game-installable patch.
- Exclude official content/replacers/bodies/hair/tattoos/magic/followers/quests heuristically, with manual recovery and remembered overrides.
- Real installed NPC groups, understandable faction/occupation/archetype hierarchy, optional multi-select roles and races, editable title suggestions.
- Remember user choices across sessions, merge previous patches, preserve unrelated outfits.
- Patch overview with status, details, editing/removal, backups, variants, progress, preferences, and portable settings.
- Windows EXE with icon, documentation/source, Vortex/MO2 installation guidance, honest preview limitations.

## First review changes (2026-10-16, based on v0.3.0)
- Fixed generator using the complete available-target catalog when no audience was selected. Missing selected audience now fails before output mutation.
- Fixed repatching leaving obsolete named variant ESP records. Cleanup now uses prior manifest ownership and preserves records referenced by retained framework entries.
- Fixed review-window DockPanel child order so footer is docked before the filling results grid.
- Added integration regressions for absent audience preserving old ESP and obsolete variant cleanup.

## Prioritized development backlog
1. Output integrity: unify duplicate transaction/backup implementations; preflight all restore inputs; verify backups and prevent partial restore; validate manifest paths; document/restrict framework switching in shared outputs.
2. Scan correctness: load-order-aware winning records and template inheritance; active-profile support; preserve NPC origin versus plugin providing override; cancellation and non-reentrant scan; diagnostics and immutable scan snapshots.
3. Target correctness: derive roles within matched NPC populations, humanoid/adult defaults with explicit opt-ins, avoid farm-animal and unrelated class matches; clearly explain what records imply and why suggestions are chosen.
4. Rule correctness: check syntax/AND/OR behavior against official SPID and SkyPatcher docs and real logs; prevent expansion blowup; validate all supported target kinds; make estimates account for sex/race/templates and explain independent chances.
5. Usability: persist per-page scroll position, accessible keyboard navigation, clear no-audience validation at setup, explicit missing-master diagnostics, inventory health/status and stale-output detection, split colors without conflicting slots.
6. Architecture: extract scanner, target resolver, settings storage, and patch transaction services from UI; add meaningful focused regressions before changing semantics.
7. Distribution: runtime/startup diagnostics, reproducible release packaging and checksums, avoid stale app/source mismatch, optional self-contained build with clear size tradeoff.
8. Larger features: mesh preview (NIF renderer), deeper manager integration, confirmed disposable-profile in-game testing. These are not implemented.

## Verification and release policy
Run integration checks and publish from this repo after changes. Synthetic checks establish serialization and merge behavior, not correct game distribution. Prior SKSE attempt exited without Skyrim; no live-game success. Do not push/upload changes without user instruction. Do not silently replace historical v0.3.0 assets with a development build. Publish review build separately under publish-review and provide it as a development preview.

## 0.4.0 implementation checkpoint — 17 October 2026

The broad improvement pass is implemented in the authoritative src folder; historical 0.3 packages remain. Read docs/ARCHITECTURE.md, docs/REVIEW-0.4.0.md and docs/TESTING.md for the updated technical map, verified scope, and manual work still needed. The new UI is dark/teal and FOMOD-inspired, with a library/inventory/settings shell, per-outfit step navigation, live populations, readable roles/races, one-click parts and nested scroll chaining.

The scanner now runs in the background with cancellation and validates effective profile ordering, installed Skyrim.ccc implicit resources, source masters and per-plugin atomic snapshot ingestion. NPC audiences conservatively resolve direct/nested leveled templates and use actual group populations. Missing/cyclic/ambiguous/script-only behavior remains explained rather than assumed. Group evidence ignores exclusion factions; Wenches fused identifiers and Farmer occupations are covered by tests.

Distribution fixes union audiences before SPID chance, use correct SkyPatcher Plugin|hex syntax, retain runtime adult exclusion and preserve stable record allocation after removing the last outfit. Shared output validation/transactions cover ESP+INI+manifest, recovery integrity, missing/truncated rules and archive export. My patch checks health and can export an installable generated-mod ZIP.

Current code is 0.4.0-preview. This local source has not been pushed/uploaded. No in-game confirmation or NIF renderer/native manager API is claimed. The read-only installed-mod audit found 948 plugins/192 wearable candidates/7,013 eligible NPC bases in 21.6 seconds, with one skipped Mutagen quest-parser failure. Reports under the outer reports folder are local-only and contain paths/mod names; public packaging omits them.

Future priorities: complete disposable-profile runtime tests, verify SPID template-descendant boundaries and SkyPatcher template-inventory behavior, improve actual mesh/body compatibility previews, and add managed profile integration without weakening present guards.

