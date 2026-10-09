# UnoEdit migration

This branch is an **in-progress port**, not a complete public-API-compatible
replacement for AvaloniaEdit. The original editor implementations and tests are
retained as a regression baseline. Renaming a namespace or passing those
baseline tests is not evidence that every original UI feature works in Uno.

## Toolchain

`global.json` pins .NET SDK 10.0.401 and Uno.Sdk 6.7.30. The SDK version was checked
against the stable Uno template release on 2026-10-09. Native rendering packages
are centrally pinned and isolated from the Avalonia baseline's graphics versions;
NuGet auditing and warnings-as-errors remain enabled.

Install the .NET SDK selected by `global.json`, Python 3.10 or newer, and the
`wasm-tools` workload for browser builds. Node.js is only required for browser
verification, not for the editor itself.

## Source layout

| Project | Purpose and current boundary |
| --- | --- |
| `UnoEdit.Core` | Original document, rope, line/height indexing, undo, search and indentation implementations; framework-neutral native input state. |
| `UnoEdit.Rendering.Skia` | Shaped text, UTF-16/scalar/tab mapping, hit testing, selection painting and indexed viewport with a 256-line LRU cache. |
| `UnoEdit.Uno` | Real Uno `TextEditor`, `TextArea`, `TextView` and caret controls; an incomplete subset of the original editor's UI and extension APIs. |
| `UnoEdit.Uno.Demo` | Native desktop/browser preview, original sample text resources, large-file exercise and opt-in browser diagnostics. |
| `UnoEdit`, `UnoEdit.TextMate`, `UnoEdit.Demo` | Renamed Avalonia implementation retained for regression comparison; these projects have **not** become native Uno implementations. |
| `UnoEdit.Tests` | Original regression suite against the retained baseline, including extracted-core type forwards. |
| `UnoEdit.Core.Tests` | Original framework-neutral regressions plus ownership, native input and height-index tests. |
| `UnoEdit.Rendering.Skia.Tests` | Executable native shaping, hit-test, wrapping and cache-bound tests. |

Editor namespace prefixes were renamed to `UnoEdit`. Extracted public core types
are forwarded from the baseline UI assembly to preserve their identity for its
consumers. Native framework types necessarily differ; the final Uno API mapping
is not complete. Do not reference both UI implementations in one application:
they currently intentionally contain overlapping editor type names.

## Build and test

Run these commands from the repository root:

```sh
python3 tools/build.py test
python3 tools/build.py desktop --install-workloads
python3 tools/build.py browser --base-path /AvaloniaEdit/
python3 tools/build.py pack --version 0.1.0-alpha.1
```

On Windows, use `python` instead of `python3` where appropriate. The `test` target
runs all three test projects and rejects missing TRX results, zero executed tests,
and failures. It records passed/failed/not-executed counts instead of treating a
successful build as a successful test run. `core`, `rendering`, and `baseline`
run individual suites. `all` runs tests, builds, publishing and package creation.

Browser output is placed in `artifacts/browser`; the normalized static site is
`artifacts/site`. `build.json` identifies the source revision and explicitly marks
the deployment as an API-incomplete preview. Packages and SHA-256 checksums are
written to `artifacts/packages`.

To verify the browser application:

```sh
npm install --prefix tools --no-save --package-lock=false playwright@1.64.0
tools/node_modules/.bin/playwright install --with-deps chromium
node tools/browser-smoke.mjs artifacts/browser AvaloniaEdit
```

The test hosts the published bytes under the repository subpath, not at `/`.
It exercises real browser keyboard/pointer events, selection replacement,
undo/redo, clipboard Unicode/tab input, grapheme deletion, large-document
navigation and resizing. The opt-in `?smoke=1` application exposes read-only
state; JavaScript does not mutate the C# document to simulate successful input.
It also runs seven native-control checks on the real Uno UI thread, covering
property binding, null documents, stream ownership and `IDisposable` dispatch.
Failures retain screenshots, console output, diagnostic state and a Playwright
trace in `artifacts/browser-tests`.

## CI and releases

- `UnoEdit CI` validates the retained editor regression baseline.
- `Uno native backend` runs core tests on Windows, Linux and macOS; native
  renderer tests; desktop compilation; browser publishing and input smoke tests.
- `UnoEdit GitHub Pages` runs all three suites, publishes the browser and tests
  that exact output before allowing deployment.
- `UnoEdit prerelease` accepts tags such as `unoedit-v0.1.0-alpha.1`. It validates
  all suites and browser behavior before attaching source, browser, NuGet and
  symbol packages with checksums to a GitHub prerelease. It rejects stable tags
  while the port is incomplete. Optional NuGet publication uses the repository's
  `NUGET_API_KEY`; without it, packages remain downloadable release assets.

Adding a workflow does not prove it passed or deployed. Consult the actual run
and source revision when evaluating a build. No stable release is asserted by
this document.

## GitHub Pages

The Pages workflow supports `master`, `port/unoedit` and manual dispatch. It never
deploys pull-request artifacts from forks or bypasses failed input tests. The
repository needs **Settings > Pages > Build and deployment > Source: GitHub
Actions**, and the `github-pages` environment must allow the selected branch.
The default `GITHUB_TOKEN` cannot grant itself the administrative permission to
enable a disabled Pages site. The deployment step reports that configuration
failure rather than claiming publication succeeded.

For the current repository name, use `/AvaloniaEdit/` as the build base path.
After a repository rename, workflows derive the new name automatically. For
local/custom-domain builds use the appropriate `--base-path`, normally `/`.

## Performance model

The viewport uses the original augmented height tree to find visible document
lines without scanning preceding lines. It shapes only lines needed for painting
or caret/hit testing, reuses cached layouts, and limits retained layouts to 256
lines. Rendering tests enforce cache/visible-line bounds on 100,000-line documents.
These are algorithmic checks, not universal FPS or typing-latency guarantees.

Initial document/index construction still scales with document size. Shaping and
Unicode mapping scale with individual line length; the cache cap is by line count,
not byte size. Width/height estimates for unseen wrapped lines are refined when
visited. Very long lines, stale off-screen measurements, bidi/visual-column
navigation, touch behavior and allocation/latency benchmarks require additional
work and validation.

## Remaining compatibility work

The native control is not yet a drop-in replacement. Work still includes the
original highlighting/TextMate APIs and integration, completion/overload windows,
original selection/read-only-section and command extensibility, full snippets,
folding adornments, visual-line generators/transformers/background renderers,
search-panel API parity, templates/styles, drag/drop, full IME/composition and
accessibility automation. Native control lifecycle must be validated across
unload/reload, retained documents, focus changes and actual desktop hosts.

The demo exposes a useful subset of the original sample experience; loading the
original text resources is not equivalent to porting every original sample UI
feature. Keep this distinction when reviewing test results and package names.

## Attribution

Original source copyright/license notices and upstream links are retained.
SkiaSharp, HarfBuzzSharp and RichTextKit are consumed through their NuGet packages
under their respective licenses. This project is not affiliated with the separate
LeXtudio UnoEdit implementation.
