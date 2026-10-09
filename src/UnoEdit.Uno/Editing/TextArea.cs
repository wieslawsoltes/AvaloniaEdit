using System;
using System.Threading.Tasks;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using UnoEdit.Document;
using UnoEdit.Rendering;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace UnoEdit.Editing;

/// <summary>Native Uno input and scroll host over the shared document engine.</summary>
public class TextArea : UserControl
{
    private readonly ScrollBar _vertical = new() { Orientation = Orientation.Vertical, Width = 14 };
    private readonly ScrollBar _horizontal = new() { Orientation = Orientation.Horizontal, Height = 14 };
    private readonly DispatcherTimer _caretTimer = new() { Interval = TimeSpan.FromMilliseconds(530) };
    private bool _updatingScrollBars;
    private bool _scrollUpdateQueued;
    private bool _selecting;
    private bool _focused;
    private bool _disposed;
    private uint _capturedPointer;
    private char? _pendingHighSurrogate;

    public TextArea()
    {
        Session = new EditorSession(new TextDocument());
        TextView = new TextView(Session);
        Caret = new Caret(this);
        IsTabStop = true;
        AutomationProperties.SetName(this, "Code editor input");
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.Children.Add(TextView);
        Grid.SetColumn(_vertical, 1);
        layout.Children.Add(_vertical);
        Grid.SetRow(_horizontal, 1);
        layout.Children.Add(_horizontal);
        Content = layout;
        _vertical.ValueChanged += (_, e) => { if (!_updatingScrollBars && !_disposed) TextView.ScrollToVerticalOffset(e.NewValue); };
        _horizontal.ValueChanged += (_, e) => { if (!_updatingScrollBars && !_disposed) TextView.ScrollToHorizontalOffset(e.NewValue); };
        TextView.Rendered += (_, _) => QueueScrollBarUpdate();
        TextView.ScrollOffsetChanged += (_, _) => QueueScrollBarUpdate();
        Session.Changed += OnSessionChanged;
        KeyDown += OnEditorKeyDown;
        CharacterReceived += OnCharacterReceived;
        TextView.PointerPressed += OnPointerPressed;
        TextView.PointerMoved += OnPointerMoved;
        TextView.PointerReleased += OnPointerReleased;
        TextView.PointerCaptureLost += (_, _) => _selecting = false;
        TextView.PointerWheelChanged += OnPointerWheelChanged;
        GotFocus += (_, _) => { if (_disposed) return; _focused = true; TextView.DrawCaret = true; _caretTimer.Start(); };
        LostFocus += (_, _) => { _focused = false; TextView.DrawCaret = false; _caretTimer.Stop(); _pendingHighSurrogate = null; };
        Loaded += (_, _) => { if (_disposed) return; if (_focused) _caretTimer.Start(); TextView.Redraw(); };
        Unloaded += (_, _) => { _caretTimer.Stop(); _selecting = false; TextView.ReleasePointerCaptures(); };
        _caretTimer.Tick += (_, _) => TextView.DrawCaret = _focused && !TextView.DrawCaret;
        var menu = new MenuFlyout();
        AddMenuCommand(menu, "Undo", () => { Session.Undo(); return Task.CompletedTask; });
        AddMenuCommand(menu, "Redo", () => { Session.Redo(); return Task.CompletedTask; });
        menu.Items.Add(new MenuFlyoutSeparator());
        AddMenuCommand(menu, "Cut", () => { Cut(); return Task.CompletedTask; });
        AddMenuCommand(menu, "Copy", () => { Copy(); return Task.CompletedTask; });
        AddMenuCommand(menu, "Paste", PasteAsync);
        AddMenuCommand(menu, "Select All", () => { Session.SelectAll(); return Task.CompletedTask; });
        ContextFlyout = menu;
    }

    public EditorSession Session { get; }
    public TextView TextView { get; }
    public Caret Caret { get; }
    public TextDocument Document { get => Session.Document; set => Session.Document = value; }
    public TextEditorOptions Options
    {
        get => Session.Options;
        set
        {
            Session.Options = value ?? new TextEditorOptions();
            TextView.Viewport.Style = TextView.Viewport.Style with { TabSize = Math.Clamp(Session.Options.IndentationSize, 1, 256) };
        }
    }
    public bool IsReadOnly { get => Session.IsReadOnly; set => Session.IsReadOnly = value; }
    public event EventHandler SelectionChanged;
    public event EventHandler SearchRequested;
    public event EventHandler<Exception> InputError;

    public void Copy()
    {
        if (Session.SelectionLength == 0) return;
        var content = new DataPackage();
        content.SetText(Session.SelectedText);
        Clipboard.SetContent(content);
    }

    public void Cut()
    {
        if (IsReadOnly || Session.SelectionLength == 0) return;
        Copy(); // Never delete selected text if writing the clipboard fails.
        Session.ReplaceSelection(string.Empty);
    }

