#!/usr/bin/env python3
"""Complete native protected-edit integration after port-readonly.py.

This is a source migration, not a runtime patch. Its resulting C# is committed
only after native, baseline and browser validation.
"""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    protection = ROOT / 'src/UnoEdit.Core/Editing/EditorSession.ReadOnly.cs'
    text = protection.read_text(encoding='utf-8')
    if 'private void DeleteProtected' not in text:
        anchor = '    private void ReplaceProtectedSelection(string text)\n    {\n'
        if text.count(anchor) != 1:
            raise RuntimeError('Unexpected protected input implementation')
        text = text.replace(anchor, '''    private void DeleteProtected(bool backwards, bool byWord)
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
''')
        text = text.replace('        var start = SelectionStart;\n        var length = SelectionLength;\n', '')
        protection.write_text(text, encoding='utf-8')
        session = ROOT / 'src/UnoEdit.Core/Editing/EditorSession.cs'
        text = session.read_text(encoding='utf-8')
        start = text.index('    public void Delete(bool backwards, bool byWord = false)')
        end = text.index('    public void Enter()', start)
        session.write_text(text[:start] + '    public void Delete(bool backwards, bool byWord = false) => DeleteProtected(backwards, byWord);\n\n' + text[end:], encoding='utf-8')
    checks = ROOT / 'src/UnoEdit.Uno.Demo/NativeControlChecks.cs'
    text = checks.read_text(encoding='utf-8')
    if 'NativeReadOnlyChecks.Run()' not in text:
        anchor = '        checks.AddRange(LegacyHighlightingChecks.Run());'
        if text.count(anchor) != 1:
            raise RuntimeError('Unexpected native test entry point')
        checks.write_text(text.replace(anchor, anchor + '\n        checks.AddRange(NativeReadOnlyChecks.Run());'), encoding='utf-8')
    print('Blocked deletion and native UI protection checks integrated.')


if __name__ == '__main__':
    main()
