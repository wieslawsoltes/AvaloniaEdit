using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using UnoEdit.Document;
using UnoEdit.Editing;
using Windows.Foundation;
using Windows.System;

namespace UnoEdit.CodeCompletion;

/// <summary>A caret-anchored native popup with a stacked, non-focus-stealing input handler.</summary>
public class CompletionWindowBase : Popup, IDisposable
{
    private readonly InputHandler _handler;
    private TextDocument _document;
    private bool _attached, _disposed;
    private int _startOffset, _endOffset;
    public CompletionWindowBase(TextArea textArea)
    {
        TextArea = textArea ?? throw new ArgumentNullException(nameof(textArea));
        _handler = new InputHandler(this);
        _startOffset = _endOffset = TextArea.Caret.Offset;
        IsLightDismissEnabled = false;
        Closed += OnNativeClosed;
        KeyDown += OnPopupKeyDown;
    }
    public TextArea TextArea { get; }
    public int StartOffset { get => _startOffset; set { ValidateOffset(value); _startOffset = value; } }
    public int EndOffset { get => _endOffset; set { ValidateOffset(value); _endOffset = value; } }
    public bool ExpectInsertionBeforeStart { get; set; }
    protected bool IsUp { get; private set; }
    protected virtual bool CloseOnFocusLost => true;
    private void ValidateOffset(int value) { if (value < 0 || value > TextArea.Document.TextLength) throw new ArgumentOutOfRangeException(nameof(value)); }
    public void Show()
    {
        if (_disposed) throw new ObjectDisposedException(GetType().Name);
        if (_attached) { IsOpen = true; UpdatePosition(); return; }
        if (EndOffset < StartOffset) throw new InvalidOperationException("Completion range is reversed.");
        XamlRoot = TextArea.XamlRoot ?? throw new InvalidOperationException("The text area must be attached to a native XamlRoot before showing completion.");
        _document = TextArea.Document;
        _attached = true;
        _document.Changing += OnDocumentChanging;
        TextArea.DocumentChanged += OnDocumentReplaced;
        TextArea.Session.Changed += OnSessionChanged;
        TextArea.LostFocus += OnFocusLost;
        TextArea.Unloaded += OnUnloaded;
        TextArea.TextView.ScrollOffsetChanged += OnViewportChanged;
        TextArea.TextView.SizeChanged += OnViewportSizeChanged;
        TextArea.PushStackedInputHandler(_handler);
        OnOpened();
        if (!_attached) return;
        UpdatePosition();
        IsOpen = true;
    }
    protected virtual void OnOpened() { }
    public void Hide()
    {
        var notify = _attached;
        DetachEvents();
        if (IsOpen) IsOpen = false;
        else if (notify) OnClosed();
    }
    private void OnNativeClosed(object sender, object args) { DetachEvents(); OnClosed(); }
    protected virtual void OnClosed() { }
    protected virtual void DetachEvents()
    {
        if (!_attached) return;
        _attached = false;
        _document.Changing -= OnDocumentChanging;
        _document = null;
        TextArea.DocumentChanged -= OnDocumentReplaced;
        TextArea.Session.Changed -= OnSessionChanged;
        TextArea.LostFocus -= OnFocusLost;
        TextArea.Unloaded -= OnUnloaded;
        TextArea.TextView.ScrollOffsetChanged -= OnViewportChanged;
        TextArea.TextView.SizeChanged -= OnViewportSizeChanged;
        TextArea.PopStackedInputHandler(_handler);
    }
    private void OnDocumentChanging(object sender, DocumentChangeEventArgs e)
    {
        if (e.RemovalLength > 0 && e.Offset < _startOffset && e.Offset + e.RemovalLength > _startOffset) { Hide(); return; }
        var before = ExpectInsertionBeforeStart && e.Offset == _startOffset && e.RemovalLength == 0;
        _startOffset = e.GetNewOffset(_startOffset, before ? AnchorMovementType.AfterInsertion : AnchorMovementType.BeforeInsertion);
        _endOffset = e.GetNewOffset(_endOffset, AnchorMovementType.AfterInsertion);
        ExpectInsertionBeforeStart = false;
    }
    protected virtual void OnCaretChanged() { }
    private void OnSessionChanged(object sender, EventArgs e) { if (!_attached) return; OnCaretChanged(); if (_attached) UpdatePosition(); }
    private void OnDocumentReplaced(object sender, DocumentChangedEventArgs e) => Hide();
    private void OnUnloaded(object sender, RoutedEventArgs e) => Hide();
    private void OnViewportChanged(object sender, EventArgs e) { if (_attached) UpdatePosition(); }
    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e) { if (_attached) UpdatePosition(); }
    private void OnFocusLost(object sender, RoutedEventArgs e)
    {
        TextArea.DispatcherQueue.TryEnqueue(() =>
        {
            if (!_attached || !CloseOnFocusLost || TextArea.XamlRoot == null) return;
            var focused = FocusManager.GetFocusedElement(TextArea.XamlRoot) as DependencyObject;
            for (var current = focused; current != null; current = VisualTreeHelper.GetParent(current))
                if (ReferenceEquals(current, TextArea) || ReferenceEquals(current, Child)) return;
            Hide();
        });
    }
    protected virtual void ActivateParentWindow() => TextArea.Focus(FocusState.Programmatic);
    protected void SetPosition(TextViewPosition position)
    { _startOffset = TextArea.Document.GetOffset(position.Line, position.Column); UpdatePosition(); }
    protected void UpdatePosition()
    {
        if (XamlRoot?.Content == null || Child == null) return;
        var view = TextArea.TextView;
        var rectangle = view.Viewport.GetCaretRectangle(Math.Clamp(_startOffset, 0, TextArea.Document.TextLength));
        var origin = view.TransformToVisual(XamlRoot.Content).TransformPoint(new Point(rectangle.Left, rectangle.Bottom));
        Child.Measure(new Size(Math.Min(MaxWidth, XamlRoot.Size.Width), Math.Min(MaxHeight, XamlRoot.Size.Height)));
        var width = Math.Max(MinWidth, Math.Min(MaxWidth, Child.DesiredSize.Width));
        var height = Math.Max(MinHeight, Math.Min(MaxHeight, Child.DesiredSize.Height));
        HorizontalOffset = Math.Clamp(origin.X, 0, Math.Max(0, XamlRoot.Size.Width - width));
        IsUp = origin.Y + height > XamlRoot.Size.Height && origin.Y - rectangle.Height >= height;
        VerticalOffset = Math.Max(0, IsUp ? origin.Y - rectangle.Height - height : Math.Min(origin.Y, XamlRoot.Size.Height - height));
    }
    private void OnPopupKeyDown(object sender, KeyRoutedEventArgs e) => OnKeyDown(e);
    protected virtual void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (!e.Handled && e.Key == VirtualKey.Escape) { e.Handled = true; Hide(); }
    }
    private sealed class InputHandler : TextAreaStackedInputHandler
    {
        private readonly CompletionWindowBase _owner;
        internal InputHandler(CompletionWindowBase owner) : base(owner.TextArea) => _owner = owner;
        public override void OnPreviewKeyDown(KeyRoutedEventArgs e) => _owner.OnKeyDown(e);
        public override void Detach() { if (_owner._attached) _owner.Hide(); }
    }
    public new void Dispose()
    {
        if (_disposed) return;
        Hide(); _disposed = true; Closed -= OnNativeClosed; KeyDown -= OnPopupKeyDown;
        if (Child is IDisposable owned) owned.Dispose(); Child = null;
        base.Dispose();
    }
}
