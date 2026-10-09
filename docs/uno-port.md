# UnoEdit migration

This branch is an **in-progress native Uno Platform port**, not a complete
public-API-compatible replacement for AvaloniaEdit. Original implementations and
tests remain available as a regression baseline. Renaming a namespace or passing
those baseline tests does not establish native Uno UI parity.

## Toolchain

`global.json` pins .NET SDK 10.0.401 and Uno.Sdk 6.7.30. The SDK version was checked
against the stable Uno template release on 2026-10-09. Native rendering packages
are centrally pinned and isolated from the Avalonia baseline's graphics versions.
NuGet auditing and compiler warnings-as-errors remain enabled.

Install the .NET SDK selected by `global.json`, Python 3.10 or newer, and the
`wasm-tools` workload for browser builds. Node.js is required for browser
verification, not for the editor itself.

## Source layout

| Project | Purpose and current boundary |
| --- | --- |
| `UnoEdit.Core` | Original document, rope, undo, line/height indexing, search, indentation and read-only-section providers; framework-neutral native input state. |
| `UnoEdit.Rendering.Skia` | Shaped text, UTF-16/scalar/tab mapping, styled runs, hit testing, selection painting and an indexed viewport with a 256-line LRU layout cache. |
| `UnoEdit.Uno` | Native Uno `TextEditor`, `TextArea`, `TextView` and caret controls; original XSHD highlighting adapted to native types; still an incomplete subset of the original UI and extension APIs. |
| `UnoEdit.Uno.Demo` | Desktop/browser preview, original sample text resources, large-file exercise and opt-in native-control/browser checks. |
| `UnoEdit`, `UnoEdit.TextMate`, `UnoEdit.Demo` | Renamed Avalonia implementations retained for regression comparison. These entire projects have **not** become native Uno implementations. |
| `UnoEdit.Tests` | Original regression suite against the retained baseline, including extracted-core type forwards. |
| `UnoEdit.Core.Tests` | Original framework-neutral regressions plus ownership, input, protected sections and height-index checks. |
| `UnoEdit.Rendering.Skia.Tests` | Executable native shaping, styling, hit-test, wrapping and cache-bound tests. |

Namespaces use `UnoEdit`. Extracted public core types are forwarded from the
baseline assembly to preserve their identity for its consumers. Native framework
types necessarily differ; the final Uno API mapping is not complete. Do not
reference both UI implementations in one application: they contain overlapping
editor type names.

## Native highlighting

`TextEditor.SyntaxHighlighting` accepts the original `IHighlightingDefinition`
contract. `HighlightingManager.Instance` supplies the original embedded language
definitions and extension mapping. Each editor/document owns its own incremental
`DocumentHighlighter` state; document rebinding and disposal detach the previous
state.

The original XSHD loaders, lexical-state tracking and rich-text/HTML paths are
retained and adapted to native Uno colors, brushes and font properties. Both
current and legacy XSHD formats have native runtime checks. Styled rendering uses
the same shaped runs for painting, selection and caret geometry, including font
sizes, weights, backgrounds and decorations.

This is **not** a claim that TextMate integration or every original visual-line
transformer/generator API has been ported. `UnoEdit.TextMate` still uses the
retained Avalonia UI baseline.

## Native protected document sections

`TextArea.ReadOnlySectionProvider` uses the original
`IReadOnlySectionProvider` and `TextSegmentReadOnlySectionProvider<T>` APIs, now in
`UnoEdit.Core`. The original provider implementation and its connected segment
tracking are preserved; the baseline assembly forwards their public types.

Native insertion consults `CanInsert`. Selection replacement follows the original
provider contract: replace the **last** deletable interval, delete earlier editable
intervals, and preserve protected text between them. No editable interval means
no replacement. Read-only providers must return ordered, nonoverlapping intervals
inside the requested range; the native session validates and snapshots those
intervals before applying edits. A provider that changes the document while an
edit is being planned is rejected rather than applying stale offsets.

Typing, Enter, Tab, deletion, selected-text replacement and clipboard paste share
the protected-edit path. Block indentation skips disallowed insertion points;
unindent removes only editable whitespace. Multi-interval edits form one undo
group and one session notification. Rejected Backspace/Delete does not move the
caret or create a selection. The native `ClearSelection`,
`ReplaceSelectionWithText` and `RemoveSelectedText` entry points are provided.

Whole-editor `IsReadOnly` is an additional restriction; toggling it does not
replace a custom section provider. As in the original editor, this is an **input
policy**, not a security boundary for `TextDocument`: explicit programmatic
changes and undo retain their document semantics. Full rectangular selection and
command-extension parity are separate remaining work.

## Build and test

Run from the repository root:

```sh
python3 -m unittest discover -s test -p test_build_tools.py -v
python3 tools/build.py test
python3 tools/build.py desktop --install-workloads
python3 tools/build.py browser --base-path /AvaloniaEdit/
python3 tools/build.py pack --version 0.1.0-alpha.1
```

On Windows, use `python` instead of `python3` where appropriate. `test` runs all
three .NET test projects and rejects missing TRX results, zero executed tests and
failed runs. It records passed/failed/not-executed counts. `core`, `rendering` and
`baseline` run individual suites. `all` runs tests, builds, browser publishing
and package validation.

Browser output is `artifacts/browser`; the normalized static site is
`artifacts/site`. `build.json` identifies the exact source revision and marks it
as an API-incomplete preview. Serve the site over HTTP(S), not a `file://` URL.

### Native runtime and browser checks

On Linux with Xvfb installed, after building desktop:

