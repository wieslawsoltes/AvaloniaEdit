using System;
using System.Globalization;
using UnoEdit.Document;

namespace UnoEdit.Editing;

/// <summary>
/// Document-backed input state shared by the native Uno host and its tests.
/// Offsets are UTF-16, like TextDocument. Moving/deleting a character respects
/// Unicode text elements and never splits a CRLF delimiter. No operation copies
/// the complete document except an explicitly requested complete selection.
/// </summary>
public sealed partial class EditorSession : IDisposable
{
    private TextDocument _document;
    private int _anchor;
    private int _caret;
    private bool _disposed;
    private int _transaction;
    private bool _changed;

    public EditorSession(TextDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _document.Changed += OnDocumentChanged;
    }

    public TextDocument Document
    {
        get => _document;
        set
        {
            ThrowIfDisposed();
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (ReferenceEquals(value, _document)) return;
            _document.Changed -= OnDocumentChanged;
            _document = value;
            _document.Changed += OnDocumentChanged;
            _anchor = _caret = 0;
            NotifyChanged();
        }
    }

    public TextEditorOptions Options { get; set; } = new TextEditorOptions();
    public bool IsReadOnly { get; set; }
    public int AnchorOffset => _anchor;
    public int CaretOffset => _caret;
    public int SelectionStart => Math.Min(_anchor, _caret);
    public int SelectionLength => Math.Abs(_anchor - _caret);
    public string SelectedText => _document.GetText(SelectionStart, SelectionLength);
    public event EventHandler Changed;

    public void Select(int start, int length)
    {
        ThrowIfDisposed();
        if (start < 0 || start > _document.TextLength) throw new ArgumentOutOfRangeException(nameof(start));
        if (length < 0 || length > _document.TextLength - start) throw new ArgumentOutOfRangeException(nameof(length));
        _anchor = start;
        _caret = start + length;
        NotifyChanged();
    }

    public void MoveTo(int offset, bool extendSelection = false)
    {
        ThrowIfDisposed();
        if (offset < 0 || offset > _document.TextLength) throw new ArgumentOutOfRangeException(nameof(offset));
        _caret = offset;
        if (!extendSelection) _anchor = offset;
        NotifyChanged();
    }

    public void SelectAll() => Select(0, _document.TextLength);

    public void ReplaceSelection(string text) => ReplaceProtectedSelection(text);

    public void MoveHorizontal(int direction, bool extendSelection = false, bool byWord = false)
    {
        ThrowIfDisposed();
        if (direction != -1 && direction != 1) throw new ArgumentOutOfRangeException(nameof(direction));
        if (!extendSelection && SelectionLength != 0)
        {
            MoveTo(direction < 0 ? SelectionStart : SelectionStart + SelectionLength);
            return;
        }
        var target = byWord
            ? TextUtilities.GetNextCaretPosition(_document, _caret,
                direction < 0 ? LogicalDirection.Backward : LogicalDirection.Forward,
                CaretPositioningMode.WordStart)
            : GetCharacterBoundary(_caret, direction);
        MoveTo(target < 0 ? (direction < 0 ? 0 : _document.TextLength) : target, extendSelection);
    }

    public void MoveVertical(int lines, bool extendSelection = false)
    {
        ThrowIfDisposed();
        var current = _document.GetLineByOffset(_caret);
        var targetNumber = (int)Math.Clamp((long)current.LineNumber + lines, 1, _document.LineCount);
        var target = _document.GetLineByNumber(targetNumber);
        var column = Math.Min(_caret - current.Offset, target.Length);
        var text = _document.GetText(target.Offset, target.Length);
        // Snap an equal UTF-16 column to the beginning of the containing grapheme.
        var boundaries = StringInfo.ParseCombiningCharacters(text);
        if (column < target.Length && boundaries.Length != 0)
        {
            var index = Array.BinarySearch(boundaries, column);
            if (index < 0) column = boundaries[Math.Max(0, ~index - 1)];
        }
        MoveTo(target.Offset + column, extendSelection);
    }

