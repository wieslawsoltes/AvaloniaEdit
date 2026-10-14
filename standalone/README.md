# Scribe — independent Avalonia text editor

Scribe is an original text/code editing engine and a fully replaceable Avalonia editor control. **It does not depend on AvaloniaEdit, AvalonEdit, Monaco, Scintilla, TextMateSharp, or another text-editing control.** The editing viewport derives directly from `Avalonia.Controls.Control`; the outer editor derives from `TemplatedControl`. Avalonia supplies platform integration, text shaping, drawing and input primitives, not the document engine or editing behavior.

This directory is a **standalone solution**. It can be copied out of its hosting repository and built without any source, targets or project references outside this directory. The downloadable source contains this solution only, not the hosting repository's existing editor implementation. This replaces the previous ProText wrapper approach rather than renaming it.

## Packages and ownership

`Scribe.Core` targets .NET 8 and has **no NuGet dependencies**. It implements a persistent AVL text rope, immutable snapshots, mixed CR/LF/CRLF indexing, validated edit transactions, grouped undo/redo, saved revision tracking, multiple selections, grapheme navigation/deletion, line commands, anchored bookmarks, bounded search, linked snippet placeholders and language-provider contracts.

`Scribe.Avalonia` targets .NET 8 and references only `Scribe.Core` and the **Avalonia framework package 12.1.4**. It implements the original `EditorViewport`, `ScribeEditor`, `SearchBar`, `EditorStatusBar`, and sampled `EditorOverview`. The viewport owns drawing, tab expansion, selection/caret painting, shaped hit testing, wrapping, scrolling, pointer selection, rectangular selection and input-method integration.

The desktop and actual WebAssembly samples target .NET 10. The sample applications use Avalonia's Fluent theme and Inter font package; neither is required by the reusable core engine. No standalone font files are included in this source distribution.

## Run the samples

Install .NET SDK **10.0.402** (or a compatible later feature band), then run from this directory:

```sh
dotnet run --project samples/Scribe.Desktop

dotnet workload install wasm-tools
dotnet publish samples/Scribe.Browser -c Release -o artifacts/browser
python tools/serve.py artifacts/browser
```

Open the HTTP address printed by the server. A `file:` URL cannot load the WASM application. The browser host uses real Avalonia rendering and the same original editor engine as the desktop host; there is no HTML/JavaScript replacement editor. Do not set `WasmBuildNative=false`: Avalonia's native shaping/rendering assets must be linked.

For a previously published browser archive, extract it and run `python tools/serve.py /path/to/extracted/browser` from the source directory. The server locates the published `wwwroot` automatically.

## Add an editor

Reference the `Scribe.Avalonia` project or generated package, and include its theme after your application's Fluent theme:

```xml
<Application.Styles>
  <FluentTheme />
  <StyleInclude Source="avares://Scribe.Avalonia/Themes/Generic.axaml" />
</Application.Styles>
```

```xml
<scribe:ScribeEditor xmlns:scribe="using:Scribe.Avalonia"
                     Document="{Binding CurrentDocument}"
                     Language="C#"
                     TabSize="4"
                     ShowLineNumbers="True"
                     WordWrap="False" />
```

```csharp
using Scribe.Core;
using Scribe.Avalonia;

var document = new Document("// Your document\n");
var first = new ScribeEditor { Document = document, Language = "C#" };
var second = new ScribeEditor { Document = document, Language = "C#" };

// Independent selection/viewport state, one shared original buffer and undo history.
first.Session.Select(new Selection(0, 2));
first.Session.Insert("///");
```

A `Document` is single-writer-owned; use it on the UI thread when attached to a control. `TextSnapshot` is immutable and may safely be passed to background work. Do not copy the complete document into an MVVM string on each keystroke; bind `Document` instead.

## Replace the template

The **only required part** is a `ContentPresenter` named `PART_ViewportHost`. Scribe reattaches its existing original viewport there. Replacing `Theme` does not create another document, viewport or history stack.

```xml
<ControlTheme x:Key="MyScribeTheme" TargetType="scribe:ScribeEditor">
  <Setter Property="Template">
    <ControlTemplate>
      <Border Background="{TemplateBinding Background}" Padding="12">
        <ContentPresenter Name="PART_ViewportHost"
                          HorizontalContentAlignment="Stretch"
                          VerticalContentAlignment="Stretch" />
      </Border>
    </ControlTemplate>
  </Setter>
</ControlTheme>
```

Optional parts are `PART_SearchBar`, `PART_VerticalScrollBar`, `PART_HorizontalScrollBar`, `PART_CompletionPopup` and `PART_CompletionList`. A custom template that omits a part also omits that interface. The minimal gallery template intentionally has no completion popup or scrollbar chrome; keyboard/wheel navigation and search still operate. Add those parts when those interfaces are required.

The default template exposes header/footer content and independent status, overview, line-number and whitespace options. Theme resources are `ScribeBackground`, `ScribePanel`, `ScribeForeground`, `ScribeMuted`, `ScribeBorder`, `ScribeAccent`, `ScribeSelection` and `ScribeCurrentLine`. `SelectionBrush`, `CaretBrush`, `CurrentLineBrush` and `GutterBrush` are styled properties.

## Editing features

The original engine supports multiple carets and disjoint selections. **Alt+click** adds a caret; **Alt+Shift+drag** selects a rectangle; **Ctrl/Cmd+D** adds the next occurrence. Insertion, deletion, indentation and undo operate on the resulting selection set in one transaction. Touch/pointer focus and dragging use the same original hit-testing path.

Undo groups contiguous typing but separates navigation, saved states and unrelated operations. Shared-document undo restores the initiating editing session's selection state while other sessions remap their positions through the change.

Search supports literal/regex, case, Unicode-aware whole words, anchored selection scope, forward/backward wrap and regex capture replacement. Invalid patterns are reported. Search has result/time budgets; replace-all refuses truncated plans without modifying the document.

Linked snippets support `${1:default}`, `$1`, `${1}` and `$0`. For example:

```csharp
editor.InsertSnippet("${1:name} = $1; ${2:value}$0");
```

The two occurrences of placeholder 1 become a multi-selection and update together; Tab selects placeholder 2, then the final stop. This is not only insertion of static snippet text. Nested placeholders and transform expressions are not supported.

Additional commands cover word/line/document navigation, grapheme-safe deletion, CRLF-preserving edits, indentation, line comments, duplicate text/lines, trailing-whitespace cleanup, selected-text case conversion, bookmarks and automatic bracket pairs.

## Language integration

`ICompletionProvider` receives an immutable snapshot, offset and revision. `IDiagnosticProvider` receives a snapshot and revision. Results are capped and discarded when stale; asynchronous result publication returns to the UI thread. The control supplies its own completion list, keyboard acceptance and diagnostic underlines. Snippet completions can use the same linked-placeholder engine.

Basic highlighting and brace folding are implemented by Scribe's own lexical scanner. They are not a full C#/JavaScript/Python parser. The sample keyword completion and TODO diagnostics are illustrative; no compiler, language server, semantic refactoring or debugger is bundled. Connect a real language service when your application needs semantic intelligence.

## Validation and packaging

```sh
dotnet run --project tests/Scribe.Core.Tests -c Release
dotnet run --project tests/Scribe.UI.Tests -c Release
python -m pip install playwright==1.64.0 pillow==12.0.0
python -m playwright install chromium
python tools/browser_validate.py artifacts/browser

dotnet pack src/Scribe.Core -c Release -o artifacts/packages
dotnet pack src/Scribe.Avalonia -c Release -o artifacts/packages
python tools/verify_independence.py
python tools/verify_packages.py
```

Core tests include randomized persistent edits, tree invariants, newline indexing, immutable snapshots and transaction rejection. Native/headless tests check original base classes, dependency references, templates and editing. Browser tests operate the published WASM canvas with real keyboard/pointer input and Chromium IME composition. A sample-only bridge supplies fixture setup and state observation when `?test=1` is present; it is never part of either library package and is not intended as an application security boundary.

Results, screenshots and logs are written to `artifacts/validation`. Inspect the delivered report for exactly which build passed; writing a test is not evidence that it ran. The package verification builds an isolated consumer using a fresh NuGet cache and no project references. Package files are build outputs, not a claim that these IDs are published to nuget.org.

## Release status and engineering limits

This is **0.2.0-preview.1**, an independent implementation, not a claim of complete commercial-editor feature parity or universal production qualification. See [capabilities and limits](docs/capabilities.md) before integrating. In particular, it is a plain-text/code editor rather than a rich-text word processor, and long-line shaping and unmeasured wrapping have explicit bounds.

Dispose each `ScribeEditor` when it is no longer used so it detaches its editing session from the shared document. The host owns `Document` lifetime. The sample retains open documents in memory until application exit and provides user-initiated open/save-as, not a recovery journal. Saved snapshots carry revisions so an older save cannot mark newer edits as saved.

MIT license. See [third-party notices](THIRD-PARTY-NOTICES.md) for framework/tool dependencies.
