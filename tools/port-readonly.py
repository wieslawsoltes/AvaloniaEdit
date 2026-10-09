#!/usr/bin/env python3
"""Apply the native read-only-section migration without replacing original algorithms.

The workflow validates the complete baseline, core, renderer and native Uno host
before pushing the resulting source commit. Repeated application is a no-op.
"""
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[1]


def replace(path, before, after):
    target = ROOT / path
    text = target.read_text(encoding='utf-8-sig')
    if text.count(before) != 1:
        raise RuntimeError(f'Expected exactly one migration anchor in {path}: {before[:70]}')
    target.write_text(text.replace(before, after), encoding='utf-8')


def write(path, text):
    target = ROOT / path
    if target.exists():
        raise RuntimeError(f'Refusing to overwrite {path}')
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(text, encoding='utf-8')


def main():
    marker = ROOT / 'src/UnoEdit.Core/Editing/EditorSession.ReadOnly.cs'
    if marker.exists():
        print('Read-only migration already applied.')
        return
    for name in ('IReadOnlySectionProvider.cs', 'TextSegmentReadOnlySectionProvider.cs', 'NoReadOnlySections.cs'):
        source = ROOT / 'src/UnoEdit/Editing' / name
        destination = ROOT / 'src/UnoEdit.Core/Editing' / name
        if not source.is_file() or destination.exists():
            raise RuntimeError(f'Ambiguous source for {name}')
        shutil.move(source, destination)
    replace('src/UnoEdit.Core/Editing/NoReadOnlySections.cs', 'return ExtensionMethods.Sequence(segment);', 'return new[] { segment };')
    forwarders = ROOT / 'src/UnoEdit/Properties/CoreTypeForwarders.cs'
    with forwarders.open('a', encoding='utf-8') as file:
        for name in ('IReadOnlySectionProvider', 'TextSegmentReadOnlySectionProvider<>'):
            file.write(f'[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(global::UnoEdit.Editing.{name}))]\n')
    replace('src/UnoEdit.Core/Editing/EditorSession.cs', 'public sealed class EditorSession : IDisposable', 'public sealed partial class EditorSession : IDisposable')
    session = ROOT / 'src/UnoEdit.Core/Editing/EditorSession.cs'
    text = session.read_text(encoding='utf-8')
    start = text.index('    public void ReplaceSelection(string text)')
    end = text.index('    public void MoveHorizontal(', start)
    text = text[:start] + '    public void ReplaceSelection(string text) => ReplaceProtectedSelection(text);\n\n' + text[end:]
    start = text.index('        var first = _document.GetLineByOffset(SelectionStart).LineNumber;', text.index('    public void Indent('))
    end = text.index('    public void Undo()', start)
    text = text[:start] + '        IndentProtectedLines(backwards, indentationSize);\n    }\n\n' + text[end:]
    session.write_text(text, encoding='utf-8')
    replace('src/UnoEdit.Uno/Editing/TextArea.cs', '    public bool IsReadOnly { get => Session.IsReadOnly; set => Session.IsReadOnly = value; }', '''    public bool IsReadOnly { get => Session.IsReadOnly; set => Session.IsReadOnly = value; }
    /// <summary>Gets or sets the original provider controlling editable document sections.</summary>
    public IReadOnlySectionProvider ReadOnlySectionProvider
    {
        get => Session.ReadOnlySectionProvider;
        set => Session.ReadOnlySectionProvider = value;
    }
    /// <summary>Clears the selection while preserving the active caret.</summary>
    public void ClearSelection() => Session.MoveTo(Session.CaretOffset);
    /// <summary>Replaces only editable ranges, preserving protected document content.</summary>
    public void ReplaceSelectionWithText(string text) => Session.ReplaceSelection(text);
    /// <summary>Removes only editable ranges in the current selection.</summary>
    public void RemoveSelectedText() => Session.ReplaceSelection(string.Empty);''')
    write('src/UnoEdit.Core/Editing/EditorSession.ReadOnly.cs', '''using System;
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

    private void ReplaceProtectedSelection(string text)
    {
        ThrowIfDisposed();
        if (text == null) throw new ArgumentNullException(nameof(text));
        if (IsReadOnly) return;
        var start = SelectionStart;
        var length = SelectionLength;
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
            if (line.Length > 0 && document.GetCharAt(line.Offset) == '\\t') count = 1;
            else while (count < line.Length && count < indentationSize && document.GetCharAt(line.Offset + count) == ' ') count++;
            if (count == 0) continue;
            var allowed = GetEditableSegments(line.Offset, count);
            for (var index = allowed.Length - 1; index >= 0; index--)
                if (allowed[index].Length != 0) edits.Add(allowed[index]);
        }
        VerifyUnchangedDocument(document, version);
        if (edits.Count == 0) return;
        var indentation = backwards ? string.Empty : Options.ConvertTabsToSpaces ? new string(' ', indentationSize) : "\\t";
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
''')
    print('Preserved original providers, added public type forwards and integrated native input protection.')


if __name__ == '__main__':
    main()
