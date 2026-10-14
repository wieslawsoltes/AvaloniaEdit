# Capability matrix — ProText 0.1.0

## Implemented in this package

| Area | Implementation |
|---|---|
| Controls | Templated editor, search bar, status bar and side-by-side diff. Stable native editing surface retained across outer template replacement. Styled document-overview drawing primitive. |
| Styling | Light/dark resources, custom ControlTheme, separate native EditorTheme, header/footer slots, independent typography and visibility options. |
| Document model | AvaloniaEdit TextDocument; shared sessions; revision tracking; save points; grouped undo/redo; read-only view and session modes. |
| Editing | Native selection, keyboard navigation, clipboard commands, line numbering and virtualization. Additional duplicate/delete lines, indent/unindent, toggle comments, trim trailing whitespace, case conversion, sort selected lines and newline normalization. |
| Search | Literal/regex, case, Unicode-aware whole word, forward/backward wrapping navigation, replacement groups, anchored selection scope, match highlights. Match and regex/time budgets; replace-all fails rather than applying a truncated plan. |
| Navigation | Line/column jump, anchored bookmarks, bounded document overview with click navigation. |
| Language features | Simple theme-aware lexical highlighting, lexical brace/comment folding, cancelable completion-provider interface, revision-checked diagnostic-provider interface and diagnostic underlines. |
| Completion | Completion popup, prefix replacement, priority sorting, insertion of multiline snippet text. |
| Diff | Side-by-side read-only line comparison, exact LCS within a cell budget, coarse fallback beyond the budget, change-group navigation. |
| Large documents | Native visible-line virtualization; bounded overview samples; optional folding/diagnostic work paused above MaxAnalysisLength. |
| Samples | One shared gallery hosted on desktop and real Avalonia WebAssembly. Multiple in-memory example documents, shared split view, file pickers, UTF-8 save-as, template/theme controls, large-document and diff demonstrations. |

## Engine capabilities versus wrapper commands

AvaloniaEdit supplies the underlying text surface. Advanced native engine integrations remain accessible through `ProTextEditor.Editor`; they are not reimplemented or claimed as independently validated ProText features. Wrapper commands currently operate on the primary linear selection. Rectangular selections and multi-caret workflows, particularly custom pair insertion and batch line commands, require additional integration work and tests before relying on them as full professional multi-selection editing.

## Deliberate limitations

This release is a text/code editor, not a formatted rich-text word processor. It does not implement pagination, tables, inline pictures, DOCX import/export or document printing.

Completion and diagnostics are service interfaces, not a bundled language server or compiler. There is no built-in LSP transport, project-wide symbol index, semantic refactoring, debugger, inline code lens, inlay hints or signature-help engine. The gallery providers use a small keyword/snippet list and TODO matching. Snippets insert text but do not yet provide linked placeholder navigation.

The folding provider is a lexical brace/comment scanner. It recognizes ordinary quoted strings, characters, verbatim strings and raw quote runs, but is not a complete C# interpolated/raw-string parser or an indentation-language folding engine. Consumers can replace it with a language-aware provider. Basic syntax coloring is intentionally smaller than a full TextMate grammar installation; consumers may integrate TextMate through the native surface.

The overview renders sampled line extents, viewport and issue/bookmark markers; it is not a full-resolution text minimap. Search hits and diagnostic underlines are rendered only for visible lines, while explicit search still takes a document snapshot. Extremely long individual lines, multi-gigabyte files and memory-mapped editing need further work.

Diff compares line text, ignoring delimiter differences. It is not a three-way merge tool, has no intraline character diff, and does not implement synchronized blank-line alignment or editable merge decisions. The coarse-mode flag is visible rather than hiding approximation.

No collaborative/CRDT layer, recovery journal, disk change monitor or encrypted document store is included. Open/save is a sample-host concern; the reusable libraries perform no autonomous file or network I/O.

## Release qualification still required

Automated Chromium tests and native headless tests do not establish complete cross-platform accessibility or input compatibility. Complex IME composition, bidirectional text, screen-reader TextPattern support, platform clipboard permission flows, touch selection handles and browser-specific keyboard conflicts need dedicated Windows/macOS/Linux, Safari/Firefox and mobile validation. Treat this as a tested initial implementation, not a blanket production-readiness certification.
