using System;
using System.Linq;
using NUnit.Framework;
using SkiaSharp;
using UnoEdit.Document;
using UnoEdit.Editing;
using UnoEdit.Rendering.Skia;
using UnoEdit.Search;

namespace UnoEdit.Rendering.Skia.Tests;

[TestFixture]
public sealed class SearchMarkerTests
{
    [TestCase("abc אבג 123", 1, 8)]
    [TestCase("مرحبا بالعالم", 2, 5)]
    [TestCase("a\t🙂e\u0301 tail", 1, 5)]
    public void MatchGeometryUsesFiniteShapedRunCoordinates(string text, int offset, int length)
    {
        using var layout = new TextLineLayout(text, new TextViewStyle { FontSize = 18 }, 90);
        var rectangles = layout.GetRangeRectangles(offset, length);
        Assert.That(rectangles, Is.Not.Empty);
        foreach (var rectangle in rectangles)
        {
            Assert.That(float.IsFinite(rectangle.Left) && float.IsFinite(rectangle.Top), Is.True);
            Assert.That(rectangle.Width, Is.GreaterThanOrEqualTo(0));
            Assert.That(rectangle.Height, Is.GreaterThan(0));
            Assert.That(rectangle.Bottom, Is.LessThanOrEqualTo(layout.Height + 1));
        }
    }

    [Test]
    public void WrappedRangeCreatesGeometryOnEachVisualLine()
    {
        var text = string.Join(" ", Enumerable.Repeat("word", 20));
        using var layout = new TextLineLayout(text, new TextViewStyle { FontSize = 16 }, 100);
        var rectangles = layout.GetRangeRectangles(0, text.Length);
        Assert.That(rectangles.Select(r => r.Top).Distinct().Count(), Is.GreaterThan(3));
        Assert.That(rectangles.All(r => r.Right <= 101), Is.True);
    }

    [Test]
    public void SearchMarkersAreViewportIndexedAndNeverRecreateShapedLayouts()
    {
        var document = new TextDocument(string.Join("\n", Enumerable.Repeat("needle text", 100000)));
        using var editor = new EditorSession(document);
        using var search = new SearchSession(editor);
        using var markers = new SearchResultMarkerSource(search);
        using var viewport = new DocumentViewport(document) { MarkerSource = markers };
        using var surface = SKSurface.Create(new SKImageInfo(640, 480));
        viewport.SetViewport(640, 480);
        viewport.ScrollToLine(95000);
        viewport.Render(surface.Canvas);
        var shaped = viewport.LayoutCreationCount;
        search.Configure("needle");
        search.Refresh();
        viewport.Render(surface.Canvas);
        Assert.That(viewport.LastMarkerCount, Is.EqualTo(viewport.LastVisibleLineCount));
        Assert.That(viewport.LayoutCreationCount, Is.EqualTo(shaped));
        markers.Color = new SKColor(255, 0, 0, 90);
        viewport.Render(surface.Canvas);
        Assert.That(viewport.LayoutCreationCount, Is.EqualTo(shaped));
        Assert.That(search.SearchExecutionCount, Is.EqualTo(1));
    }

    [Test]
    public void DocumentEditImmediatelyDropsStaleMarkersBeforeAnotherSearch()
    {
        using var editor = new EditorSession(new TextDocument("needle"));
        using var search = new SearchSession(editor);
        using var markers = new SearchResultMarkerSource(search);
        search.Configure("needle");
        search.Refresh();
        Assert.That(markers.GetMarkers(editor.Document.GetLineByNumber(1)).Count, Is.EqualTo(1));
        editor.Document.Insert(0, "prefix ");
        Assert.That(markers.GetMarkers(editor.Document.GetLineByNumber(1)), Is.Empty);
        Assert.That(search.SearchExecutionCount, Is.EqualTo(1), "Painting must not rerun a dirty query");
        search.Refresh();
        Assert.That(markers.GetMarkers(editor.Document.GetLineByNumber(1))[0].Start, Is.EqualTo(7));
    }

    [Test]
    public void MultilineAndZeroWidthMatchesRespectDelimiterAndLineBoundaries()
    {
        using var editor = new EditorSession(new TextDocument("ab\r\ncd"));
        using var search = new SearchSession(editor);
        using var markers = new SearchResultMarkerSource(search);
        search.Configure("b\r\nc");
        search.Refresh();
        var first = markers.GetMarkers(editor.Document.GetLineByNumber(1)).Single();
        var second = markers.GetMarkers(editor.Document.GetLineByNumber(2)).Single();
        Assert.That((first.Start, first.Length, first.IncludesLineBreak), Is.EqualTo((1, 1, true)));
        Assert.That((second.Start, second.Length, second.IncludesLineBreak), Is.EqualTo((0, 1, false)));
        search.Configure("^", useRegex: true);
        search.Refresh();
        Assert.That(markers.GetMarkers(editor.Document.GetLineByNumber(2)).Single().Length, Is.Zero);
    }

    [Test]
    public void ZeroWidthGeometryIsVisibleAndBoundsAreValidated()
    {
        using var layout = new TextLineLayout(string.Empty, new TextViewStyle());
        Assert.That(layout.GetRangeRectangles(0, 0).Single().Width, Is.EqualTo(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.GetRangeRectangles(0, 1));
    }

    [Test]
    public void RebindingViewportDetachesMarkerSourceAndRejectsForeignSources()
    {
        using var editor = new EditorSession(new TextDocument("first"));
        using var search = new SearchSession(editor);
        using var markers = new SearchResultMarkerSource(search);
        using var viewport = new DocumentViewport(editor.Document) { MarkerSource = markers };
        viewport.Document = new TextDocument("second");
        Assert.That(viewport.MarkerSource, Is.Null);
        Assert.Throws<ArgumentException>(() => viewport.MarkerSource = markers);
        Assert.Throws<ArgumentException>(() => markers.GetMarkers(viewport.Document.GetLineByNumber(1)));
    }
}
