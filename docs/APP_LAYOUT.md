# Application download layout — 0.4.10

## For users

Extract the Windows ZIP. Everything is inside **Skyrim SPID Patch Generator**:

```text
Skyrim SPID Patch Generator/
  SkyrimSPIDPatchGenerator.exe
  START-HERE.txt
  Runtime/
    SkyrimSPIDPatchGenerator.dll
    SkyrimSPIDPatchGenerator.deps.json
    SkyrimSPIDPatchGenerator.runtimeconfig.json
    ... dependency DLLs (release builds omit debug symbols)
  Documentation/
    README.md
    RELEASE_NOTES.md
    LICENSE
    VERSION.txt
    docs/
      ... guides and documentation images
```

Start the root EXE. For MO2, select that EXE as **Binary**, its enclosing folder as **Start in**, and launch through MO2 so virtual Data is visible. In Vortex, update the executable tool's path to that EXE. Do not point either manager at a DLL in Runtime. This is the program download, not the generated mod; export/install generated patch files separately as described in README.

Use a fresh folder when upgrading from earlier flat downloads. Do not move individual DLLs or copy only the EXE. Settings/logs/backups remain under the existing LocalAppData WearableDistributor location; chosen output mod folders are separate. Removing an old program folder is optional and does not migrate or delete those locations.

## For contributors

`tools/Publish-Release.ps1` publishes framework-dependent win-x64 files into `app/Runtime`. `tools/ReleaseLayout.targets` runs only when `ReleaseLayoutRoot` is explicitly supplied by the script. It uses the SDK's **CreateAppHost** task to create `app/SkyrimSPIDPatchGenerator.exe` with the embedded relative entry point `Runtime/SkyrimSPIDPatchGenerator.dll`. The duplicate Runtime EXE is removed. Icons and version resources come from the built application assembly, and DisableCetCompat=true preserves the user-approved MO2 workaround.

This uses the same native host and managed application in one process. The SDK task accepts an embedded application DLL path ([Microsoft SDK source](https://github.com/dotnet/sdk/blob/main/src/Tasks/Microsoft.NET.Build.Tasks/CreateAppHost.cs)). It does not install or bundle .NET; the Microsoft .NET 10 Desktop Runtime x64 is still required. Normal `dotnet build`, `dotnet run` and ordinary `dotnet publish` are unaffected by the release-only property. The target is a customization of the SDK task and should be reviewed when upgrading the SDK.

Managed `AppContext.BaseDirectory` now points into Runtime. Bounded game/MO2 discovery also checks the root process executable's folder. User settings and generated output never depended on the application directory and retain their existing paths.

ZIP creation explicitly prefixes every member with one friendly root directory. The Windows root is `Skyrim SPID Patch Generator`; the source-and-app root is `Skyrim SPID Patch Generator - Source`. The latter contains app/, src/, tests/, tools/ and docs/, plus project documents. MSBuild .targets/.props files and repository dotfiles are included. Both archives carry the same ready-to-run app layout. Documentation is copied with its relative docs/ links intact.

The release script checks that the EXE, Runtime managed entry point/runtimeconfig and Documentation README are present, and reads each archive entry. It does not start the program. For this update only build/packaging were performed; direct launching, MO2/Vortex injection and in-game behavior were not checked. No new tests were added or run.
