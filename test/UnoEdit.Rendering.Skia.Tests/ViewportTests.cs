using System;
using System.Linq;
using NUnit.Framework;
using SkiaSharp;
using UnoEdit.Document;
using UnoEdit.Editing;
using UnoEdit.Rendering.Skia;

namespace UnoEdit.Rendering.Skia.Tests;

[TestFixture]
public sealed class ViewportTests
{
    private static TextViewStyle Style => new() { FontFamily = "DejaVu Sans Mono", FontSize = 16 };

    [Test]
    public void ScalarMappingPreservesSurrogatesCombiningMarksAndTabStops()
    {
        using var line = new TextLineLayout("A\t🙂e\u0301", Style);
        Assert.That(line.Length, Is.EqualTo(6));
        Assert.That(line.GetScalarIndex(2), Is.EqualTo(4));
        Assert.That(line.GetScalarIndex(3), Is.EqualTo(4));
        Assert.That(line.GetScalarIndex(4), Is.EqualTo(5));
        Assert.That(line.GetScalarIndex(6), Is.EqualTo(7));
        foreach (var offset in new[] { 0, 1, 2, 4, 5, 6 })
            Assert.That(line.GetUtf16Offset(line.GetScalarIndex(offset)), Is.EqualTo(offset));
    }

    [TestCase("")]
    [TestCase("hello world")]
    [TestCase("مرحبا بالعالم")]
    [TestCase("abc אבג 123")]
    [TestCase("日本語 🙂 e\u0301")]
    public void ShapingAndCaretGeometryAreFinite(string text)
    {
        using var line = new TextLineLayout(text, Style);
        Assert.That(float.IsFinite(line.Width), Is.True);
        Assert.That(line.Height, Is.GreaterThan(0));
        var caret = line.GetCaretRectangle(text.Length);
        Assert.That(float.IsFinite(caret.Left) && float.IsFinite(caret.Top), Is.True);
        Assert.That(line.HitTest(0, 0), Is.InRange(0, text.Length));
        Assert.That(line.HitTest(10000, 10000), Is.InRange(0, text.Length));
        using var surface = SKSurface.Create(new SKImageInfo(400, 100));
        line.Paint(surface.Canvas, 0, 0, 0, text.Length);
    }

    [Test]
    public void AsciiHitTestingUsesTheSameGeometryAsTheCaret()
    {
        using var line = new TextLineLayout("abcdefghij", Style);
        for (var offset = 0; offset <= 10; offset++)
        {
            var caret = line.GetCaretRectangle(offset);
            Assert.That(line.HitTest(caret.Left + 0.05f, (caret.Top + caret.Bottom) / 2), Is.EqualTo(offset));
        }
    }

    [Test]
    public void RepeatedPaintReusesLayoutsAndDocumentEditOnlyInvalidatesTouchedLine()
    {
        var document = new TextDocument(string.Join("\n", Enumerable.Repeat("public class Example { }", 1000)));
        using var viewport = new DocumentViewport(document) { Style = Style };
        using var session = new EditorSession(document);
        using var surface = SKSurface.Create(new SKImageInfo(640, 480));
        viewport.SetViewport(640, 480);
        viewport.Render(surface.Canvas, session);
        var before = viewport.LayoutCreationCount;
        Assert.That(viewport.LastVisibleLineCount, Is.InRange(1, 30));
        viewport.Render(surface.Canvas, session);
        Assert.That(viewport.LayoutCreationCount, Is.EqualTo(before));
        document.Insert(document.GetLineByNumber(4).Offset + 5, "X");
        viewport.Render(surface.Canvas, session);
        Assert.That(viewport.LayoutCreationCount - before, Is.EqualTo(1));
    }

    [Test]
    public void LineCacheRemainsBoundedAcrossALargeDocument()
    {
        var document = new TextDocument(string.Join("\n", Enumerable.Repeat("text", 100000)));
        using var viewport = new DocumentViewport(document) { Style = Style };
        using var surface = SKSurface.Create(new SKImageInfo(640, 240));
        viewport.SetViewport(640, 240);
        for (var number = 1; number < 100000; number += 1000)
        {
            viewport.ScrollToLine(number);
            viewport.Render(surface.Canvas);
            Assert.That(viewport.CachedLineCount, Is.LessThanOrEqualTo(DocumentViewport.CacheCapacity));
            Assert.That(viewport.LastVisibleLineCount, Is.LessThan(20));
        }
        Assert.That(viewport.LayoutCreationCount, Is.LessThan(2000));
    }

    [Test]
    public void WrappedLinesUpdateTheHeightIndexAndStillMapToDocumentOffsets()
    {
        var document = new TextDocument(string.Join(" ", Enumerable.Repeat("word", 100)) + "\nlast");
        using var viewport = new DocumentViewport(document) { Style = Style with { WordWrap = true } };
        using var surface = SKSurface.Create(new SKImageInfo(250, 300));
        viewport.SetViewport(250, 300);
        var estimate = viewport.ExtentHeight;
        viewport.Render(surface.Canvas);
        Assert.That(viewport.ExtentHeight, Is.GreaterThan(estimate));
        var offset = viewport.HitTest(100, 80);
        Assert.That(offset, Is.GreaterThan(0).And.LessThan(document.GetLineByNumber(1).EndOffset));
        viewport.EnsureCaretVisible(document.TextLength);
        Assert.That(viewport.VerticalOffset, Is.GreaterThan(0));
    }

    [Test]
    public void ReplacingDocumentDetachesTheOldSourceAndResetsGeometry()
    {
        var old = new TextDocument("old\nold");
        using var viewport = new DocumentViewport(old);
        viewport.Document = new TextDocument("new");
        var notifications = 0;
        viewport.Invalidated += (_, _) => notifications++;
        old.Insert(0, "ignored");
        Assert.That(notifications, Is.Zero);
        Assert.That(viewport.CachedLineCount, Is.Zero);
        Assert.That(viewport.ExtentHeight, Is.EqualTo(viewport.DefaultLineHeight));
    }
}
