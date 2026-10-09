using System;
using System.Linq;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NUnit.Framework;
using SkiaSharp;
using UnoEdit.Document;
using UnoEdit.Folding;
using UnoEdit.Rendering;
using UnoEdit.Rendering.Skia;
using UnoEdit.Search;
using Windows.Foundation;

namespace UnoEdit.Uno.Tests;

[TestFixture]
public sealed class InlineAndProjectionTests
{
    [Test] public void InlineControlHasRealParentGeometryAndDetachesWhenGeneratorIsRemoved()
    {
        using var editor = new TextEditor { Text = "before after", Width = 620, Height = 260 };
        TestApp.Root.Children.Add(editor); TestApp.Root.UpdateLayout();
        try
        {
            var button = new Button { Content = "Action", Width = 120, Height = 40 };
            var generator = new InlineGenerator(button);
            var view = editor.TextArea.TextView; view.ElementGenerators.Add(generator);
            using var surface = SKSurface.Create(new SKImageInfo(600, 240));
            typeof(TextView).GetMethod("RenderOverride", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, new object[] { surface.Canvas, new Size(600, 240) });
            TestApp.Root.UpdateLayout();
            Assert.That(VisualTreeHelper.GetParent(button), Is.InstanceOf<Canvas>());
            Assert.That(button.ActualWidth, Is.EqualTo(120).Within(.1));
            Assert.That(button.ActualHeight, Is.EqualTo(40).Within(.1));
            Assert.That(view.GetOrConstructVisualLine(editor.Document.GetLineByNumber(1)).Height, Is.GreaterThanOrEqualTo(40));
            view.ElementGenerators.Remove(generator);
            Assert.That(VisualTreeHelper.GetParent(button), Is.Null);
            Assert.That(editor.Text, Is.EqualTo("before after"));
        }
        finally { TestApp.Root.Children.Remove(editor); }
    }
    [Test] public void MarkerOnlyInvalidationRetainsVisualLineAndLargeFoldDoesNotEnumerateHiddenMatches()
    {
        using var editor = new TextEditor { Text = "begin {\n" + string.Join("\n", Enumerable.Repeat("match", 20000)) + "\n} end" };
        var view = editor.TextArea.TextView; view.Viewport.SetViewport(600, 300);
        using var manager = FoldingManager.Install(editor.TextArea); var fold = manager.CreateFolding(6, editor.Text.Length - 4); fold.IsFolded = true;
        using var search = new SearchSession(editor.TextArea.Session);
        search.Configure("match", false, false, false);
        search.IsActive = true;
        using var markers = new SearchResultMarkerSource(search) { Color = SKColors.Yellow }; view.Viewport.MarkerSource = markers;
        using var surface = SKSurface.Create(new SKImageInfo(600, 300)); view.Viewport.Render(surface.Canvas);
        var count = view.Viewport.LayoutCreationCount;
        Assert.That(search.Results.Count, Is.EqualTo(20000));
        Assert.That(view.Viewport.LastVisibleLineCount, Is.EqualTo(1));
        Assert.That(view.Viewport.LastMarkerCount, Is.LessThan(5));
        view.InvalidateLayer(KnownLayer.Selection); view.Viewport.Render(surface.Canvas);
        Assert.That(view.Viewport.LayoutCreationCount, Is.EqualTo(count));
    }
    [Test] public void WrappedTextRowsExposeOnlyTheirOwnTextRuns()
    {
        using var editor = new TextEditor { Text = "hello world", WordWrap = true };
        var view = editor.TextArea.TextView; view.Viewport.SetViewport(100, 300);
        var line = view.GetOrConstructVisualLine(editor.Document.GetLineByNumber(1));
        Assert.That(line.TextLines.Count, Is.GreaterThan(1));
        var text = string.Concat(line.TextLines.SelectMany(l => l.TextRuns).Select(r => new string(r.Text.Span)));
        Assert.That(text, Is.EqualTo(editor.Text));
    }
    private sealed class InlineGenerator : VisualLineElementGenerator
    {
        private readonly FrameworkElement _control;
        internal InlineGenerator(FrameworkElement control) => _control = control;
        public override int GetFirstInterestedOffset(int startOffset) => startOffset <= 7 ? 7 : -1;
        public override VisualLineElement ConstructElement(int offset) => new InlineObjectElement(0, _control);
    }
}
