using System;
using System.Linq;
using NUnit.Framework;
using SkiaSharp;
using UnoEdit.Document;
using UnoEdit.Folding;
using UnoEdit.Rendering;
using Windows.Foundation;

namespace UnoEdit.Uno.Tests;

[TestFixture]
public sealed class FoldingTests
{
    private TextEditor _editor;
    private FoldingManager _manager;
    [SetUp] public void Setup()
    {
        _editor = new TextEditor { Text = "start {\n alpha\n inner {\n beta\n }\n omega\n} tail", Width = 640, Height = 360 };
        TestApp.Root.Children.Add(_editor); TestApp.Root.UpdateLayout();
        _editor.TextArea.TextView.Viewport.SetViewport(600, 300);
        _manager = FoldingManager.Install(_editor.TextArea);
    }
    [TearDown] public void Cleanup() { _manager.Dispose(); TestApp.Root.Children.Remove(_editor); _editor.Dispose(); }
    private FoldingSection Whole() => _manager.CreateFolding(_editor.Text.IndexOf('{'), _editor.Text.LastIndexOf('}') + 1);
    [Test] public void FoldingPreservesDocumentAndShapesLabelWithTrailingText()
    {
        var text = _editor.Text; var view = _editor.TextArea.TextView;
        var section = Whole(); section.Title = "{ ... }"; section.IsFolded = true;
        var visual = view.GetOrConstructVisualLine(_editor.Document.GetLineByNumber(1));
        Assert.That(visual.LastDocumentLine.LineNumber, Is.EqualTo(7));
        Assert.That(visual.Elements.Any(e => e is FormattedTextElement), Is.True);
        Assert.That(view.GetOrConstructVisualLine(_editor.Document.GetLineByNumber(4)), Is.SameAs(visual));
        Assert.That(_editor.Text, Is.EqualTo(text));
        using var surface = SKSurface.Create(new SKImageInfo(600, 300)); view.Viewport.Render(surface.Canvas);
        Assert.That(view.Viewport.LastVisibleLineCount, Is.EqualTo(1));
        Assert.That(view.Viewport.GetSegmentRectangles(_editor.Text.IndexOf("tail"), 4).Count, Is.GreaterThan(0));
    }
    [TestCase(0.1)]
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
    [Test] public void UnfoldRestoresHeightAndInvalidatesProjection()
    {
        var view = _editor.TextArea.TextView; var section = Whole(); section.IsFolded = true;
        var folded = view.GetOrConstructVisualLine(_editor.Document.GetLineByNumber(1));
        var height = view.ExtentHeight;
        section.IsFolded = false;
        Assert.That(folded.IsDisposed, Is.True);
        Assert.That(view.Viewport.IsLineCollapsed(3), Is.False);
        Assert.That(view.ExtentHeight, Is.GreaterThan(height));
        Assert.That(view.GetOrConstructVisualLine(_editor.Document.GetLineByNumber(3)).FirstDocumentLine.LineNumber, Is.EqualTo(3));
    }
    [Test] public void NestedFoldRemainsClosedWhenParentOpens()
    {
        var inner = _manager.CreateFolding(_editor.Text.IndexOf("inner {") + 6, _editor.Text.IndexOf("\n }", StringComparison.Ordinal) + 3);
        inner.IsFolded = true; var outer = Whole(); outer.IsFolded = true;
        var view = _editor.TextArea.TextView; view.GetOrConstructVisualLine(_editor.Document.GetLineByNumber(1));
        outer.IsFolded = false;
        Assert.That(inner.IsFolded, Is.True); Assert.That(view.Viewport.IsLineCollapsed(4), Is.True);
        var line = view.GetOrConstructVisualLine(_editor.Document.GetLineByNumber(3));
        Assert.That(line.LastDocumentLine.LineNumber, Is.EqualTo(5));
    }
    [Test] public void ChangingFontRetainsFoldedRangesAndUpdatesLabelMetrics()
    {
        var section = Whole(); section.IsFolded = true; var view = _editor.TextArea.TextView;
        var first = view.GetOrConstructVisualLine(_editor.Document.GetLineByNumber(1));
        var height = first.Height; _editor.FontSize = 30;
        Assert.That(view.Viewport.IsLineCollapsed(4), Is.True);
        var current = view.GetOrConstructVisualLine(_editor.Document.GetLineByNumber(1));
        Assert.That(current.Height, Is.GreaterThan(height)); Assert.That(current.LastDocumentLine.LineNumber, Is.EqualTo(7));
    }
    [Test] public void LiveEditMovesFoldingOffsetsAndUndoRestoresThem()
    {
        var section = Whole(); var start = section.StartOffset; section.IsFolded = true;
        _editor.Document.Insert(0, "prefix\n");
        Assert.That(section.StartOffset, Is.EqualTo(start + 7)); Assert.That(section.IsFolded, Is.True);
        Assert.That(_editor.TextArea.TextView.GetOrConstructVisualLine(_editor.Document.GetLineByNumber(2)).LastDocumentLine.LineNumber, Is.EqualTo(8));
        _editor.Document.UndoStack.Undo(); Assert.That(section.StartOffset, Is.EqualTo(start));
    }
    [Test] public void CaretEnteringFoldUnfoldsIt()
    {
        var section = Whole(); section.IsFolded = true;
        _editor.CaretOffset = _editor.Text.IndexOf("beta", StringComparison.Ordinal);
        Assert.That(section.IsFolded, Is.False);
        Assert.That(_editor.TextArea.TextView.Viewport.IsLineCollapsed(4), Is.False);
    }
    [Test] public void RebindingDocumentUninstallsOldFoldingWithoutTouchingNewDocument()
    {
        var section = Whole(); section.IsFolded = true;
        _editor.Document = new TextDocument("replacement");
        Assert.That(_editor.TextArea.LeftMargins, Is.Empty);
        Assert.That(_editor.TextArea.TextView.ElementGenerators.OfType<FoldingElementGenerator>(), Is.Empty);
        Assert.That(_editor.TextArea.TextView.GetService(typeof(FoldingManager)), Is.Null);
        Assert.That(_editor.TextArea.TextView.GetOrConstructVisualLine(_editor.Document.GetLineByNumber(1)).VisualLength, Is.EqualTo(11));
    }
    [Test] public void ManagerUninstallAndDisposeAreIdempotent()
    {
        Whole().IsFolded = true; FoldingManager.Uninstall(_manager); FoldingManager.Uninstall(_manager); _manager.Dispose();
        Assert.That(_editor.TextArea.LeftMargins, Is.Empty); Assert.That(_editor.TextArea.TextView.Viewport.IsLineCollapsed(4), Is.False);
    }
    [Test] public void EditorDisposalDetachesInstalledManager()
    {
        Whole().IsFolded = true; var document = _editor.Document;
        _editor.Dispose(); document.Insert(0, "detached");
        Assert.That(_manager.AllFoldings, Is.Empty);
    }
    [Test] public void UpdatingFoldingsPreservesIdentityStateAndRemovesObsoleteRegions()
    {
        _manager.UpdateFoldings(new[] { new NewFolding(6, 20) { Name = "one", DefaultClosed = true }, new NewFolding(24, 29) { Name = "two" } }, -1);
        var first = _manager.AllFoldings.First(); Assert.That(first.IsFolded, Is.True);
        _manager.UpdateFoldings(new[] { new NewFolding(6, 25) { Name = "updated" } }, -1);
        Assert.That(_manager.AllFoldings.Single(), Is.SameAs(first)); Assert.That(first.Title, Is.EqualTo("updated")); Assert.That(first.IsFolded, Is.True);
    }
    [Test] public void XmlStrategyProducesNestedNativeFoldings()
    {
        _editor.Text = "<root>\n<child>\n value\n</child>\n</root>";
        new XmlFoldingStrategy().UpdateFoldings(_manager, _editor.Document);
        Assert.That(_manager.AllFoldings.Count(), Is.GreaterThanOrEqualTo(2));
        foreach (var fold in _manager.AllFoldings) fold.IsFolded = true;
        Assert.That(_editor.TextArea.TextView.GetOrConstructVisualLine(_editor.Document.GetLineByNumber(1)).LastDocumentLine.LineNumber, Is.EqualTo(5));
    }
}
