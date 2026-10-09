using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using UnoEdit.Document;
using UnoEdit.Folding;
using UnoEdit.Rendering;
using Windows.Foundation;
using Windows.System;

namespace UnoEdit.Uno.Demo;

public sealed partial class DemoPage
{
    private FoldingManager _folding;
    private InlineButtonGenerator _inline;
    private int _inlineClicks;
    private void InitializeVisualFeatures(Panel toolbar)
    {
        AddButton(toolbar, "Fold / Unfold", ToggleFoldings);
        AddButton(toolbar, "Inline button", InsertInlineButton);
        _editor.TextArea.CommandBindings.Add(new RoutedCommandBinding(new RoutedCommand("DemoFoldings", new KeyGesture(VirtualKey.F8)), (_, _) => ToggleFoldings()));
        _editor.TextArea.CommandBindings.Add(new RoutedCommandBinding(new RoutedCommand("DemoInlineControl", new KeyGesture(VirtualKey.F9)), (_, _) => InsertInlineButton()));
        _editor.DocumentChanged += (_, _) => { _folding?.Dispose(); _folding = null; _inline = null; };
    }
    private void ToggleFoldings()
    {
        _folding ??= FoldingManager.Install(_editor.TextArea);
        if (_folding.AllFoldings.Any(s => s.IsFolded))
        {
            foreach (var fold in _folding.AllFoldings) fold.IsFolded = false;
        }
        else
        {
            if (_editor.Text.TrimStart().StartsWith('<')) new XmlFoldingStrategy().UpdateFoldings(_folding, _editor.Document);
            else
            {
                // A brace demonstration, not a replacement for a language parser.
                var stack = new Stack<int>(); var folds = new List<NewFolding>();
                for (var offset = 0; offset < _editor.Document.TextLength; offset++)
                {
                    var c = _editor.Document.GetCharAt(offset);
                    if (c == '{') stack.Push(offset);
                    else if (c == '}' && stack.Count != 0)
                    {
                        var start = stack.Pop();
                        if (_editor.Document.GetLineByOffset(start) != _editor.Document.GetLineByOffset(offset))
                            folds.Add(new NewFolding(start, offset + 1) { Name = "{ ... }" });
                    }
                }
                _folding.UpdateFoldings(folds.OrderBy(f => f.StartOffset), -1);
            }
            _editor.CaretOffset = 0;
            foreach (var fold in _folding.AllFoldings) fold.IsFolded = true;
        }
        _editor.TextArea.TextView.Redraw(); QueueDiagnostics();
    }
    private double[] GetFoldLabel()
    {
        var fold = _folding?.AllFoldings.FirstOrDefault(f => f.IsFolded);
        if (fold == null) return Array.Empty<double>();
        var view = _editor.TextArea.TextView;
        var line = view.GetOrConstructVisualLine(_editor.Document.GetLineByOffset(fold.StartOffset));
        var column = line.GetVisualColumn(fold.StartOffset - line.StartOffset);
        var from = line.GetVisualPosition(column, VisualYPosition.LineTop);
        var to = line.GetVisualPosition(column + 1, VisualYPosition.LineBottom);
        var origin = view.TransformToVisual(this).TransformPoint(new Point());
        return new[] { origin.X + view.Viewport.GutterWidth + from.X - view.HorizontalOffset, origin.Y + from.Y - view.VerticalOffset, Math.Max(1, to.X - from.X), Math.Max(1, to.Y - from.Y) };
    }
    private void InsertInlineButton()
    {
        var view = _editor.TextArea.TextView;
        if (_inline != null) view.ElementGenerators.Remove(_inline);
        var button = new Button { Content = "Inline action", Width = 126, Height = 38, Padding = new Thickness(6) };
        button.Click += (_, _) => { _inlineClicks++; button.Content = $"Clicked {_inlineClicks}"; QueueDiagnostics(); };
        _inline = new InlineButtonGenerator(_editor.Document, _editor.Document.CreateAnchor(_editor.CaretOffset), button);
        view.ElementGenerators.Add(_inline); view.Redraw(); QueueDiagnostics();
    }
    private double[] GetInlineButton()
    {
        var button = _inline?.Button;
        if (button == null || VisualTreeHelper.GetParent(button) == null) return Array.Empty<double>();
        var origin = button.TransformToVisual(this).TransformPoint(new Point());
        return new[] { origin.X, origin.Y, button.ActualWidth, button.ActualHeight };
    }
    private sealed class InlineButtonGenerator : VisualLineElementGenerator
    {
        private readonly TextDocument _document; private readonly TextAnchor _anchor;
        internal Button Button { get; }
        internal InlineButtonGenerator(TextDocument document, TextAnchor anchor, Button button) { _document = document; _anchor = anchor; Button = button; }
        public override int GetFirstInterestedOffset(int startOffset) => ReferenceEquals(CurrentContext.Document, _document) && !_anchor.IsDeleted && _anchor.Offset >= startOffset ? _anchor.Offset : -1;
        public override VisualLineElement ConstructElement(int offset) => new InlineObjectElement(0, Button);
    }
}
