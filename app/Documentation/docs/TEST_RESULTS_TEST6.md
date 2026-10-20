# Test build 6 — resize cursor correction — 20 October 2026

The user confirmed the pointer disappears only at the outer resize border/corners, while interaction continues working. There was no explicit cursor-hiding code in the project. This build adds a narrowly scoped candidate fix; it does not establish the underlying Windows/display cause or prove visibility on the user's screen.

## Follow-up

The user confirmed corruption of their Windows custom cursor scheme; repairing the scheme fixed both stable and test versions. Test 7 removes this unsuccessful app workaround and its dedicated harness mode. The checks below are historical implementation tests, not evidence that this handler fixed the reported disappearance.

## Change

Each themed app window now attaches a native WM_SETCURSOR hook. It explicitly chooses the appropriate system horizontal, vertical or diagonal resize cursor for the eight sizing regions. If that cursor resource cannot load, it tries the normal arrow; if both loads fail, Windows handling continues. This does not detect an existing but visually transparent system cursor resource.

Client content, controls, title bar and child-window messages pass through. Maximized/minimized and nonresizable windows are excluded. Windows still owns hit testing and resizing. Hook registration is idempotent and removed on close. No global Mouse.OverrideCursor, ShowCursor counter, cursor scheme or Windows setting is changed.

## Results

- 47 targeted cursor assertions passed: sizing directions/modes, excluded regions/states, fallback selection, actual Windows resource loading, attachment/cleanup, client and child-message pass-through, and control cursor preservation.
- The inherited integration suite and all 33 current regression scenarios passed again.
- All 18 WPF workflow assertions passed again, including saved choices, merchant-only output, Next/Skip and checked/highlighted actions.
- Manual visibility at the border remains pending user confirmation. Loading a cursor handle does not prove that its image is visible.

Previous verification and limits remain in TEST_RESULTS_TEST5.md. No live MO2 check was retried, no Skyrim/SKSE started, and no real game patch generated for this correction. The interface and distribution process are unchanged.

## Reproduce

From the experimental source root:

```powershell
dotnet run --project tools/UiPreview/WearableDistributor.UiPreview.csproj -c Release -p:NuGetAudit=false -- cursor-checks
dotnet run --project tools/UiPreview/WearableDistributor.UiPreview.csproj -c Release --no-build -- workflow-checks
dotnet run --project tests/WearableDistributor.IntegrationChecks.csproj -c Release -p:NuGetAudit=false
```

Move the pointer over every outside edge and corner of a normal, resizable app window, then back over buttons and text fields. Verify visible horizontal/vertical/diagonal arrows, normal resizing, and normal hand/text cursors inside controls.

Implementation follows Microsoft's WM_SETCURSOR message and shared system cursor resource handling:

- https://learn.microsoft.com/en-us/windows/win32/menurc/wm-setcursor
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-loadcursorw
