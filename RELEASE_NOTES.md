# 0.1.1 preview — 2026-10-16

- Removed the duplicate prototype project that only saved `generation-draft.json`; the repository now has one authoritative build path under `src/`.
- Rebuilt the Windows x64 app from `src/WearableDistributor.csproj`.
- Generate Patch writes `WearableDistributor.esp` and the selected SPID or SkyPatcher distribution file.
- Existing smart scan, per-plugin setup wizard, and structural read-back validation remain included.
- Live Skyrim validation across different load orders is still needed.