    public async Task PasteAsync()
    {
        if (IsReadOnly) return;
        var document = Document;
        var version = document.Version;
        var anchor = Session.AnchorOffset;
        var caret = Session.CaretOffset;
        var content = Clipboard.GetContent();
        if (!content.Contains(StandardDataFormats.Text)) return;
        var text = await content.GetTextAsync();
        if (_disposed || IsReadOnly) return;
        // Clipboard permission prompts may suspend the operation. Do not paste
        // into a different document or overwrite a newly changed selection.
        if (!ReferenceEquals(document, Document) || document.Version.CompareAge(version) != 0 || Session.AnchorOffset != anchor || Session.CaretOffset != caret)
            throw new InvalidOperationException("The document or selection changed while reading the clipboard. Retry paste.");
        Session.ReplaceSelection(text);
    }

    private static bool IsDown(VirtualKey key) => (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;
    private static bool CommandModifier => IsDown(VirtualKey.Control) || IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows);

    private async void OnEditorKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_disposed) return;
        var shift = IsDown(VirtualKey.Shift);
        var command = CommandModifier;
        if (IsDown(VirtualKey.Menu)) return; // Keep AltGr and native menu combinations intact.
        try
        {
            if (command)
            {
                switch (e.Key)
                {
                    case VirtualKey.A: Session.SelectAll(); e.Handled = true; return;
                    case VirtualKey.C: Copy(); e.Handled = true; return;
                    case VirtualKey.X: Cut(); e.Handled = true; return;
                    case VirtualKey.V: e.Handled = true; await PasteAsync(); return;
                    case VirtualKey.Z: if (shift) Session.Redo(); else Session.Undo(); e.Handled = true; return;
                    case VirtualKey.Y: Session.Redo(); e.Handled = true; return;
                    case VirtualKey.F: SearchRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; return;
                    case VirtualKey.Tab: return;
                }
            }
            e.Handled = true;
            switch (e.Key)
            {
                case VirtualKey.Left: Session.MoveHorizontal(-1, shift, command); break;
                case VirtualKey.Right: Session.MoveHorizontal(1, shift, command); break;
                case VirtualKey.Up: MoveVisualLine(-TextView.DefaultLineHeight, shift); break;
                case VirtualKey.Down: MoveVisualLine(TextView.DefaultLineHeight, shift); break;
                case VirtualKey.PageUp: MoveVisualLine(-Math.Max(TextView.DefaultLineHeight, TextView.ActualHeight), shift); break;
                case VirtualKey.PageDown: MoveVisualLine(Math.Max(TextView.DefaultLineHeight, TextView.ActualHeight), shift); break;
                case VirtualKey.Home: Session.MoveLineBoundary(false, shift, command); break;
                case VirtualKey.End: Session.MoveLineBoundary(true, shift, command); break;
                case VirtualKey.Back: Session.Delete(true, command); break;
                case VirtualKey.Delete: Session.Delete(false, command); break;
                case VirtualKey.Enter: Session.Enter(); break;
                case VirtualKey.Tab: Session.Indent(shift); break;
                case VirtualKey.Escape: Session.MoveTo(Session.CaretOffset); break;
                default: e.Handled = false; break;
            }
        }
        catch (Exception error) { ReportInputError(error); }
    }

    private void MoveVisualLine(double delta, bool extend)
    {
        var rectangle = TextView.Viewport.GetCaretRectangle(Session.CaretOffset);
        var offset = TextView.Viewport.HitTest(rectangle.Left, (rectangle.Top + rectangle.Bottom) / 2 + delta);
        Session.MoveTo(offset, extend);
    }

    private void OnCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        if (_disposed || IsReadOnly || CommandModifier && !IsDown(VirtualKey.Menu)) return;
        var code = (uint)e.Character;
        if (code < 32 || code == 127 || code > 0x10ffff) return;
        e.Handled = true;
        if (code >= 0xd800 && code <= 0xdbff) { _pendingHighSurrogate = (char)code; return; }
        string text;
        if (code >= 0xdc00 && code <= 0xdfff)
            text = _pendingHighSurrogate.HasValue ? new string(new[] { _pendingHighSurrogate.Value, (char)code }) : "\ufffd";
        else
            text = (_pendingHighSurrogate.HasValue ? "\ufffd" : string.Empty) + char.ConvertFromUtf32((int)code);
        _pendingHighSurrogate = null;
        Session.ReplaceSelection(text);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_disposed) return;
        var point = e.GetCurrentPoint(TextView);
        if (!point.Properties.IsLeftButtonPressed && e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse) return;
        Focus(FocusState.Pointer);
        _pendingHighSurrogate = null;
        Session.MoveTo(TextView.GetOffsetFromPoint(point.Position), (e.KeyModifiers & VirtualKeyModifiers.Shift) != 0);
        _selecting = TextView.CapturePointer(e.Pointer);
        _capturedPointer = e.Pointer.PointerId;
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_disposed || !_selecting || e.Pointer.PointerId != _capturedPointer) return;
        var point = e.GetCurrentPoint(TextView).Position;
        if (point.Y < 0) TextView.ScrollToVerticalOffset(TextView.VerticalOffset - TextView.DefaultLineHeight);
        else if (point.Y > TextView.ActualHeight) TextView.ScrollToVerticalOffset(TextView.VerticalOffset + TextView.DefaultLineHeight);
        Session.MoveTo(TextView.GetOffsetFromPoint(point), true);
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_disposed || !_selecting || e.Pointer.PointerId != _capturedPointer) return;
        _selecting = false;
        TextView.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_disposed) return;
        var properties = e.GetCurrentPoint(TextView).Properties;
        var delta = properties.MouseWheelDelta / 120.0 * TextView.DefaultLineHeight * 3;
        if (properties.IsHorizontalMouseWheel || (e.KeyModifiers & VirtualKeyModifiers.Shift) != 0)
            TextView.ScrollToHorizontalOffset(TextView.HorizontalOffset + delta);
        else
            TextView.ScrollToVerticalOffset(TextView.VerticalOffset - delta);
        e.Handled = true;
    }

    private void OnSessionChanged(object sender, EventArgs e)
    {
        if (_disposed) return;
        TextView.EnsureCaretVisible();
        TextView.DrawCaret = _focused;
        Caret.NotifyPositionChanged();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        QueueScrollBarUpdate();
    }

    private void QueueScrollBarUpdate()
    {
        if (_scrollUpdateQueued || _disposed) return;
        _scrollUpdateQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            _scrollUpdateQueued = false;
            if (_disposed) return;
            _updatingScrollBars = true;
            try
            {
                _vertical.Maximum = Math.Max(0, TextView.ExtentHeight - TextView.ActualHeight);
                _vertical.ViewportSize = TextView.ActualHeight;
                _vertical.LargeChange = TextView.ActualHeight;
                _vertical.SmallChange = TextView.DefaultLineHeight;
                _vertical.Value = TextView.VerticalOffset;
                _horizontal.Maximum = Math.Max(0, TextView.ExtentWidth - TextView.ActualWidth);
                _horizontal.ViewportSize = TextView.ActualWidth;
                _horizontal.LargeChange = TextView.ActualWidth;
                _horizontal.SmallChange = TextView.DefaultLineHeight;
                _horizontal.Value = TextView.HorizontalOffset;
                _horizontal.IsEnabled = !TextView.Viewport.Style.WordWrap;
            }
            finally { _updatingScrollBars = false; }
        })) _scrollUpdateQueued = false;
    }

    private void AddMenuCommand(MenuFlyout menu, string title, Func<Task> command)
    {
        var item = new MenuFlyoutItem { Text = title };
        item.Click += async (_, _) => { if (_disposed) return; try { await command(); } catch (Exception error) { ReportInputError(error); } };
        menu.Items.Add(item);
    }

    private void ReportInputError(Exception error)
    {
        Console.Error.WriteLine(error);
        InputError?.Invoke(this, error);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _caretTimer.Stop();
            TextView.ReleasePointerCaptures();
            KeyDown -= OnEditorKeyDown;
            CharacterReceived -= OnCharacterReceived;
            Session.Changed -= OnSessionChanged;
            TextView.Dispose();
            Session.Dispose();
            SelectionChanged = null;
            SearchRequested = null;
            InputError = null;
        }
        base.Dispose(disposing);
    }
}

/// <summary>UTF-16 caret facade backed by the shared native editing session.</summary>
public sealed class Caret
{
    private readonly TextArea _area;
    private TextLocation _lastLocation;
    internal Caret(TextArea area) { _area = area; _lastLocation = area.Document.GetLocation(0); }
    public int Offset { get => _area.Session.CaretOffset; set => _area.Session.MoveTo(value); }
    public int Line
    {
        get => _area.Document.GetLocation(Offset).Line;
        set => Offset = _area.Document.GetOffset(value, Column);
    }
    public int Column
    {
        get => _area.Document.GetLocation(Offset).Column;
        set => Offset = _area.Document.GetOffset(Line, value);
    }
    public TextViewPosition Position
    {
        get => new TextViewPosition(_area.Document.GetLocation(Offset));
        set => Offset = _area.Document.GetOffset(value.Line, value.Column);
    }
    public event EventHandler PositionChanged;
    public void BringCaretToView() => _area.TextView.EnsureCaretVisible();
    internal void NotifyPositionChanged()
    {
        var location = _area.Document.GetLocation(Offset);
        if (location == _lastLocation) return;
        _lastLocation = location;
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }
}