```sh
UNOEDIT_NATIVE_CHECKS=1 xvfb-run -a timeout 90s dotnet run \
  --project src/UnoEdit.Uno.Demo/UnoEdit.Uno.Demo.csproj \
  -c Release -f net10.0-desktop --no-build
```

This starts the real Uno application and executes control/highlighting/protected
editing assertions on its UI thread. The process exits with a failure code when
an assertion fails. It is not a substitute for interactive testing across all
desktop operating systems, input methods and accessibility tools.

For the browser:

```sh
npm install --prefix tools --no-save --package-lock=false playwright@1.64.0
tools/node_modules/.bin/playwright install --with-deps chromium
node tools/browser-smoke.mjs artifacts/site AvaloniaEdit
```

The smoke test serves published bytes under the repository subpath. It exercises
actual keyboard/pointer input, selection replacement, undo/redo, Unicode/tab
clipboard input, grapheme deletion, 100,000-line navigation and resizing. The
opt-in `?smoke=1` app also executes native property/document/stream/disposal,
highlighting and protected-edit checks. Its JavaScript diagnostic state is
read-only; tests do not mutate the document through a JavaScript back door.
Failures retain screenshots, console output, state and a Playwright trace in
`artifacts/browser-tests`.

### NuGet package validation

All three native packages share one prerelease version. `pack` cleans its output
directory, creates NuGet and symbol packages, verifies their identities, README,
compiled libraries and native dependency versions, and rejects direct Avalonia
baseline dependencies.

It then restores an independent consumer **outside the repository** using only
the newly packed `UnoEdit.Uno` package. There are no project references or inherited
repository package pins. NuGet source mapping restricts `UnoEdit.*` to the local
artifact feed, and an isolated package cache prevents an older package with the
same prerelease version from producing a false-positive build. Both desktop and
browser consumer targets must compile before `validation.json` and SHA-256
checksums are written. `verify-packages` rechecks an existing output set.

These checks verify package consumption, not universal public API parity. Adding
this validation code is not itself evidence of a passing run; use the actual CI
result at the source revision being evaluated.

## CI and releases

- `UnoEdit CI` validates the retained editor regression baseline.
- `Uno native backend` validates core tests on Windows, Linux and macOS, native
  rendering, native Linux control checks, browser input and package consumption.
- `UnoEdit GitHub Pages` runs all three suites, publishes and tests the exact
  static site before deployment. After deployment it verifies the public source
  revision, rather than inferring success from artifact upload.
- `UnoEdit prerelease` accepts tags such as `unoedit-v0.1.0-alpha.1`. It validates
  suites and browser behavior before attaching source, browser, NuGet and symbols
  with checksums to a GitHub prerelease. Stable tags are rejected while the port
  is incomplete. Optional NuGet publication uses the configured repository
  `NUGET_API_KEY`; otherwise packages remain release assets.

Source-migration workflows preserve the original implementations, validate their
resulting source commit, and push only after validation. They use a normal
fast-forward push and never overwrite concurrent work. No stable release or
end-to-end public package publication is asserted by this document.

## Branch and GitHub Pages configuration

The active development and publication branch is `port/unoedit`. The Pages
workflow explicitly targets that branch; it does not depend on merging the
incomplete port into `master` and never deploys fork pull-request artifacts.

On 2026-10-09 the repository's Pages site was confirmed enabled with the
**GitHub Actions** build source. Two administrative settings remained unresolved:

1. The request to change the default branch from `master` to `port/unoedit` was
   rejected with HTTP 403 (`Resource not accessible by integration`). A repository
   administrator can set **Settings > General > Default branch > port/unoedit**.
2. The `github-pages` environment rejected deployment because `port/unoedit` was
   not an allowed branch. Add that specific branch under **Settings > Environments
   > github-pages > Deployment branches and tags**, retaining other protections.

These policies are not disabled or bypassed by the workflows. Enabling Pages
alone does not change the environment's branch allowlist. Once it permits the
branch, rerun the failed deployment while its artifact is retained or run a fresh
Pages build. Until a deployment and public revision check pass, a successful build
or artifact upload must not be described as a published site.

The current repository name requires `/AvaloniaEdit/` as the browser base path.
After a repository rename, workflows derive its new name automatically. For
local/custom-domain hosting choose the appropriate `--base-path`, normally `/`.

## Performance model

The viewport uses the original augmented height tree to find visible document
lines without scanning preceding lines. It shapes only lines needed for painting
or caret/hit testing, reuses cached layouts, and bounds retained layouts to 256
lines. Tests exercise viewport/cache bounds on 100,000-line documents. These are
algorithmic checks, not universal FPS or typing-latency guarantees.

Initial document/index construction scales with document size. Shaping and
Unicode mapping scale with individual line length; the cache is bounded by line
count, not byte size. Unseen wrapped-line measurements are estimates refined on
visits. Very long lines, stale off-screen measurements, bidi/visual-column
navigation, touch behavior and allocation/latency profiling remain important.

## Remaining compatibility work

The native control is not a drop-in replacement yet. Remaining work includes
TextMate integration, completion/overload windows, full selection/command
extensibility, snippets, folding adornments, original visual-line
generators/transformers/background renderers, search-panel API parity,
templates/styles, drag/drop, complete IME/composition and accessibility. Broader
native lifecycle and interactive Windows/macOS/Linux validation remain.

The preview uses original sample text resources but does not yet reproduce every
original sample UI feature. Preserve this distinction in test reports, package
descriptions and release notes.

## Attribution

Original copyright/license notices and upstream links are retained. SkiaSharp,
HarfBuzzSharp and RichTextKit are NuGet dependencies under their respective
licenses. This project is not affiliated with the separate LeXtudio UnoEdit
implementation.
