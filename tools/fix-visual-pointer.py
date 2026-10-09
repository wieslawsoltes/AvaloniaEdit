#!/usr/bin/env python3
"""Apply the reviewed pointer-targeting correction after visual-source recovery.

The original source manifest remains immutable and checksum-verified. This
small follow-up edit is intentionally readable and fails on unexpected source.
"""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(path: str, before: str, after: str) -> None:
    target = ROOT / path
    text = target.read_text(encoding='utf-8')
    if after in text:
        return
    if text.count(before) != 1:
        raise RuntimeError(f'Expected exactly one source match: {path}')
    target.write_text(text.replace(before, after, 1), encoding='utf-8')


replace_once('src/UnoEdit.Uno/Rendering/TextView.VisualLines.cs', '''    private VisualLineElement ElementAt(PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this).Position;
        if (point.X < Viewport.GutterWidth) return null;''', '''    private VisualLineElement ElementAt(PointerRoutedEventArgs e) => HitTestVisualElement(e.GetCurrentPoint(this).Position);

    // Element targeting must use the containing cell, not the nearest caret.
    // A folding placeholder occupies one visual column regardless of its width;
    // nearest-caret rounding makes its entire right half target the next element.
    internal VisualLineElement HitTestVisualElement(Point point)
    {
        if (point.X < Viewport.GutterWidth || point.Y < 0 || point.Y >= ActualHeight) return null;''')
replace_once('src/UnoEdit.Uno/Rendering/TextView.VisualLines.cs',
    'var column = line.GetVisualColumn(new Point(point.X + HorizontalOffset - Viewport.GutterWidth, point.Y + VerticalOffset), false);',
    'var column = line.GetVisualColumnFloor(new Point(point.X + HorizontalOffset - Viewport.GutterWidth, point.Y + VerticalOffset), false);')
replace_once('test/UnoEdit.Uno.Tests/FoldingTests.cs',
    '    [Test] public void UnfoldRestoresHeightAndInvalidatesProjection()', '''    [TestCase(0.1)]
    [TestCase(0.5)]
    [TestCase(0.9)]
    public void EntireFoldingPlaceholderTargetsItsContainingElement(double fraction)
    {
        var section = Whole(); section.Title = "{ ... }"; section.IsFolded = true;
        var view = _editor.TextArea.TextView;
        var line = view.GetOrConstructVisualLine(_editor.Document.GetLineByOffset(section.StartOffset));
        var element = line.Elements.Single(e => e is FormattedTextElement);
        var left = line.GetVisualPosition(element.VisualColumn, VisualYPosition.LineTop);
        var right = line.GetVisualPosition(element.VisualColumn + element.VisualLength, VisualYPosition.LineBottom);
        var point = new Point(view.Viewport.GutterWidth + left.X + (right.X - left.X) * fraction - view.HorizontalOffset,
            (left.Y + right.Y) / 2 - view.VerticalOffset);
        Assert.That(view.HitTestVisualElement(point), Is.SameAs(element));
    }
    [Test] public void UnfoldRestoresHeightAndInvalidatesProjection()''')

friend = ROOT / 'src/UnoEdit.Uno/Properties/InternalsVisibleTo.cs'
content = 'using System.Runtime.CompilerServices;\n\n[assembly: InternalsVisibleTo("UnoEdit.Uno.Tests")]\n'
if friend.exists() and friend.read_text(encoding='utf-8') != content:
    raise RuntimeError('Refusing to replace different assembly friend declarations')
friend.parent.mkdir(parents=True, exist_ok=True)
friend.write_text(content, encoding='utf-8')
print('Applied containing-element hit testing and three native folding regressions.')
