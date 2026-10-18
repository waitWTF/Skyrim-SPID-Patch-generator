# Skyrim SPID Patch Generator

Windows WPF application for Skyrim SE/AE. Version 0.4.2-preview.

## Purpose
Scan installed wearable plugins, choose NPC audiences in a guided installer-style workflow, and manually generate an outfit plugin with SPID or SkyPatcher rules. Optional merchant stock uses SkyPatcher container rules.

## Source map
- src/: authoritative application; build src/WearableDistributor.csproj. The internal namespace and generated WearableDistributor identifiers are retained for compatibility.
- tests/: synthetic integration checks.
- tools/: release packaging and isolated UI documentation renderer.
- docs/: architecture, verification guidance, review notes and demo screenshots.
- app/: framework-dependent Windows x64 build; keep all accompanying files together.

## Design priorities
Readable installed NPC groups; optional roles and races; saved choices; incremental patch merging; stable generated IDs; review before writing; recoverable output transactions; installable mod ZIP exports; Vortex/MO2 compatibility.

## MO2 startup fix
Version 0.4.2 uses the documented app-specific CETCompat=false setting. Startup through MO2 2.5.2 was verified with USVFS attached. See docs/REVIEW-0.4.2.md for the security tradeoff and scope.

## Current limitations
This is a development preview. Static NPC discovery and audience counts cannot prove runtime encounters or compatibility with every mod. The 0.4.1 merchandise feature has been built and UI-rendered but has not been verified in Skyrim. See README.md, RELEASE_NOTES.md and docs/TESTING.md before contributing.

## Contributing
Issues, improvements and forks are welcome at https://github.com/waitWTF/Skyrim-SPID-Patch-generator . The project was developed with substantial AI assistance and is MIT licensed. Do not commit personal logs, settings, raw mod scans, private conversation exports, credentials or third-party game assets.

