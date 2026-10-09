using System;
using Microsoft.UI.Xaml;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using UnoEdit.Document;
using UnoEdit.Editing;
using UnoEdit.Rendering.Skia;
using Windows.Foundation;

namespace UnoEdit.Rendering;

/// <summary>
/// Native Uno drawing surface. It shares the document-backed editing session
/// with TextArea and draws directly into Uno's existing Skia canvas.
/// </summary>
public class TextView : SKCanvasElement, IDisposable
{
    private readonly bool _ownsSession;
    private bool _drawCaret;
    private bool _disposed;
    private double _lastHorizontalOffset;
    private double _lastVerticalOffset;

    public TextView() : this(new EditorSession(new TextDocument()), true) { }
    internal TextView(EditorSession session) : this(session, false) { }

    private TextView(EditorSession session, bool ownsSession)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        _ownsSession = ownsSession;
        Viewport = new DocumentViewport(session.Document);
        Viewport.Invalidated += OnViewportInvalidated;
        Session.Changed += OnSessionChanged;
        SizeChanged += OnSizeChanged;
    }

    internal EditorSession Session { get; }
    public DocumentViewport Viewport { get; }
    public TextDocument Document { get => Session.Document; set => Session.Document = value; }
    public double DefaultLineHeight => Viewport.DefaultLineHeight;
    public double VerticalOffset => Viewport.VerticalOffset;
    public double HorizontalOffset => Viewport.HorizontalOffset;
    public double ExtentHeight => Viewport.ExtentHeight;
    public double ExtentWidth => Viewport.ExtentWidth;
    public event EventHandler VisualLinesChanged;
    public event EventHandler ScrollOffsetChanged;
    public event EventHandler Rendered;

    public bool DrawCaret
    {
        get => _drawCaret;
        set { if (_disposed || _drawCaret == value) return; _drawCaret = value; Invalidate(); }
    }

    public void Redraw() { if (!_disposed) Invalidate(); }
    public void ScrollToHorizontalOffset(double offset) => Viewport.ScrollTo(offset, Viewport.VerticalOffset);
    public void ScrollToVerticalOffset(double offset) => Viewport.ScrollTo(Viewport.HorizontalOffset, offset);
    public void EnsureCaretVisible() => Viewport.EnsureCaretVisible(Session.CaretOffset);
    public int GetOffsetFromPoint(Point point) => Viewport.HitTest(point.X, point.Y);

    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        if (_disposed) return;
        Viewport.SetViewport(area.Width, area.Height);
        Viewport.Render(canvas, Session, DrawCaret);
        VisualLinesChanged?.Invoke(this, EventArgs.Empty);
        Rendered?.Invoke(this, EventArgs.Empty);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_disposed) Viewport.SetViewport(ActualWidth, ActualHeight);
    }

    private void OnSessionChanged(object sender, EventArgs e)
    {
        if (_disposed) return;
        if (!ReferenceEquals(Viewport.Document, Session.Document)) Viewport.Document = Session.Document;
        Invalidate();
    }

    private void OnViewportInvalidated(object sender, EventArgs e)
    {
        if (_disposed) return;
        Invalidate();
        if (_lastHorizontalOffset == Viewport.HorizontalOffset && _lastVerticalOffset == Viewport.VerticalOffset) return;
        _lastHorizontalOffset = Viewport.HorizontalOffset;
        _lastVerticalOffset = Viewport.VerticalOffset;
        ScrollOffsetChanged?.Invoke(this, EventArgs.Empty);
    }

    // Uno 6.7 FrameworkElement.Dispose is nonvirtual. Reimplement IDisposable
    // so both concrete and interface calls release the owned editor resources.
    public new void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SizeChanged -= OnSizeChanged;
        Session.Changed -= OnSessionChanged;
        Viewport.Invalidated -= OnViewportInvalidated;
        Viewport.Dispose();
        if (_ownsSession) Session.Dispose();
        VisualLinesChanged = null;
        ScrollOffsetChanged = null;
        Rendered = null;
        base.Dispose();
    }
}
