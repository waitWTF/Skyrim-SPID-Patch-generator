# 0.4.0 documentation images

These images show the current WPF interface with synthetic records and example paths. They do not show game results, actual installed outfits, or a successful runtime distribution test. No third-party outfit assets are included.

| File | Use |
| --- | --- |
| `v040-banner.png` | GitHub README header or upload cover |
| `v040-library.png` | Choose source outfit plugins |
| `v040-wizard.png` | Choose NPC audience; footer includes Skip outfit |
| `v040-parts.png` | Outfit pieces, color variants and compatible-parts shortcut |
| `v040-review.png` | Review recipients and overlap notes |
| `v040-patch-library.png` | Previously generated outfits and their details |
| `v040-settings.png` | Scan/output folders and preferences |

To regenerate, build `tools/UiPreview/WearableDistributor.UiPreview.csproj` and run its DLL or EXE with a mode, `--docs`, and `--render <file.png>`. Modes: `cover`, `main`, `wizard`, `parts`, `review`, `inventory`, `settings`. Documentation fixtures live only in the renderer. `--docs` does not scan installed mods or generate a patch.

Examples from the repository root:

```powershell
dotnet build tools/UiPreview/WearableDistributor.UiPreview.csproj -c Release
dotnet tools/UiPreview/bin/Release/net10.0-windows/WearableDistributor.UiPreview.dll cover --docs --render docs/images/v040-banner.png
dotnet tools/UiPreview/bin/Release/net10.0-windows/WearableDistributor.UiPreview.dll wizard --docs --render docs/images/v040-wizard.png
```

The interface is rendered directly from application code. The cover uses code-drawn shapes/type and the existing project icon; these are not AI-generated mock screenshots.
