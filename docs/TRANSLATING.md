# Interface languages and community translations

Local language preview: **0.4.12**. No translation service, account or network connection is used by the application.

## Using another language

Open **Settings → Interface language**. The default, **Automatic — Windows display language**, reads the Windows user-interface culture at startup. It does not use the keyboard layout or change the PC's region settings.

Included: English, Deutsch, Français, Español and 简体中文 (Simplified Chinese for Mandarin readers). For example, de-AT uses German, fr-CA uses French, and zh-CN/zh-SG use Simplified Chinese. Unsupported languages use English. Traditional Chinese regions are not silently mapped to Simplified Chinese; users can explicitly select 简体中文 if desired.

Select a language and wait for the animated app icon. The interface updates and the language preference saves automatically; **no Save button or restart is needed**. A current scan or patch operation finishes before the interface changes. The refresh reuses the current scan and cached NPC data, preserves checked/highlighted outfits, library filters, window position and unsaved Settings fields, and keeps the session activity history. It does not start another scan or generate patches. Other unsaved settings still use Save settings. If the settings file cannot be saved, the language applies for the current session and a message explains the issue; an unreadable settings file is preserved. Imported preferences also apply their language automatically once any resulting scan has finished. Older settings without a language preference use Automatic.

Plugin/outfit names, mod-provided record names, paths, EditorIDs, FormKeys, engine names, saved rule values and generated patch syntax are not translated. Dropdowns display translated labels while retaining their original English internal values. Diagnostic codes and shareable technical reports stay stable for support. Some detailed scan/validation evidence, native Windows dialog buttons and technical exception details can still appear in English or the Windows language. This is interface localization, not translation of the installed Skyrim mods.

These initial translations were produced with AI assistance and need community review, especially game terminology. Completeness of the extracted catalog does not mean every dynamically assembled technical message has been localized.

## Where translation files live

- Source: `src/Translations/*.json`.
- A developer build: `Translations/` beside the managed application DLL.
- The enclosing release layout: `Runtime/Translations/`, beside the other runtime files.

The same catalogs are embedded into the app as a fallback. Editable files overlay the embedded catalogs at startup. An invalid/missing external file does not remove the embedded translation. A missing/empty entry falls back to the embedded entry, then English. Unknown source keys return the original English text. No translation file executes code or loads a DLL.

## Adding or improving a language

1. Make a copy of `en.json` in the same Translations directory; use a name such as `pt-BR.json`.
2. Set `language` to a valid .NET culture name such as `pt-BR`, and `name` to its native display name, such as `Português (Brasil)`.
3. Keep every key unchanged. Translate only its value. Use `en.json` to understand the source text of `xaml.*` keys. C# text entries use the English text/template as their key. The XAML identifiers are stable until their source English text changes.
4. Preserve every numbered placeholder, for example `{0}`, `{1}` or `{0:00}`, even if its position changes. Preserve necessary newlines (`\n`) and meaningful spaces around text fragments. Do not add new placeholders or translate item names/technical identifiers inside them.
5. Omit an untranslated key or leave its value empty; English/built-in fallback will apply. Do not invent a translation to satisfy a count.
6. Restart the app. The language is listed automatically using its `name`; no code change is required. Automatic selection uses the exact installed culture, its parent culture, then English.
7. Share the JSON file with the project maintainer or through a source contribution. Include your preferred public credit name separately; do not put personal contact details into UI strings.

Files are UTF-8 JSON, not XAML or scripts. Only top-level `.json` files in the known Translations directory are considered; up to 100 files and 4 MiB per file. Invalid JSON/cultures, excessive values and invalid/mismatched format placeholders are ignored. A malformed formatted translation also falls back safely at display time. Language codes are never used to construct arbitrary file paths. Community files should still be reviewed before installation because they can change the words/warnings shown to the user.

## Maintainer notes

- `Localization.cs`: local catalog loading, Windows UI culture, parent/English fallback, validated placeholders, culture-aware UI formatting and display-only choice templates.
- `Loc.T("English source")`: fixed UI text; `Loc.F($"Found {count} outfits")`: template plus separate data arguments.
- `MainWindow.Language.cs`: asynchronous language preference saving and in-process workspace refresh under the selected catalog, with the existing loading icon, busy-operation coordination, cached session transfer and rollback on failure.
- `TrExtension`: XAML text lookup. No visual-tree text replacement, so plugin names and bound user data are not guessed/transformed.
- `CurrentCulture` is deliberately untouched. Only `CurrentUICulture` changes; distribution serialization and identifiers keep their existing behavior.
- Shared NPC preset labels use display-only properties. Their rules, group keys, category values and matching remain invariant. Search considers translated labels as well as original labels.
- `AppPreferences.Language` is additive; patch inventory schema and generated-file identities are unchanged.
- Catalogs are both embedded and copied by the project file into build/publish output. The existing source packaging includes JSON files. The separate licensing/distribution review hold remains in place: this work does not authorize a public release or a Nexus upload.

## Verification status

On 26 October 2026 the Release build passed with zero warnings/errors, the full regression run passed 190 assertions (including 65 localization assertions), and the opt-in live WPF workflow passed 128 assertions. Every supported language produced byte-identical fixture SPID output and byte-identical fixture SkyPatcher output. Repeated immediate switching, preserved state, translated setup/review selections, busy work, recovery mode and locked settings were exercised. A Chinese Settings screen was visually inspected and a dark-heading contrast issue was fixed. See [verification details](RELEASE_VERIFICATION_0412.md). No game/MO2/Vortex compatibility test is claimed. These checks do not establish linguistic correctness for every translation or game compatibility for every mod.
