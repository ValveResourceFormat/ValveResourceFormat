# GUI translations

Translate the Source 2 Viewer GUI. The GUI will later be rewritten in another C# UI framework (PanGui, immediate mode), so the string files, keys, generated language classes must survive that rewrite unchanged, and formatting must not allocate per frame.

## Scope

- Translate: forms, menus, context menus, dialogs (`MessageBox`, `TaskDialog`), file dialog titles and filters, settings, the welcome screen, renderer sidebar labels added from the GUI, enum values shown in combo boxes, through a typed `switch` from enum value to `Locale` member.
- Keys are only accessed through typed `Locale` members, never by string name.
- Do not translate: `ValveResourceFormat/` and `Renderer/` code, exceptions, log output, decompiler and CLI output, file format and entity key names.
- Automation (`--mcp`) always runs in English; MCP tools find tabs and sidebar controls by `.Text`.
- Texts shown before `Settings.Load()` (uninstall errors in `Program.cs`) stay English.

## Files

```
GUI/Localization/en.csv        source of truth, AdditionalFiles
GUI/Localization/<culture>.csv translations, AdditionalFiles
Misc/SourceGenerators/StringsGenerator.cs
GUI/Utils/Translations.cs
```

- One headerless CSV per language, two columns `key,text`, UTF-8.
- Quoted fields with `""` escapes as translation platforms export them; a message with a newline is a quoted field spanning several lines.
- An empty translation counts as missing.
- File names are culture tags (`ru`, `pt-BR`, `zh-Hans`).
- Keys are semantic (`About_CheckForUpdates`), never WinForms control names.
- Messages are whole sentences; never compose a message from translated fragments.
- `&` marks a mnemonic, at most one per message; consumers without mnemonics strip it with one helper.

## Message syntax

ICU MessageFormat 1 subset: plain arguments and `plural`, used only for counts such as "Exported 2 files".

```
Update_Installed,{buildName} (installed)
Update_Progress,{written} MB of {total} MB
Search_Results,"{count, plural, one {# result} other {# results}}"
```

- Every message is valid ICU MF1, so platform ICU checks apply.
- Supported: text, `{name}`, and `{name, plural, ...}` with CLDR categories (`other` required), exact `=N` keys, `#` for the count and `{name}` arguments inside branches.
- Branch lookup: exact `=N` key, then the CLDR category, then `other`.
- Plural arguments are integers; a fractional count is passed as a formatted string and never selects a branch.
- Apostrophes follow ICU: `''` is a literal `'`, `'{'` quotes a brace, other apostrophes are literal.
- Numbers that need a format (`0.0`, `N0`) are formatted by the caller with `CultureInfo.InvariantCulture` and passed as strings.
- Rejected as a build error: `select`, `selectordinal`, `number`, `date`, `time`, `offset:`, nested plurals and positional `{0}` arguments.

## Generator

- Incremental generator in `Misc/SourceGenerators`, referenced by `GUI.csproj` as an analyzer.
- Reads every `Localization/*.csv` through `AdditionalTextsProvider`, never the `Compilation`; emits nothing when `en.csv` is absent.
- The CSV reader and the message parser live only in the generator; the GUI has no runtime parser.
- English compiles to a class with a virtual member per key; each translation is a class deriving from it that overrides only the keys it translates, so a missing translation falls back to English.
  - key without arguments: `public virtual string About_Website => "&Website";`
  - key with arguments: a `string` method using `string.Create(CultureInfo.InvariantCulture, $"...")` and a `bool X(Span<char> destination, out int charsWritten, ...)` overload using `destination.TryWrite`; `int` for `plural` arguments, `string` otherwise, `ReadOnlySpan<char>` in the span overload.
  - a plural block is a `switch` on the language's plural rule function, each branch writing the whole message.
- Emits the `switch` from culture name to language class into `Translations`.
- `Translations.Locale` returns the selected language object; a global `using static` makes call sites read `Locale.Menu_File`.
- Build errors for duplicate keys, keys missing from `en.csv`, malformed CSV, malformed messages, arguments that differ from English, and plurals that lack or add a category of the language.

## Runtime

- `Translations.Select(culture)` swaps the language object behind `Locale`. Swapping is safe at any time, so an immediate-mode UI switches live.
- No runtime data or parsing: texts are literals, and formatting is compiler interpolation into the span or string with no boxing.
- PanGui draws `ReadOnlySpan<char>` through `TextContent`, which copies into its frame allocator, and `View<char>`/`StringView` through `DrawText`; per-frame text uses `TryFormat` into a `stackalloc` buffer.
- WinForms uses the `string` methods; no cache of formatted strings.
- Plural rules are hand-written in `GUI/Localization/PluralRules.cs`, copied from CLDR for each shipped language; the generator links the same file to validate plural categories.

## Language selection

- `Settings.Config.Language`: culture name, empty means system default.
- System default is `CultureInfo.CurrentUICulture` read in `Program.cs` before it sets the invariant UI culture.
- Resolve by walking `CultureInfo.Parent` until a file exists, else English.
- Load translations between `Settings.Load()` and `InitializeComponent()` in `MainForm`.
- `SettingsControl` gets a dropdown: "System default", then each available language by `CultureInfo.NativeName`. In WinForms a change asks to restart.

## Forms

- Keep the English `.Text` lines in `*.Designer.cs`; constructors overwrite them from `Locale` after `InitializeComponent()`.
- Static string arrays (`SettingsControl`, `SearchForm`, `TreeViewWithSearchResults`, `GLTextureViewer`) become properties or are built in constructors.
- Turn fixed-size buttons and checkboxes into auto-sized layouts while migrating each form. RTL is out of scope.

## Migration order

1. Remove `.Text` comparisons: settings tab by control type, `MainTabs` tab names.
2. Generator, `Translations`, setting and language dropdown.
3. About form and updater texts.
4. MainForm menus and context menus.
5. Dialogs, welcome screen, settings, search, extract and prompt forms.
6. Renderer sidebar labels and enum display names.

## Translator workflow

- Add a string: one row in `en.csv`, use `Locale.X`.
- The build validates every language; a translation is checked by building the GUI.
