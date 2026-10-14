# Validation evidence

The release workflow builds and tests the original core, controls, desktop host and actual Avalonia WebAssembly application from one commit. The downloaded validation archive includes that commit in `commit.txt` and `file-manifest.json`; reports must be read against that identity.

`core.json` records randomized rope editing, newline indexes, transaction preflight, undo and linked-selection semantics. `ui.json` records native headless control behavior, direct framework base classes, packaged-template contracts, and original routed-input behavior.

`browser.json` records the primary Chromium suite: actual pointer/keyboard text input, multiple carets, rectangular selection, linked snippets, IME preedit/commit, search, folding, completion, clipboard, shared views, template preservation, wrapping, idle rendering and 100,000-line editing. `browser-deep.json` records additional completion pointer-focus and delayed-provider result regressions. The latter deliberately uses providers that ignore cancellation, so the control's own stale-result guards are tested. Fixture/state bridge code lives only in the sample browser host and is not included in the reusable packages.

Run the second browser suite against a published build with:

```sh
python tools/browser_deep_validate.py artifacts/browser
```

`independence.json` audits direct and transitive project/package references. `package-consumer.json` records a NuGet-only consumer built outside the repository with a fresh package cache. `standalone-source-consumer.log` records the source copied outside the hosting repository and built/tested there. These separate checks establish that the hosting repository's existing editor source is not required.

Desktop compilation does not constitute native desktop interaction certification. Chromium composition tests do not establish all platform IME, candidate-window, screen-reader or mobile behavior. The long-line display limit and lazy wrapping estimates are documented in `capabilities.md`. Performance results are observations from the recorded CI host, not cross-device latency guarantees.
