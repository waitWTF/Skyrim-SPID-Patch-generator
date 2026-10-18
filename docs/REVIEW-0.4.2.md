# 0.4.2 — MO2 startup compatibility

18 October 2026.

## Confirmed comparison
- Published 0.4.1: direct launch succeeds, repeated MO2 2.5.2 / USVFS 0.5.6.1 launches crash before the WPF window, with Windows Application Error 0xc0000005.
- 0.4.2: same source with app-host CET compatibility opt-out and version label update opens through the same MO2 instance. The visible window displays 0.4.2; its startup diagnostic records 0.4.2.0, and process module inspection confirms MO2's usvfs_x64.dll is loaded.
- The 0.4.1 directly launched comparison process has no USVFS module.

## Change and tradeoff
`src/WearableDistributor.csproj` sets `CETCompat=false`. This is an app-host build property. It disables this app's CET hardware stack protection to permit the older MO2 injection mechanism; it does not alter operating-system security configuration. The user explicitly approved this compatibility tradeoff before implementation/testing.

See [Microsoft's documentation](https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/9.0/cet-support) and [Synthesis issue 521](https://github.com/Mutagen-Modding/Synthesis/issues/521).

## Scope
Build and actual MO2 startup comparison performed. No full integration-suite rerun or new in-game distribution test. The saved configured scan folder was not changed to MO2 virtual Data. Loaded USVFS proves attachment, not correctness of every scan or generated patch. Existing release limitations remain in effect. No Windows security setting or MO2 executable configuration was changed for the fix.
