using System;
using System.Collections.Generic;
using System.Linq;
using UnoEdit.Rendering.Skia;
using UnoEdit.Utils;
namespace UnoEdit.Rendering;

public enum KnownLayer { Background, Selection, Text, Caret }
public enum LayerInsertionPosition { Below, Replace, Above }
public interface IBackgroundRenderer { KnownLayer Layer { get; } void Draw(TextView textView, DrawingContext drawingContext); }
public interface ITextViewConnect { void AddToTextView(TextView textView); void RemoveFromTextView(TextView textView); }

public partial class TextView
{
    private ObserveAddRemoveCollection<IBackgroundRenderer> _backgroundRenderers;
    public IList<IBackgroundRenderer> BackgroundRenderers => _backgroundRenderers;
    private void InitializeLayers()
    {
        _backgroundRenderers = new ObserveAddRemoveCollection<IBackgroundRenderer>(
            value => { if (value == null) throw new ArgumentNullException(nameof(value)); if (value is ITextViewConnect connected) connected.AddToTextView(this); Invalidate(); },
            value => { if (value is ITextViewConnect connected) connected.RemoveFromTextView(this); Invalidate(); });
        Viewport.RenderingLayer += OnRenderLayer;
    }
    private void OnRenderLayer(object sender, ViewportRenderEventArgs e)
    {
        var context = new DrawingContext(e.Canvas);
        foreach (var renderer in _backgroundRenderers.ToArray())
        {
            var desired = renderer.Layer <= KnownLayer.Selection ? ViewportRenderLayer.Background : renderer.Layer == KnownLayer.Text ? ViewportRenderLayer.Text : ViewportRenderLayer.Caret;
            if (desired != e.Layer) continue;
            var count = e.Canvas.Save();
            try { renderer.Draw(this, context); } finally { e.Canvas.RestoreToCount(count); }
        }
    }
    public void InvalidateLayer(KnownLayer layer) => Invalidate();
    private void DisposeLayers()
    { Viewport.RenderingLayer -= OnRenderLayer; _backgroundRenderers.Clear(); }
}
