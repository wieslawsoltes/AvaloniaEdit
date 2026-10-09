using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using UnoEdit.Editing;
using UnoEdit.Rendering;
using Windows.Foundation;

namespace UnoEdit.Folding;

/// <summary>Viewport-only native folding margin; pointer toggles use the same drawn rectangles.</summary>
public class FoldingMargin : AbstractMargin, IDisposable
{
    private sealed class Surface : SKCanvasElement
    {
        internal FoldingMargin Owner;
        protected override void RenderOverride(SKCanvas canvas, Size area) => Owner.Paint(canvas, area);
    }
    private readonly Surface _surface;
    private readonly List<(FoldingSection Section, Rect Box)> _markers = new();
    private FoldingManager _manager;
    private FoldingSection _hover;
    private bool _disposed;
    public FoldingMargin() { Width = 18; _surface = new Surface { Owner = this }; Children.Add(_surface); PointerPressed += Pressed; PointerMoved += Moved; PointerExited += (_, _) => { _hover = null; InvalidateVisual(); }; }
    public FoldingManager FoldingManager { get => _manager; set { _manager = value; InvalidateVisual(); } }
    private static DependencyProperty BrushProperty(string name, Windows.UI.Color color) => DependencyProperty.RegisterAttached(name, typeof(Brush), typeof(FoldingMargin), new PropertyMetadata(new SolidColorBrush(color), (sender, _) => { if (sender is FoldingMargin margin) margin.InvalidateVisual(); }));
    public static readonly DependencyProperty FoldingMarkerBrushProperty = BrushProperty(nameof(FoldingMarkerBrush), Microsoft.UI.Colors.Gray);
    public static readonly DependencyProperty FoldingMarkerBackgroundBrushProperty = BrushProperty(nameof(FoldingMarkerBackgroundBrush), Microsoft.UI.Colors.White);
    public static readonly DependencyProperty SelectedFoldingMarkerBrushProperty = BrushProperty(nameof(SelectedFoldingMarkerBrush), Microsoft.UI.Colors.Black);
    public static readonly DependencyProperty SelectedFoldingMarkerBackgroundBrushProperty = BrushProperty(nameof(SelectedFoldingMarkerBackgroundBrush), Microsoft.UI.Colors.White);
    public Brush FoldingMarkerBrush { get => (Brush)GetValue(FoldingMarkerBrushProperty); set => SetValue(FoldingMarkerBrushProperty, value); }
    public Brush FoldingMarkerBackgroundBrush { get => (Brush)GetValue(FoldingMarkerBackgroundBrushProperty); set => SetValue(FoldingMarkerBackgroundBrushProperty, value); }
    public Brush SelectedFoldingMarkerBrush { get => (Brush)GetValue(SelectedFoldingMarkerBrushProperty); set => SetValue(SelectedFoldingMarkerBrushProperty, value); }
    public Brush SelectedFoldingMarkerBackgroundBrush { get => (Brush)GetValue(SelectedFoldingMarkerBackgroundBrushProperty); set => SetValue(SelectedFoldingMarkerBackgroundBrushProperty, value); }
    public override void InvalidateVisual() { if (!_disposed) _surface?.Invalidate(); }
    private void Paint(SKCanvas canvas, Size area)
    {
        _markers.Clear();
        if (_disposed || TextView == null || _manager == null) return;
        var context = new DrawingContext(canvas);
        foreach (var line in TextView.VisualLines)
        {
            var section = _manager.GetNextFolding(line.FirstDocumentLine.Offset);
            if (section == null || section.StartOffset > line.LastDocumentLine.EndOffset) continue;
            var y = line.GetVisualPosition(line.GetVisualColumn(section.StartOffset - line.StartOffset), VisualYPosition.LineMiddle).Y - TextView.VerticalOffset;
            var box = new Rect(Math.Max(0, (area.Width - 11) / 2), y - 5.5, 11, 11);
            _markers.Add((section, box));
            var pen = new Pen(ReferenceEquals(_hover, section) ? SelectedFoldingMarkerBrush : FoldingMarkerBrush);
            context.DrawRectangle(ReferenceEquals(_hover, section) ? SelectedFoldingMarkerBackgroundBrush : FoldingMarkerBackgroundBrush, pen, box);
            context.DrawLine(pen, new Point(box.X + 2, y), new Point(box.Right - 2, y));
            if (section.IsFolded) context.DrawLine(pen, new Point(box.X + 5.5, box.Y + 2), new Point(box.X + 5.5, box.Bottom - 2));
        }
    }
    private FoldingSection Hit(Point point) { foreach (var marker in _markers) if (marker.Box.Contains(point)) return marker.Section; return null; }
    private void Pressed(object sender, PointerRoutedEventArgs e)
    { var section = Hit(e.GetCurrentPoint(this).Position); if (section == null) return; section.IsFolded = !section.IsFolded; e.Handled = true; InvalidateVisual(); }
    private void Moved(object sender, PointerRoutedEventArgs e) { var section = Hit(e.GetCurrentPoint(this).Position); if (section == _hover) return; _hover = section; InvalidateVisual(); }
    public new void Dispose() { if (_disposed) return; TextView = null; _disposed = true; PointerPressed -= Pressed; PointerMoved -= Moved; _markers.Clear(); _manager = null; base.Dispose(); }
}
