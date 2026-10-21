# v0.4.11 preview verification — 2026-10-21

## Scope

Opt-in random outfit pools address multiple independent SPID outfit rules competing for the same NPCs. The generator now offers one shared coverage roll and a weighted selector of complete member sets. Daily clothing rotation is not included.

## Automated evidence

- 40 new wardrobe generation/integrity assertions cover three 100% members producing one shared rule, 1:1:2 tickets, complete UseAll bundles, no illegal LVLI-to-OTFT references, nested matching colors, optional accessories, stable FormIDs, partial repatching, retaining unrelated outfits, shared-scope rejection, invalid weights/member limits, merchant independence, tampered selectors/metadata/rules, member removal/ownership transfer, single survivors, backups, pool dissolution, preference persistence and multiple pools.
- Existing integration suite passes, including 35 retained regressions, 24 mesh-safety assertions and 26 garment-variant assertions, plus scanner, rule, package, transaction and inventory checks.
- 13 new WPF wardrobe workflow assertions cover the editor list, live percentages, Apply/Cancel, checked-row removal, shared configuration, independent target snapshots, actual wizard editing, stale-control prevention, saving/reopening and completing review without reapplying the pool.
- The existing 33 WPF workflow assertions remain passing.
- Synthetic wardrobe previews are checked at normal 1000×740 and minimum 850×580 sizes. No installed mod files are read for these UI fixtures.

## Upgrade and limits

Standalone outfit settings and schema 1–6 outputs remain supported. Opted-in pools require schema 7 and app 0.4.11+ for editing. The editor applies a shared audience, sex and coverage explicitly; generated output is staged, fully validated, backed up and committed. Pool-aware removal and backup restoration use the same transaction protections. The app installer update does not rewrite the user's game patch.

Use SPID 7.2+ for per-actor outfit distribution. Tests validate files, topology, configured probabilities and UI behavior; they do not prove in-game random outcomes, NIF visual compatibility, NPC script behavior or outfit reset timing. No Skyrim launch or new MO2/Vortex integration test was performed for this feature. The app retains its existing MO2 apphost/VFS behavior and CET compatibility setting.
