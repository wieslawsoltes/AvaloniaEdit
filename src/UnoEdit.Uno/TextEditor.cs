using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using UnoEdit.Document;
using UnoEdit.Editing;
using Windows.Foundation;

namespace UnoEdit;

/// <summary>
/// Native Uno implementation of the editor's document and editing surface.
/// The migration baseline remains separately buildable until the remaining
/// completion, snippet, folding-adornment and extension APIs are ported.
/// </summary>
public partial class TextEditor : UserControl, IDisposable
{
    private readonly long[] _appearanceCallbacks;
    private readonly DependencyProperty[] _appearanceProperties;
    private bool _disposed;

    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document), typeof(TextDocument), typeof(TextEditor), new PropertyMetadata(null, (sender, args) => ((TextEditor)sender).ChangeDocument((TextDocument)args.OldValue, (TextDocument)args.NewValue)));
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
        nameof(Options), typeof(TextEditorOptions), typeof(TextEditor), new PropertyMetadata(null, (sender, args) => ((TextEditor)sender).ChangeOptions((TextEditorOptions)args.OldValue, (TextEditorOptions)args.NewValue)));
    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly), typeof(bool), typeof(TextEditor), new PropertyMetadata(false, (sender, _) => ((TextEditor)sender).UpdateReadOnly()));
    public static readonly DependencyProperty ShowLineNumbersProperty = DependencyProperty.Register(
        nameof(ShowLineNumbers), typeof(bool), typeof(TextEditor), new PropertyMetadata(false, (sender, _) => ((TextEditor)sender).RefreshAppearance()));
    public static readonly DependencyProperty WordWrapProperty = DependencyProperty.Register(
        nameof(WordWrap), typeof(bool), typeof(TextEditor), new PropertyMetadata(false, (sender, _) => ((TextEditor)sender).RefreshAppearance()));

    public TextEditor()
    {
        TextArea = new TextArea();
        Content = TextArea;
        IsTabStop = false;
        FontSize = 14;
        FontFamily = new FontFamily("monospace");
        TextArea.SelectionChanged += (_, _) => SelectionChanged?.Invoke(this, EventArgs.Empty);
        TextArea.SearchRequested += (_, _) => SearchRequested?.Invoke(this, EventArgs.Empty);
        TextArea.InputError += (_, error) => InputError?.Invoke(this, error);
        _appearanceProperties = new[] { FontSizeProperty, FontFamilyProperty, ForegroundProperty, BackgroundProperty };
        _appearanceCallbacks = new long[_appearanceProperties.Length];
        for (var i = 0; i < _appearanceProperties.Length; i++)
            _appearanceCallbacks[i] = RegisterPropertyChangedCallback(_appearanceProperties[i], (_, _) => RefreshAppearance());
        ActualThemeChanged += OnActualThemeChanged;
        Options = new TextEditorOptions();
        Document = new TextDocument();
        InitializeSearchPanel();
        RefreshAppearance();
    }

    public TextArea TextArea { get; }
    public TextDocument Document { get => (TextDocument)GetValue(DocumentProperty); set => SetValue(DocumentProperty, value); }
    public TextEditorOptions Options { get => (TextEditorOptions)GetValue(OptionsProperty); set => SetValue(OptionsProperty, value); }
    public bool IsReadOnly { get => (bool)GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }
    public bool ShowLineNumbers { get => (bool)GetValue(ShowLineNumbersProperty); set => SetValue(ShowLineNumbersProperty, value); }
    public bool WordWrap { get => (bool)GetValue(WordWrapProperty); set => SetValue(WordWrapProperty, value); }
    public Encoding Encoding { get; set; } = new UTF8Encoding(false);

    public string Text
    {
        get => Document?.Text ?? string.Empty;
        set
        {
            var document = GetDocument();
            document.Text = value ?? string.Empty;
            CaretOffset = 0;
            document.UndoStack.ClearAll();
        }
    }

    public string SelectedText { get => TextArea.Session.SelectedText; set { GetDocument(); TextArea.Session.ReplaceSelection(value ?? string.Empty); } }
    public int SelectionStart
    {
        get => TextArea.Session.SelectionStart;
        set
        {
            var document = GetDocument();
            if (value < 0 || value > document.TextLength) throw new ArgumentOutOfRangeException(nameof(value));
            TextArea.Session.Select(value, Math.Min(SelectionLength, document.TextLength - value));
        }
    }
    public int SelectionLength { get => TextArea.Session.SelectionLength; set => TextArea.Session.Select(SelectionStart, value); }
    public int CaretOffset { get => TextArea.Caret.Offset; set { GetDocument(); TextArea.Caret.Offset = value; } }
    public int LineCount => Document?.LineCount ?? 1;
    public bool CanUndo => Document?.UndoStack.CanUndo ?? false;
    public bool CanRedo => Document?.UndoStack.CanRedo ?? false;
    public double VerticalOffset => TextArea.TextView.VerticalOffset;
    public double HorizontalOffset => TextArea.TextView.HorizontalOffset;
    public event EventHandler TextChanged;
    public event EventHandler<DocumentChangedEventArgs> DocumentChanged;
    public event PropertyChangedEventHandler OptionChanged;
    public event EventHandler SelectionChanged;
    public event EventHandler SearchRequested;
    public event EventHandler<Exception> InputError;

    public void Select(int start, int length) { GetDocument(); TextArea.Session.Select(start, length); }
    public void SelectAll() { GetDocument(); TextArea.Session.SelectAll(); }
    public void Clear() => Text = string.Empty;
    public void AppendText(string text) => GetDocument().Insert(GetDocument().TextLength, text ?? string.Empty);
    public void Undo() => TextArea.Session.Undo();
    public void Redo() => TextArea.Session.Redo();
    public void Copy() => TextArea.Copy();
    public void Cut() => TextArea.Cut();
    public Task PasteAsync() => TextArea.PasteAsync();
    public async void Paste()
    {
        try { await PasteAsync(); }
        catch (Exception error) { Console.Error.WriteLine(error); InputError?.Invoke(this, error); }
    }
    public void Delete() => TextArea.Session.Delete(false);
    public void ScrollToHorizontalOffset(double offset) => TextArea.TextView.ScrollToHorizontalOffset(offset);
    public void ScrollToVerticalOffset(double offset) => TextArea.TextView.ScrollToVerticalOffset(offset);
    public void ScrollToLine(int line) => TextArea.TextView.Viewport.ScrollToLine(line);
    public void ScrollTo(int line, int column) => TextArea.TextView.Viewport.EnsureCaretVisible(GetDocument().GetOffset(line, column));
    public void ScrollToHome() => ScrollToVerticalOffset(0);
    public void ScrollToEnd() => ScrollToVerticalOffset(TextArea.TextView.ExtentHeight);

    public TextViewPosition? GetPositionFromPoint(Point point)
    {
        if (Document == null) return null;
        var position = TransformToVisual(TextArea.TextView).TransformPoint(point);
        return new TextViewPosition(Document.GetLocation(TextArea.TextView.GetOffsetFromPoint(position)));
    }

    public void Load(Stream stream)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        using var reader = new StreamReader(stream, Encoding ?? System.Text.Encoding.UTF8, true, 4096, true);
        Text = reader.ReadToEnd();
        Encoding = reader.CurrentEncoding;
    }

    public void Save(Stream stream)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        using var writer = new StreamWriter(stream, Encoding ?? new UTF8Encoding(false), 4096, true);
        GetDocument().WriteTextTo(writer);
    }

    public void Load(string fileName) { using var stream = File.OpenRead(fileName); Load(stream); }
    public void Save(string fileName) { using var stream = File.Create(fileName); Save(stream); }
    protected virtual void OnTextChanged(EventArgs args) => TextChanged?.Invoke(this, args);
    protected virtual void OnOptionChanged(PropertyChangedEventArgs args) => OptionChanged?.Invoke(this, args);

    private TextDocument GetDocument()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TextEditor));
        return Document ?? throw new InvalidOperationException("No document is assigned to the editor.");
    }

    private void UpdateReadOnly()
    {
        if (!_disposed && TextArea != null) TextArea.IsReadOnly = Document == null || IsReadOnly;
    }

    private void ChangeDocument(TextDocument oldDocument, TextDocument newDocument)
    {
        if (_disposed) return;
        if (oldDocument != null) TextDocumentWeakEventManager.TextChanged.RemoveHandler(oldDocument, OnDocumentTextChanged);
        TextArea.Document = newDocument ?? new TextDocument();
        UpdateReadOnly();
        if (newDocument != null) TextDocumentWeakEventManager.TextChanged.AddHandler(newDocument, OnDocumentTextChanged);
        ResetSyntaxHighlighting();
        DocumentChanged?.Invoke(this, new DocumentChangedEventArgs(oldDocument, newDocument));
        OnTextChanged(EventArgs.Empty);
    }

    private void ChangeOptions(TextEditorOptions oldOptions, TextEditorOptions newOptions)
    {
        if (_disposed) return;
        if (oldOptions != null) oldOptions.PropertyChanged -= OnOptionsChanged;
        TextArea.Options = newOptions;
        if (newOptions != null) newOptions.PropertyChanged += OnOptionsChanged;
        RefreshAppearance();
        OnOptionChanged(new PropertyChangedEventArgs(null));
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args) => RefreshAppearance();
    private void OnDocumentTextChanged(object sender, EventArgs e) => OnTextChanged(e);
    private void OnOptionsChanged(object sender, PropertyChangedEventArgs e)
    {
        if (_disposed) return;
        TextArea.Options = Options;
        RefreshAppearance();
        OnOptionChanged(e);
    }

    private void RefreshAppearance()
    {
        if (TextArea == null || _disposed) return;
        var dark = ActualTheme == ElementTheme.Dark;
        var foreground = Foreground is SolidColorBrush fg ? new SKColor(fg.Color.R, fg.Color.G, fg.Color.B, fg.Color.A) : dark ? SKColors.White : SKColors.Black;
        var background = Background is SolidColorBrush bg ? new SKColor(bg.Color.R, bg.Color.G, bg.Color.B, bg.Color.A) : dark ? new SKColor(30, 30, 30) : SKColors.White;
        TextArea.TextView.Viewport.Style = TextArea.TextView.Viewport.Style with
        {
            FontSize = (float)FontSize,
            FontFamily = FontFamily?.Source ?? "monospace",
            TabSize = Math.Clamp(Options?.IndentationSize ?? 4, 1, 256),
            WordWrap = WordWrap,
            Foreground = foreground,
            Background = background,
            LineNumber = dark ? new SKColor(155, 155, 155) : new SKColor(100, 100, 100)
        };
        TextArea.TextView.Viewport.ShowLineNumbers = ShowLineNumbers;
    }

    // Reimplement IDisposable because Uno 6.7's base disposal is nonvirtual.
    // Both TextEditor.Dispose() and ((IDisposable)editor).Dispose() reach here.
    public new void Dispose()
    {
        if (_disposed) return;
        DisposeSearchPanel();
        _disposed = true;
        ActualThemeChanged -= OnActualThemeChanged;
        if (Document != null) TextDocumentWeakEventManager.TextChanged.RemoveHandler(Document, OnDocumentTextChanged);
        if (Options != null) Options.PropertyChanged -= OnOptionsChanged;
        for (var i = 0; i < _appearanceProperties.Length; i++)
            UnregisterPropertyChangedCallback(_appearanceProperties[i], _appearanceCallbacks[i]);
        DisposeSyntaxHighlighting();
        TextArea.Dispose();
        TextChanged = null;
        DocumentChanged = null;
        SelectionChanged = null;
        OptionChanged = null;
        SearchRequested = null;
        InputError = null;
        base.Dispose();
    }
}
