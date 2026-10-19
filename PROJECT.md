# Skyrim SPID Patch Generator

## Current update: 0.4.8

Independent NPC/trader destination checkboxes, merchants-only generation/health/merging, memory-only live Activity history, checked/highlighted visible-row action scopes, remembered unhide overrides, faction/guild presets with selective existing-protection warnings and a Unique-flag NPC group with individual selection are included. Settings/manifests use schema 4 when merchants-only data is present; older files retain NPC mode. See docs/MERCHANTS_ACTIVITY_AND_AUDIENCES.md. Build/package verification only; no new runtime/game checks or automated tests.

## Previous update: 0.4.7

The Windows download has one enclosing folder with a root EXE, Runtime libraries and Documentation. The SDK-generated root apphost loads Runtime/SkyrimSPIDPatchGenerator.dll directly and retains CETCompat=false. Source bundles have their own enclosing folder; app/ uses the same layout. Settings/output paths stay unchanged. See docs/APP_LAYOUT.md. Build/package verification only; the changed layout has not been launch-tested.

## Previous update: 0.4.6

Typed privacy-safe diagnostic logging, a visible Error reports window, per-outfit repair/repatching, Repatch all with direct saved-setup review, remembered output reuse and indexed INI validation are included. See docs/ERROR_REPORTS_AND_REPATCHING.md. Build-only verification; no new runtime checks.

## Previous update: 0.4.5

Outfit filtering, installed-population audience presets, patch-selection context tools, separate already-patched hiding, model-path sex safeguards and an icon startup window are implemented. See docs/SMART_SCAN_AND_PRESETS.md for research, policy, source map and limits. Build-only verification; no new runtime checks were run. The app remains a preview.

Windows WPF application for Skyrim SE/AE. Version 0.4.8-preview.

## Purpose
Scan installed wearable plugins, choose NPC audiences in a guided installer-style workflow, and manually generate an outfit plugin with SPID or SkyPatcher rules. Optional merchant stock uses SkyPatcher container rules.

## Source map
- src/: authoritative application; build src/WearableDistributor.csproj. The internal namespace and generated WearableDistributor identifiers are retained for compatibility.
- tests/: synthetic integration checks.
- tools/: release packaging and isolated UI documentation renderer.
- docs/: architecture, verification guidance, review notes and demo screenshots.
- app/: framework-dependent Windows x64 build; root EXE plus Runtime/ and Documentation/. Keep the whole folder together.

## Design priorities
Readable installed NPC groups; optional roles and races; saved choices; incremental patch merging; stable generated IDs; review before writing; recoverable output transactions; installable mod ZIP exports; Vortex/MO2 compatibility.

## MO2 startup fix
Version 0.4.2 uses the documented app-specific CETCompat=false setting. Startup through MO2 2.5.2 was verified with USVFS attached. See docs/REVIEW-0.4.2.md for the security tradeoff and scope.

## Current limitations
This is a development preview. Static NPC discovery and audience counts cannot prove runtime encounters or compatibility with every mod. The 0.4.1 merchandise feature has been built and UI-rendered but has not been verified in Skyrim. See README.md, RELEASE_NOTES.md and docs/TESTING.md before contributing.

## Contributing
Issues, improvements and forks are welcome at https://github.com/waitWTF/Skyrim-SPID-Patch-generator . The project was developed with substantial AI assistance and is MIT licensed. Do not commit personal logs, settings, raw mod scans, private conversation exports, credentials or third-party game assets.


## Uniformed NPC protection
Version 0.4.3 filters identified uniformed protectors from all NPC audiences, including inherited alternatives and restored choices. Requires rescan/regeneration for existing output. See docs/REVIEW-0.4.3.md. No automatic male/female mesh-compatibility inspection or new in-game validation is claimed.


## Smart folder discovery
0.4.4 adds bounded MO2/game/profile discovery and explicit base Data override. Configured profiles require review when launch overrides differ. See docs/REVIEW-0.4.4.md. Build-only verification; no new runtime validation claimed.
