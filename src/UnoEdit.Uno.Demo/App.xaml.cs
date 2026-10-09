using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using UnoEdit.Document;
using Windows.Foundation;

namespace UnoEdit.Uno.Demo;

public partial class App : Application
{
    private Window _window;
    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new Window { Title = "UnoEdit — native Uno preview" };
        _window.Content = new DemoPage();
        _window.Activate();
    }
}

public sealed class DemoPage : Page
{
    private readonly TextEditor _editor = new() { ShowLineNumbers = true };
    private readonly TextBlock _status = new() { FontSize = 12, Margin = new Thickness(12, 5, 12, 5) };
    private readonly TextBox _search = new() { PlaceholderText = "Find text", Width = 160 };
    private readonly ComboBox _samples = new() { Width = 200, PlaceholderText = "Original sample files" };
    private readonly bool _smoke;
    private string _error;
    private bool _diagnosticsQueued;
    private bool _loaded;
    private Button _largeDocumentButton;

    public DemoPage()
    {
#if __WASM__
        _smoke = global::Uno.Foundation.WebAssemblyRuntime.InvokeJS("new URLSearchParams(location.search).get('smoke') === '1' ? 'yes' : 'no'") == "yes";
#endif
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new TextBlock
        {
            Text = "UnoEdit  /  native Uno Platform preview",
            FontSize = 22,
            Margin = new Thickness(12, 10, 12, 6)
        };
        root.Children.Add(heading);
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(12, 4, 12, 8) };
        var toolbarScroll = new ScrollViewer { Content = toolbar, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(toolbarScroll, 1);
        root.Children.Add(toolbarScroll);
        toolbar.Children.Add(_samples);
        AddButton(toolbar, "Undo", () => _editor.Undo());
        AddButton(toolbar, "Redo", () => _editor.Redo());
        _largeDocumentButton = AddButton(toolbar, "100,000 lines", LoadLargeDocument);
        AddButton(toolbar, "Clear", () => _editor.Clear());
        var wrap = new CheckBox { Content = "Wrap" };
        wrap.Checked += (_, _) => _editor.WordWrap = true;
        wrap.Unchecked += (_, _) => _editor.WordWrap = false;
        toolbar.Children.Add(wrap);
        var dark = new CheckBox { Content = "Dark" };
        dark.Checked += (_, _) => RequestedTheme = ElementTheme.Dark;
        dark.Unchecked += (_, _) => RequestedTheme = ElementTheme.Light;
        toolbar.Children.Add(dark);
        Grid.SetRow(_editor, 2);
        root.Children.Add(_editor);
        var findBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(12, 6, 12, 0) };
        findBar.Children.Add(_search);
        AddButton(findBar, "Find next", FindNext);
        var notice = new TextBlock { Text = "Preview: native editing and virtualization. Full completion, snippets, TextMate and IME parity are still in progress.", FontSize = 11, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 680 };
        findBar.Children.Add(notice);
        Grid.SetRow(findBar, 3);
        root.Children.Add(findBar);
        Grid.SetRow(_status, 4);
        root.Children.Add(_status);
        Content = root;
        AutomationProperties.SetName(_editor, "UnoEdit native editor");
        var resources = typeof(DemoPage).Assembly.GetManifestResourceNames().Where(n => n.StartsWith("Samples.", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        foreach (var resource in resources) _samples.Items.Add(resource.Substring("Samples.".Length));
        _samples.SelectionChanged += (_, _) =>
        {
            if (_samples.SelectedItem is not string name) return;
            using var stream = typeof(DemoPage).Assembly.GetManifestResourceStream("Samples." + name);
            if (stream != null) _editor.Load(stream);
        };
        _editor.TextChanged += (_, _) => UpdateStatus();
        _editor.SelectionChanged += (_, _) => UpdateStatus();
        _editor.TextArea.TextView.Rendered += (_, _) => QueueDiagnostics();
        _editor.InputError += (_, error) => { _error = error.Message; UpdateStatus(); };
        _editor.SearchRequested += (_, _) => _search.Focus(FocusState.Programmatic);
        _search.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { FindNext(); e.Handled = true; } };
        _editor.Text = _smoke ? string.Empty : "// UnoEdit native Uno Platform preview\n// Open an original sample file above, or try 100,000 lines.\n\npublic class Welcome\n{\n    public string Message => \"Hello, UnoEdit!\";\n}\n\n// Unicode: مرحبا  שלום  日本語  🙂  e\u0301\n";
        Loaded += (_, _) =>
        {
            _loaded = true;
            _editor.TextArea.Focus(FocusState.Programmatic);
            UpdateStatus();
            QueueDiagnostics();
        };
        Unloaded += (_, _) => _loaded = false;
        UpdateStatus();
    }