    public void MoveLineBoundary(bool end, bool extendSelection = false, bool wholeDocument = false)
    {
        ThrowIfDisposed();
        if (wholeDocument)
        {
            MoveTo(end ? _document.TextLength : 0, extendSelection);
            return;
        }
        var line = _document.GetLineByOffset(_caret);
        MoveTo(end ? line.EndOffset : line.Offset, extendSelection);
    }

    public void Delete(bool backwards, bool byWord = false) => DeleteProtected(backwards, byWord);

    public void Enter()
    {
        ThrowIfDisposed();
        if (IsReadOnly) return;
        var line = _document.GetLineByOffset(SelectionStart);
        var prefix = _document.GetText(line.Offset, Math.Min(SelectionStart - line.Offset, line.Length));
        var indentLength = 0;
        while (indentLength < prefix.Length && (prefix[indentLength] == ' ' || prefix[indentLength] == '\t'))
            indentLength++;
        var delimiter = line.DelimiterLength != 0
            ? _document.GetText(line.EndOffset, line.DelimiterLength)
            : line.PreviousLine?.DelimiterLength > 0
                ? _document.GetText(line.PreviousLine.EndOffset, line.PreviousLine.DelimiterLength)
                : "\n";
        ReplaceSelection(delimiter + prefix.Substring(0, indentLength));
    }

    public void Indent(bool backwards = false)
    {
        ThrowIfDisposed();
        if (IsReadOnly) return;
        var indentationSize = Math.Max(1, Options.IndentationSize);
        if (SelectionLength == 0 && !backwards)
        {
            var column = _caret - _document.GetLineByOffset(_caret).Offset;
            ReplaceSelection(Options.ConvertTabsToSpaces ? new string(' ', indentationSize - column % indentationSize) : "\t");
            return;
        }
        IndentProtectedLines(backwards, indentationSize);
    }

    public void Undo()
    {
        ThrowIfDisposed();
        if (!IsReadOnly) _document.UndoStack.Undo();
    }

    public void Redo()
    {
        ThrowIfDisposed();
        if (!IsReadOnly) _document.UndoStack.Redo();
    }

    private int GetCharacterBoundary(int offset, int direction)
    {
        if (direction < 0 && offset == 0 || direction > 0 && offset == _document.TextLength) return offset;
        var line = _document.GetLineByOffset(offset);
        if (direction < 0 && offset == line.Offset) return line.PreviousLine?.EndOffset ?? 0;
        if (direction > 0 && offset >= line.EndOffset) return line.NextLine?.Offset ?? _document.TextLength;
        if (direction < 0 && offset > line.EndOffset) return line.EndOffset;
        var boundaries = StringInfo.ParseCombiningCharacters(_document.GetText(line.Offset, line.Length));
        var relative = offset - line.Offset;
        var index = Array.BinarySearch(boundaries, relative);
        if (direction < 0)
        {
            index = index >= 0 ? index - 1 : ~index - 1;
            return line.Offset + (index < 0 ? 0 : boundaries[index]);
        }
        index = index >= 0 ? index + 1 : ~index;
        return line.Offset + (index >= boundaries.Length ? line.Length : boundaries[index]);
    }

    private void OnDocumentChanged(object sender, DocumentChangeEventArgs e)
    {
        // A collapsed caret must remain collapsed when another editor modifies
        // the shared document at precisely that offset.
        if (_anchor == _caret)
        {
            _anchor = _caret = e.GetNewOffset(_caret, AnchorMovementType.AfterInsertion);
        }
        else
        {
            var reversed = _anchor > _caret;
            _anchor = e.GetNewOffset(_anchor, reversed ? AnchorMovementType.AfterInsertion : AnchorMovementType.BeforeInsertion);
            _caret = e.GetNewOffset(_caret, reversed ? AnchorMovementType.BeforeInsertion : AnchorMovementType.AfterInsertion);
        }
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        if (_transaction != 0) { _changed = true; return; }
        _changed = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(EditorSession));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _document.Changed -= OnDocumentChanged;
        Changed = null;
    }
}
