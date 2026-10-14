# ProText — Avalonia text editing controls

A reusable plain-text and code-editing control library with a shared desktop/WebAssembly gallery. ProText adds replaceable editor chrome, document sessions, safe editing services and a diff viewer to AvaloniaEdit. This is version **0.1.0**, not a claim of complete Visual Studio, VS Code, or word-processor parity.

## Projects

| Project | Purpose |
|---|---|
| `ProText.Core` | UI-independent ranges, atomic edit plans, line operations, bounded search, folding, completion/diagnostic contracts, bounded line diff |
| `ProText.Avalonia` | `ProTextEditor`, `EditorSearchBar`, `EditorStatusBar`, `TextDiffEditor`, document overview and session integration |
| `ProText.Sample` | Shared Avalonia gallery, sample language providers, user-initiated file open/save |
| `ProText.Desktop` | Desktop application entry point |
| `ProText.Browser` | Actual Avalonia WebAssembly application, not a JavaScript editor substitute |
| `ProText.Tests` | Core/property-style tests and native Avalonia headless tests |

Libraries target .NET 8. Samples and the browser host target .NET 10. Package versions are pinned to Avalonia 12.1.4 and Avalonia.AvaloniaEdit 12.0.0.

## Run

Install the .NET 10 SDK. Run commands from this directory.

```sh
dotnet run --project samples/ProText.Desktop

dotnet workload install wasm-tools
dotnet publish samples/ProText.Browser -c Release -o artifacts/browser
python tools/serve.py artifacts/browser
```

Open the address printed by the server. The browser application requires HTTP(S); opening `index.html` with a `file:` URL is not supported. The first build links the WebAssembly native rendering dependencies. `RunAOTCompilation` is false; do not force `WasmBuildNative=false`.

## Use the control

Reference `ProText.Avalonia` and add the theme after your application's Fluent theme:

```xml
<Application.Styles>
  <FluentTheme />
  <StyleInclude Source="avares://ProText.Avalonia/Themes/Generic.axaml" />
</Application.Styles>
```

```xml
<pt:ProTextEditor xmlns:pt="using:ProText.Avalonia.Controls"
                  Session="{Binding CurrentDocument}"
                  ShowLineNumbers="True"
                  ShowOverview="True"
                  TabSize="4"
                  WordWrap="False" />
```

```csharp
var session = new EditorSession("// Start here\n", "Example.cs", "C#");
var left = new ProTextEditor { Session = session };
var right = new ProTextEditor { Session = session };
// Same text and undo history; independent caret, selection and viewport.
```

Use `Session` for MVVM rather than two-way copying the entire text on every keystroke. `EditorSession` exposes `Document`, `Revision`, `IsDirty`, `IsReadOnly`, bookmarks and diagnostics. The native `Editor` property is an escape hatch for AvaloniaEdit integrations, including custom margins, line transformers, completion or highlighting adapters.

## Replace the template

The outer editor has a stable editing surface. The required template part is a `ContentPresenter` called `PART_EditorHost`. Optional chrome includes `PART_SearchBar` and `PART_GoToBox`. Templates that omit an optional part intentionally omit that UI; for example, the minimal gallery theme has no go-to textbox.

```xml
<ControlTheme x:Key="MyEditorTheme" TargetType="pt:ProTextEditor">
  <Setter Property="Template">
    <ControlTemplate>
      <Border Background="{TemplateBinding Background}" Padding="12">
        <ContentPresenter Name="PART_EditorHost"
                          HorizontalContentAlignment="Stretch"
                          VerticalContentAlignment="Stretch" />
      </Border>
    </ControlTemplate>
  </Setter>
</ControlTheme>
```

Apply the theme through the standard `Theme` property. `EditorTheme` separately controls the native editing surface's theme. Header/footer content, panel visibility, brushes, typography and editor options remain independently configurable. Light/dark resources use `ProTextBackground`, `ProTextPanel`, `ProTextBorder`, `ProTextForeground`, `ProTextMuted` and `ProTextAccent`.

## Features and boundaries

See [capabilities](docs/capabilities.md) for implemented features and explicit limitations. Highlights include grouped undo/redo, shared sessions, bounded regex search/replace, anchored search scope and bookmarks, folding, provider-based completion and diagnostics, line commands, EOL conversion, a document overview and side-by-side diff navigation.

The sample completion list and TODO diagnostics are deliberately illustrative providers. They are not a C# compiler or a language server. The library does not silently advertise compiler intelligence where none is installed.

## Validation

```sh
dotnet run --project tests/ProText.Tests -c Release
python -m pip install playwright pillow
python -m playwright install chromium
python tools/browser_validate.py artifacts/browser
```

The browser suite loads the published WASM application and uses both real pointer/keyboard input and a sample-only test bridge to observe document state. The bridge is disabled unless the URL contains `?test=1`; neither reusable package contains it. Tests write JSON results, logs and screenshots to `artifacts/validation`. The CI workflow also builds the desktop sample, publishes the browser application and packs both reusable libraries.

The 100,000-line scenario records construction/assignment time separately from browser readiness and checks the count of realized visual lines. Those measurements are machine-specific, not universal latency or frame-rate guarantees. Chromium is the automated browser target; macOS/Windows desktop input, Safari/Firefox, screen readers, mobile keyboards and complex IMEs need additional release qualification.

## Threading, ownership and safety

All Avalonia controls and `EditorSession` instances are UI-thread-owned. Providers may perform asynchronous work, but callers must return through the UI synchronization context before mutating a document. Completion requests are cancelled or discarded when the session, revision or caret changes. Diagnostic results carry a revision and are rejected when stale.

`ApplyEdits` preflights the complete edit batch before applying it in one document update. Overlaps, duplicate insertion boundaries, invalid ranges, read-only edits and stale revisions are rejected. Direct mutation of `Session.Document` is an advanced escape hatch and can bypass these guards; application integrations must enforce their own policy when using it.

Dispose controls no longer used, then dispose sessions after every view has detached. The sample keeps open documents in memory until the application closes. Saving captures a revision; completing an older save does not mark newer changes as saved. The file picker is user-initiated, and the sample limits opened text to 20 million characters. Save-as writes UTF-8 without a BOM.

## Packaging and license

```sh
dotnet pack src/ProText.Core -c Release -o artifacts/packages
dotnet pack src/ProText.Avalonia -c Release -o artifacts/packages
```

ProText is MIT-licensed. Dependency attribution is in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Generated package artifacts are local build products, not a claim that these package IDs have been published to nuget.org.
