# Native Uno search and replace

This is the native search implementation in `UnoEdit.Uno`, not the retained
Avalonia `UnoEdit` baseline. It extends the repository migration described in
[uno-port.md](uno-port.md). The entire editor is still an API-incomplete preview.

## Native control and API

`TextEditor.SearchPanel` exposes the templated overlay. `SearchPanel.Install(editor)`
returns that same installation rather than adding duplicate handlers. `Open`,
`Close`, `Reactivate`, `FindNext`, `FindPrevious`, `ReplaceNext`, `ReplaceAll`, and
`Uninstall` operate on the actual Uno editor. The common original property names
are retained as native dependency properties: `SearchPattern`, `ReplacePattern`,
`MatchCase`, `WholeWords`, `UseRegex`, and `IsReplaceMode`. The original
`SearchOptionsChangedEventArgs` contract and source attribution are retained.

```csharp
var panel = editor.SearchPanel;
panel.SearchPattern = "(?<name>class)";
panel.UseRegex = true;
panel.MatchCase = true;
panel.ReplacePattern = "${name} /* declaration */";
panel.IsReplaceMode = true;
panel.Reactivate();
```

The native sample includes a **Find / Replace** button. **Ctrl+F**, **Ctrl+H**,
**F3**, **Shift+F3**, **Enter**, and **Escape** open, navigate, or close the panel.
The existing command-modifier path is also used on hosts exposing the platform
command modifier through Uno; this does not establish interactive macOS parity.

`Themes/Generic.xaml` supplies the default native `ControlTemplate`, with light,
dark and high-contrast brushes, named parts, focus cycling, tooltips and a live
status label. Applications can replace the template. Native text fields sync
bidirectionally through dependency-property callbacks, with their callbacks
removed when the template changes. First-open focus is retried through template
and load callbacks when the field does not yet exist. Search-field keyboard
input does not leak into document insertion, deletion or selection commands.

The primary template parts are `PART_searchTextBox`, `PART_replaceTextBox`,
`PART_findNext`, `PART_findPrevious`, `PART_replaceNext`, `PART_replaceAll`,
`PART_close`, `PART_matchCase`, `PART_wholeWords`, `PART_useRegex`,
`PART_replaceMode`, `PART_replaceRow`, and `PART_searchStatus`.

## Search model and replacement semantics

The framework-independent `SearchSession` reuses the original search engine and
caches immutable match coordinates per document version and query. Normal
replacement keeps dollar signs literal. Regular-expression replacement supports
numbered and named captures. Zero-width navigation advances between matches and
wraps, instead of repeatedly selecting one caret position.

Replace All expands and validates the complete batch against one document version,
then applies descending edits in one undo group. Inserted replacement text is not
searched again during that batch. A match intersecting a protected range is skipped
**as a whole**: search replacement never rewrites just the editable fragments of a
partially protected match. Status reports replaced and skipped counts. Whole-editor
read-only state blocks replacement and hides replace controls.

Provider callbacks that change the document or edit protection, malformed ranges,
and stale offsets caused by an `UpdateStarted` callback are rejected before the
planned replacement batch is applied. This protects the search operation's own
planned edits; it cannot roll back arbitrary application code mutating the document
from its callbacks. Programmatic `TextDocument` changes and undo retain their
existing semantics.

Closing the panel releases results, disables search work and detaches the marker
layer. Document replacement detaches the previous source. Uninstall/reinstall and
editor disposal remove subscriptions, timers and owned search resources.

## Performance and failure handling

Navigation uses binary lookup over cached results and never rescans on ordinary
caret movement. Query changes are debounced by 120 ms in the native panel. Invalid
patterns and regex timeouts clear stale results and show a status error.

The panel's `SearchSession` defaults to a **250 ms per-match regex timeout**.
The original four-argument `SearchStrategyFactory.Create` now has a finite
**two-second** match timeout; a new overload accepts an explicit finite timeout.
`MaximumResults` defaults to **100,000**. Truncation is explicit, and Replace All
refuses a truncated result set instead of silently replacing a prefix.

These are not total-query-time or document-size guarantees. Regex search still
materializes document text and runs synchronously. A large file, many expensive
matches, or a very long line still requires additional profiling and optimization.

`SearchResultMarkerSource` supplies only visible-line intersections using binary
lookup. Marker changes request a repaint without throwing away shaped text. Range
geometry splits across actual shaped runs for wrapping and bidirectional text,
maps tab/UTF-16 positions, and includes multiline delimiters and zero-width matches.
Paint does not execute a search. Tests enforce unchanged shape-creation counts and
visible-line marker bounds near line 95,000 of a 100,000-line document.

This marker interface is not the complete original background-renderer or
visual-line extension API. `SearchResultsBrush` currently accepts native
`SolidColorBrush` values, including opacity, or null. Other brush types explicitly
throw `NotSupportedException`; gradient/image-brush parity is not claimed.

## Executed validation

Implementation revision: `834d4d9045f182708b9dcf5d95d4d79e3f8ecb83`.

[Validation run](https://github.com/wieslawsoltes/AvaloniaEdit/actions/runs/37964065568)
completed the following before pushing the integrated source:

| Validation | Result |
| --- | --- |
| Original retained Avalonia regression suite | 515 passed; zero failures/skips |
| Framework-independent core | 313 passed; zero failures/skips, including 24 new search cases |
| Native shaped renderer | 26 passed; zero failures/skips, including nine new marker cases |
| Python build/package tests | 13 passed |
| Real Uno Linux application under Xvfb | 23 named groups passed, including five new native search groups |
| Published WebAssembly application | All 14 actual browser input scenarios passed |
| Native packages and symbols | All three package pairs produced and validated |
| Independent package consumer, without project references | Desktop and browser targets built successfully |

Browser search scenarios cover first-open Ctrl+H focus without pointer assistance,
case/whole-word options, next/previous navigation, literal dollar replacement,
regex captures, invalid-pattern recovery, Escape/focus restoration, grouped undo,
and zero-width navigation. Tests read native template bounds and diagnostics;
all document and field mutations use real browser keyboard/pointer input.

The existing browser input, Unicode clipboard/grapheme editing, 100,000-line
navigation and narrow-window scenarios remain in the same test run. Screenshots,
trace, TRX files, package validation, checksums and the exact source revision are
retained in the run artifact. Linux functional checks are not substitutes for
interactive Windows/macOS IME or accessibility testing.

The one-time integration workflow and patch were removed after their source edits
were committed and validated. Normal native CI, Pages and release checks invoke
`tools/browser-smoke.mjs`, which includes `tools/browser-search-checks.mjs`.

## Remaining compatibility boundary

The search overlay's original routed-command registration integration and all
legacy template/style details are not fully ported. Complete editor text automation
and IME/composition remain separate work; native search TextBox accessibility and
an exposed status label do not establish full editor accessibility parity.

The overall editor still needs TextMate integration, completion/overload windows,
snippets, folding adornments, complete selection/command extensibility, original
rendering-extension APIs, drag/drop, broader platform/lifecycle verification and
the remaining original sample UI. No stable release or full API parity is claimed.
