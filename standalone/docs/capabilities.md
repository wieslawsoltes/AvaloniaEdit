# Scribe independence and release boundaries

## What is original

The text buffer, AVL balancing, snapshots, newline metadata, transactions, history grouping, view selections, grapheme commands, anchored positions, search planning, snippet expansion, lexical scanner, visual-row indexing, layout cache, caret/selection painting, mouse/keyboard handling, IME client and editor templates are Scribe implementation code.

The main editing area does not instantiate or inherit from a TextBox or any existing text editor. Avalonia `TextLayout` provides shaping and glyph hit testing, like a graphics primitive; it does not supply editing or document storage. Standard Buttons, TextBoxes and ListBoxes are used only for surrounding chrome such as the search field and completion list.

The source solution has no parent-directory project references. `Scribe.Core` has zero NuGet dependencies. `Scribe.Avalonia` references the core and Avalonia framework only. `verify_independence.py`, native tests and the isolated package consumer verify these boundaries.

## Implemented

- Templated editor, search and status controls, with a stable original rendering viewport and optional overview.
- Persistent immutable snapshots, atomic edit plans and grouped undo/redo with shared-document view remapping.
- Multiple carets, disjoint and rectangular selections, occurrence selection, clipboard integration and line-editing commands.
- Grapheme/emoji/combining-mark movement and deletion, mixed CR/LF/CRLF handling, tab expansion and original pointer hit testing.
- Linked snippet placeholders and keyboard tab stops, including repeated placeholders.
- Literal/regex/case/whole-word/selection-scoped search and transactional replacement, with explicit budget/error handling.
- Basic lexical highlighting, brace folding, bookmarks, sampled overview, asynchronous completion/diagnostic provider integration.
- Soft wrapping, visible-line realization, bounded shaped-layout cache, large-document sample and idle-render-loop regression test.
- Original IME surrounding-text/selection client and drawn preedit overlay, with browser composition testing.
- Shared native desktop and real Avalonia WebAssembly samples; isolated NuGet packaging and dependency auditing.

## Performance bounds and tradeoffs

The persistent rope is chunked at 1,024 UTF-16 units and shares unchanged nodes between snapshots. Line lookup currently binary-searches augmented line starts; it is not a memory-mapped file backend. A newline-changing edit rebuilds the viewport's line-weight array. Large documents remain editable but no constant-time newline insertion claim is made.

The viewport retains at most 128 shaped logical-line layouts, with an additional raw/display-code-unit budget of 262,144. A single exceptional line can exceed the aggregate character budget after tab expansion, but the logical line's display input is capped. **Only the first 65,536 UTF-16 units of an individual logical line are shaped/displayed in this preview.** The complete line remains in the document and is saved intact; a visible limit label is drawn. This is a known long-line rendering boundary, not full giant-line support.

Wrapping is measured lazily for visited lines. Unmeasured lines initially contribute one estimated visual row, so scroll extent/position can adjust as long lines are measured. This is not an exact precomputed whole-document wrapping index. Syntax coloring is per visible line; lexical folding and diagnostic analysis pause above 2 million UTF-16 units. Explicit search materializes a document snapshot string and is not streaming file search.

The document default maximum is 50 million UTF-16 units; the sample file opener limits input to 20 million. History is capped by group count, not by a strict retained-byte ceiling. Limits are safeguards, not guarantees that every device can comfortably edit documents at the maximum.

## Not yet provided

There is no bundled LSP transport, compiler, semantic refactoring, debugger, project index, signature help, inlay hints, code lens or full grammar system. The sample providers intentionally demonstrate integration contracts rather than compiler intelligence. Folding is lexical, not a complete interpolated/raw-string or indentation-language parser.

The snippet syntax does not implement nested placeholders, variable resolvers, regular-expression transforms, choices or arbitrary scripting. Rectangular selection uses logical-line positions and is not a complete virtual-space column editor. Selection ordering is normalized by document position, not a separate last-click primary-caret ordering model.

This preview does not include a diff/three-way merge control, collaborative editing, crash recovery, disk-change monitoring, a printing engine, rich-text formatting, pagination, tables or DOCX/PDF import/export. A host may build these on the original core; they are not claimed as present.

## Platform qualification

Automated Chromium input and native headless tests cover specific scenarios, not every platform/input environment. Complex candidate-window behavior, bidirectional visual arrow navigation, all language-specific IMEs, screen-reader text-range providers, touch selection handles, real mobile keyboards, Safari and Firefox require additional release qualification. The IME preview is drawn at the caret rather than integrated into the complete wrapped paragraph layout. Rendering can shape bidirectional text, but full bidirectional editing semantics and accessibility certification are not claimed.

Desktop compilation is separate from a real native-desktop interaction matrix. Browser viewport resizing is separate from testing on physical mobile devices. Use the supplied reports to distinguish actual tested behavior from interfaces that merely exist.
