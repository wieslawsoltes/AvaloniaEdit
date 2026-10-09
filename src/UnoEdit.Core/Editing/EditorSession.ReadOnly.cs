using System;
using System.Collections.Generic;
using UnoEdit.Document;

namespace UnoEdit.Editing;

public sealed partial class EditorSession
{
    private IReadOnlySectionProvider _readOnlySectionProvider = NoReadOnlySections.Instance;

    /// <summary>
    /// Gets or sets the original read-only-section provider. Whole-editor
    /// IsReadOnly is an additional restriction and never discards this provider.
    /// Programmatic TextDocument changes and undo keep their original semantics.
    /// </summary>
    public IReadOnlySectionProvider ReadOnlySectionProvider
    {
        get => _readOnlySectionProvider;
        set
        {
            ThrowIfDisposed();
            _readOnlySectionProvider = value ?? throw new ArgumentNullException(nameof(value));
        }
    }

    private SimpleSegment[] GetEditableSegments(int start, int length)
    {
        var requested = new SimpleSegment(start, length);
        var result = new List<SimpleSegment>();
        var previousEnd = start;
        var sequence = ReadOnlySectionProvider.GetDeletableSegments(requested)
            ?? throw new InvalidOperationException("Read-only provider returned a null sequence.");
        foreach (var item in sequence)
        {
            if (item == null || item.Offset < start || item.Length < 0 ||
                (long)item.Offset + item.Length > requested.EndOffset || item.Offset < previousEnd ||
                result.Count > 0 && item.Offset == previousEnd && item.Length == 0)
                throw new InvalidOperationException("Read-only provider returned an invalid, overlapping or out-of-order range.");
            // Freeze mutable TextSegment values before any document mutation.
            result.Add(new SimpleSegment(item.Offset, item.Length));
            previousEnd = item.Offset + item.Length;
        }
        return result.ToArray();
    }

    private void DeleteProtected(bool backwards, bool byWord)
    {
        ThrowIfDisposed();
        if (IsReadOnly) return;
        if (SelectionLength != 0)
        {
            ReplaceProtectedSelection(string.Empty);
            return;
        }
        var direction = backwards ? -1 : 1;
        var target = byWord
            ? TextUtilities.GetNextCaretPosition(_document, _caret,
                backwards ? LogicalDirection.Backward : LogicalDirection.Forward,
                CaretPositioningMode.WordStart)
            : GetCharacterBoundary(_caret, direction);
        if (target < 0) target = backwards ? 0 : _document.TextLength;
        if (target == _caret) return;
        // Plan a temporary range without altering the visible selection. A
        // rejected Backspace/Delete must not select protected text or notify.
        ReplaceProtectedRange(Math.Min(target, _caret), Math.Abs(target - _caret), string.Empty);
    }

    private void ReplaceProtectedSelection(string text) =>
        ReplaceProtectedRange(SelectionStart, SelectionLength, text);

    private void ReplaceProtectedRange(int start, int length, string text)
    {
        ThrowIfDisposed();
        if (text == null) throw new ArgumentNullException(nameof(text));
        if (IsReadOnly) return;
        if (length == 0 && text.Length == 0) return;
        var document = _document;
        var version = document.Version;
        SimpleSegment[] edits;
        if (length == 0)
        {
            if (!ReadOnlySectionProvider.CanInsert(start)) return;
            edits = new[] { new SimpleSegment(start, 0) };
        }
        else
        {
            edits = GetEditableSegments(start, length);
        }
        VerifyUnchangedDocument(document, version);
        if (edits.Length == 0) return;
        var targetCaret = text.Length == 0 ? start : edits[edits.Length - 1].Offset + text.Length;
        if (text.Length != 0)
            for (var i = 0; i < edits.Length - 1; i++) targetCaret -= edits[i].Length;
        _transaction++;
        try
        {
            using (document.RunUpdate())
            {
                // Original SimpleSelection contract: replace the last editable
                // range, delete earlier ranges, leave protected islands intact.
                for (var i = edits.Length - 1; i >= 0; i--)
                {
                    var edit = edits[i];
                    document.Replace(edit.Offset, edit.Length, i == edits.Length - 1 ? text : string.Empty);
                }
                _anchor = _caret = Math.Clamp(targetCaret, 0, document.TextLength);
                _changed = true;
            }
        }
        finally
        {
            _transaction--;
            if (_transaction == 0 && _changed) NotifyChanged();
        }
    }

    private void IndentProtectedLines(bool backwards, int indentationSize)
    {
        var document = _document;
        var version = document.Version;
        var first = document.GetLineByOffset(SelectionStart).LineNumber;
        var end = SelectionStart + SelectionLength;
        var lastLine = document.GetLineByOffset(end);
        var last = lastLine.LineNumber;
        if (SelectionLength > 0 && lastLine.Offset == end && last > first) last--;
        var edits = new List<SimpleSegment>();
        for (var number = last; number >= first; number--)
        {
            var line = document.GetLineByNumber(number);
            if (!backwards)
            {
                if (ReadOnlySectionProvider.CanInsert(line.Offset)) edits.Add(new SimpleSegment(line.Offset, 0));
                continue;
            }
            var count = 0;
            if (line.Length > 0 && document.GetCharAt(line.Offset) == '\t') count = 1;
            else while (count < line.Length && count < indentationSize && document.GetCharAt(line.Offset + count) == ' ') count++;
            if (count == 0) continue;
            var allowed = GetEditableSegments(line.Offset, count);
            for (var index = allowed.Length - 1; index >= 0; index--)
                if (allowed[index].Length != 0) edits.Add(allowed[index]);
        }
        VerifyUnchangedDocument(document, version);
        if (edits.Count == 0) return;
        var indentation = backwards ? string.Empty : Options.ConvertTabsToSpaces ? new string(' ', indentationSize) : "\t";
        _transaction++;
        try
        {
            using (document.RunUpdate())
            {
                foreach (var edit in edits) document.Replace(edit.Offset, edit.Length, indentation);
            }
        }
        finally
        {
            _transaction--;
            if (_transaction == 0 && _changed) NotifyChanged();
        }
    }

    private void VerifyUnchangedDocument(TextDocument document, ITextSourceVersion version)
    {
        if (!ReferenceEquals(_document, document) || document.Version.CompareAge(version) != 0)
            throw new InvalidOperationException("Read-only providers must not change the document while an edit is being planned.");
    }
}