    private Button AddButton(Panel panel, string title, Action action)
    {
        var button = new Button { Content = title };
        AutomationProperties.SetName(button, title);
        button.Click += (_, _) =>
        {
            try { action(); _editor.TextArea.Focus(FocusState.Programmatic); }
            catch (Exception error) { _error = error.Message; UpdateStatus(); }
        };
        panel.Children.Add(button);
        return button;
    }

    private void LoadLargeDocument()
    {
        var text = new StringBuilder(4_000_000);
        for (var i = 1; i <= 100000; i++)
        {
            if (i != 1) text.Append('\n');
            text.Append("// Line ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(" — viewport-only text shaping");
        }
        _editor.Text = text.ToString();
    }

    private void FindNext()
    {
        var pattern = _search.Text;
        if (string.IsNullOrEmpty(pattern)) return;
        var start = Math.Min(_editor.Document.TextLength, _editor.SelectionStart + Math.Max(1, _editor.SelectionLength));
        var result = _editor.Document.IndexOf(pattern, start, _editor.Document.TextLength - start, StringComparison.OrdinalIgnoreCase);
        if (result < 0) result = _editor.Document.IndexOf(pattern, 0, start, StringComparison.OrdinalIgnoreCase);
        if (result >= 0) { _editor.Select(result, pattern.Length); _editor.TextArea.Caret.BringCaretToView(); }
        else _status.Text = "No match";
    }

    private void UpdateStatus()
    {
        if (_editor.Document == null) return;
        var caret = _editor.Document.GetLocation(_editor.CaretOffset);
        _status.Text = _error ?? $"Line {caret.Line:N0}, column {caret.Column:N0}   ·   {_editor.Document.LineCount:N0} lines   ·   {_editor.Document.TextLength:N0} UTF-16 units   ·   selection {_editor.SelectionLength:N0}";
        QueueDiagnostics();
    }

    private void QueueDiagnostics()
    {
        if (!_smoke || _diagnosticsQueued || !_loaded) return;
        _diagnosticsQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            _diagnosticsQueued = false;
            if (!_loaded) return;
#if __WASM__
            var view = _editor.TextArea.TextView;
            var origin = view.TransformToVisual(this).TransformPoint(new Point());
            var button = _largeDocumentButton.TransformToVisual(this).TransformPoint(new Point());
            var state = new BrowserState
            {
                Ready = view.Viewport.RenderCount > 0,
                TextPrefix = _editor.Document.GetText(0, Math.Min(2048, _editor.Document.TextLength)),
                TextLength = _editor.Document.TextLength,
                LineCount = _editor.Document.LineCount,
                CaretOffset = _editor.CaretOffset,
                SelectionLength = _editor.SelectionLength,
                RenderCount = view.Viewport.RenderCount,
                CachedLines = view.Viewport.CachedLineCount,
                VisibleLines = view.Viewport.LastVisibleLineCount,
                X = origin.X, Y = origin.Y, Width = view.ActualWidth, Height = view.ActualHeight,
                LargeButtonX = button.X + _largeDocumentButton.ActualWidth / 2,
                LargeButtonY = button.Y + _largeDocumentButton.ActualHeight / 2,
                Error = _error
            };
            global::Uno.Foundation.WebAssemblyRuntime.InvokeJS("globalThis.__unoEditTestState = " + JsonSerializer.Serialize(state, BrowserJsonContext.Default.BrowserState) + ";");
#endif
        })) _diagnosticsQueued = false;
    }
}

internal sealed class BrowserState
{
    public bool Ready { get; set; }
    public string TextPrefix { get; set; }
    public int TextLength { get; set; }
    public int LineCount { get; set; }
    public int CaretOffset { get; set; }
    public int SelectionLength { get; set; }
    public long RenderCount { get; set; }
    public int CachedLines { get; set; }
    public int VisibleLines { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double LargeButtonX { get; set; }
    public double LargeButtonY { get; set; }
    public string Error { get; set; }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(BrowserState))]
internal partial class BrowserJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }
