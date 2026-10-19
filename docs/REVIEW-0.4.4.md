# 0.4.4 — smart scan location discovery

19 October 2026. Build-only verification for this update; no new tests, UI exercise, reporter-setup reproduction or in-game validation performed.

## Scope
- Discover nearby portable MO2 configuration and registered LocalAppData instances without scanning entire drives.
- Inspect the current process for the attached USVFS module. Read configured gamePath, selected_profile and custom base/profile paths; support Qt ByteArray escaping and BASE_DIR expansion.
- Present candidates with Data/profile paths, Skyrim.esm presence and enabled-plugin visibility counts. Require selection; do not silently choose between installations.
- Standalone detection of MO2 configuration cannot expose virtual mods. MO2 candidates require an attached virtual filesystem. Saved profile names may differ from runtime command-line overrides; the UI instructs users to review/manual-select the actual plugins.txt.
- Preserve manual staging scans and provide an optional base-game Data override. Detection does not choose an output folder or change MO2/game/load-order configuration.
- Require a visible base master and distinguish missing from unreadable Skyrim.esm. Official implicit masters remain independent of profile-list star markers.
- Clear previous scan data when applying a discovered location, before starting its new scan. Include scan root, base Data, profile list and attached/not-detected state in diagnostics.

## Limits
Candidate discovery is bounded and may miss portable MO2 instances unrelated to current paths when the app is standalone. It is not a global filesystem index. Module attachment does not identify every registered instance definitively, and visible plugin files do not prove enabled load order; choose the matching instance/profile. The original report supplied only a header, so this release does not claim the commenter's exact missing-master issue is confirmed resolved.

Guard exclusions, existing output identifiers and the previously approved CET compatibility setting are retained. Rescan/regenerate existing outfits to refresh old recipient rules. No automatic male/female mesh compatibility check is added.
