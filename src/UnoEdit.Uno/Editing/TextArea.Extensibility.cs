using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using UnoEdit.Document;
using UnoEdit.Indentation;
using UnoEdit.Utils;

namespace UnoEdit.Editing;

public sealed class TextInputEventArgs : EventArgs
{
    public string Text { get; set; }
    public bool Handled { get; set; }
    public TextInputEventArgs(string text) => Text = text ?? throw new ArgumentNullException(nameof(text));
}

public partial class TextArea : IRoutedCommandBindable, ITextEditorComponent
{
    private readonly List<TextAreaStackedInputHandler> _stackedHandlers = new();
    private ITextAreaInputHandler _activeInputHandler;
    private TextDocument _lastDocument;
    private TextEditorOptions _lastOptions;
    public IList<RoutedCommandBinding> CommandBindings { get; } = new List<RoutedCommandBinding>();
    public TextAreaDefaultInputHandler DefaultInputHandler { get; private set; }
    public ITextAreaInputHandler ActiveInputHandler
    {
        get => _activeInputHandler;
        set
        {
            if (value != null && value.TextArea != this) throw new ArgumentException("Handler belongs to another text area.", nameof(value));
            if (ReferenceEquals(value, _activeInputHandler)) return;
            _activeInputHandler?.Detach();
            _activeInputHandler = value;
            value?.Attach();
            ActiveInputHandlerChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public event EventHandler ActiveInputHandlerChanged;
    public event EventHandler<DocumentChangedEventArgs> DocumentChanged;
    public event PropertyChangedEventHandler OptionChanged;
    public event EventHandler<TextInputEventArgs> TextEntering;
    public event EventHandler<TextInputEventArgs> TextEntered;
    public bool OverstrikeMode { get; set; }
    public IIndentationStrategy IndentationStrategy { get; set; } = new DefaultIndentationStrategy();
    public IServiceContainer Services { get; } = new NativeServiceContainer();
    public object GetService(Type serviceType) => serviceType == typeof(TextArea) ? this : serviceType == typeof(TextDocument) ? Document : Services.GetService(serviceType);
    public IEnumerable<TextAreaStackedInputHandler> StackedInputHandlers => _stackedHandlers.ToArray();
    internal void MoveByVisualLine(double delta, bool extend) => MoveVisualLine(delta, extend);
    internal void ReportExtendedError(Exception error) => ReportInputError(error);
    public Selection Selection
    {
        get => Selection.Create(this, Session.AnchorOffset, Session.SelectionEndOffset);
        set
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (!ReferenceEquals(value.TextArea, this)) throw new ArgumentException("Selection belongs to another text area.", nameof(value));
            Session.SetSelection(Document.GetOffset(value.StartPosition.Line, value.StartPosition.Column),
                Document.GetOffset(value.EndPosition.Line, value.EndPosition.Column));
        }
    }
    public void PushStackedInputHandler(TextAreaStackedInputHandler handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        if (handler.TextArea != this || _stackedHandlers.Contains(handler)) throw new ArgumentException("Handler belongs to another text area or is already stacked.", nameof(handler));
        _stackedHandlers.Add(handler);
        try { handler.Attach(); }
        catch { _stackedHandlers.Remove(handler); throw; }
    }
    public void PopStackedInputHandler(TextAreaStackedInputHandler handler)
    {
        var index = _stackedHandlers.IndexOf(handler);
        if (index < 0) return;
        while (_stackedHandlers.Count > index)
        {
            var last = _stackedHandlers[^1]; _stackedHandlers.RemoveAt(_stackedHandlers.Count - 1); last.Detach();
        }
    }
    private int _allowCaretOutsideSelection;
    private bool _selectionValidationQueued;
    public IDisposable AllowCaretOutsideSelection()
    {
        if (!DispatcherQueue.HasThreadAccess) throw new InvalidOperationException("Selection scopes belong to the UI thread.");
        _allowCaretOutsideSelection++;
        return new CallbackOnDispose(() =>
        {
            if (!DispatcherQueue.HasThreadAccess) throw new InvalidOperationException("Selection scopes belong to the UI thread.");
            _allowCaretOutsideSelection--; RequestSelectionValidation();
        });
    }
    private void RequestSelectionValidation()
    {
        if (_disposed || _selectionValidationQueued || _allowCaretOutsideSelection != 0) return;
        _selectionValidationQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            _selectionValidationQueued = false;
            if (!_disposed && _allowCaretOutsideSelection == 0 && !Selection.IsEmpty && !Selection.Contains(Caret.Offset)) ClearSelection();
        })) _selectionValidationQueued = false;
    }
    private void OnExtendedSessionChanged(object sender, EventArgs e) => RequestSelectionValidation();
    public ISegment[] GetDeletableSegments(ISegment segment)
    {
        if (segment == null) throw new ArgumentNullException(nameof(segment));
        if (segment.Offset < 0 || segment.Length < 0 || (long)segment.Offset + segment.Length > Document.TextLength) throw new ArgumentOutOfRangeException(nameof(segment));
        if (IsReadOnly) return Array.Empty<ISegment>();
        var document = Document; var version = document.Version; var provider = ReadOnlySectionProvider;
        var result = new List<ISegment>(); var last = segment.Offset;
        var items = provider.GetDeletableSegments(segment) ?? throw new InvalidOperationException("The read-only provider returned null.");
        foreach (var item in items)
        {
            if (item == null || item.Offset < last || item.Length < 0 || (long)item.Offset + item.Length > segment.EndOffset)
                throw new InvalidOperationException("The read-only provider returned invalid or unordered segments.");
            result.Add(new SimpleSegment(item.Offset, item.Length)); last = item.EndOffset;
        }
        if (!ReferenceEquals(document, Document) || document.Version.CompareAge(version) != 0 || !ReferenceEquals(provider, ReadOnlySectionProvider))
            throw new InvalidOperationException("The read-only provider changed the document or protection during validation.");
        return result.ToArray();
    }
    internal bool CanReplaceWhole(ISegment segment)
    {
        if (IsReadOnly) return false;
        var document = Document; var version = document.Version; var provider = ReadOnlySectionProvider;
        var allowed = segment.Length == 0 ? provider.CanInsert(segment.Offset) : GetDeletableSegments(segment) is var ranges && ranges.Length == 1 && ranges[0].Offset == segment.Offset && ranges[0].Length == segment.Length;
        if (!ReferenceEquals(document, Document) || document.Version.CompareAge(version) != 0 || !ReferenceEquals(provider, ReadOnlySectionProvider))
            throw new InvalidOperationException("The read-only provider changed the document or protection during validation.");
        return allowed;
    }
    internal void InsertInputText(string text)
    {
        if (_disposed || IsReadOnly) return;
        var args = new TextInputEventArgs(text);
        TextEntering?.Invoke(this, args);
        if (args.Handled || _disposed || IsReadOnly) return;
        if (OverstrikeMode && Session.SelectionLength == 0 && args.Text.IndexOfAny(new[] { '\r', '\n' }) < 0)
        {
            var line = Document.GetLineByOffset(Caret.Offset);
            if (Caret.Offset < line.EndOffset) Session.MoveHorizontal(1, true);
        }
        Session.ReplaceSelection(args.Text);
        TextEntered?.Invoke(this, args);
        EnsureBrowserKeyboardFocus();
    }
#if __WASM__
    [System.Runtime.InteropServices.JavaScript.JSImport("globalThis.eval")]
    private static partial void InvokeBrowserFocusScript(string script);
#endif
    private void EnsureBrowserKeyboardFocus()
    {
#if __WASM__
        // Uno 6.7's canvas host retains managed keyboard focus while DOM focus
        // can remain on body. Its accessibility-entry shortcut then consumes
        // Tab before managed preview routing. Focus the existing canvas only
        // from an already focused editor; never hide the accessibility entry
        // or steal focus from an input, popup, or other browser element.
        if (!_focused || _disposed || !OperatingSystem.IsBrowser()) return;
        InvokeBrowserFocusScript("(() => { const a = document.activeElement; if (a && a !== document.body && a !== document.documentElement) return; const c = document.querySelector('canvas'); if (c) { if (!c.hasAttribute('tabindex')) c.tabIndex = -1; c.focus({ preventScroll: true }); } })()");
#endif
    }

    private void InitializeExtensibility()
    {
        _lastDocument = Document;
        _lastOptions = Options;
        _lastOptions.PropertyChanged += OnExtendedOptionChanged;
        DefaultInputHandler = new TextAreaDefaultInputHandler(this);
        ActiveInputHandler = DefaultInputHandler;
        Session.Changed += OnExtendedSessionChanged;
        PreviewKeyDown += OnExtendedPreviewKeyDown;
        KeyUp += OnExtendedKeyUp;
        GotFocus += OnExtendedFocus;
        Services.AddService(typeof(TextArea), this);
        Services.AddService(typeof(Rendering.TextView), TextView);
    }
    private void OnExtendedFocus(object sender, RoutedEventArgs e) { RoutedCommand.SetFocusedTarget(this); EnsureBrowserKeyboardFocus(); }
    private void OnExtendedOptionChanged(object sender, PropertyChangedEventArgs e) => OptionChanged?.Invoke(this, e);
    private void NotifyExtendedDocumentChanged()
    {
        if (ReferenceEquals(_lastDocument, Document)) return;
        if (_stackedHandlers.Count > 0) PopStackedInputHandler(_stackedHandlers[0]);
        var old = _lastDocument; _lastDocument = Document;
        DocumentChanged?.Invoke(this, new DocumentChangedEventArgs(old, Document));
    }
    private void NotifyExtendedOptionsChanged()
    {
        if (ReferenceEquals(_lastOptions, Options)) return;
        if (_lastOptions != null) _lastOptions.PropertyChanged -= OnExtendedOptionChanged;
        _lastOptions = Options;
        _lastOptions.PropertyChanged += OnExtendedOptionChanged;
        OptionChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
    private void OnExtendedPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_disposed || e.Handled || !IsEditorInputSource(e.OriginalSource)) return;
        foreach (var handler in _stackedHandlers.AsEnumerable().Reverse().ToArray())
            if (_stackedHandlers.Contains(handler) && !e.Handled) handler.OnPreviewKeyDown(e);
    }
    private bool DispatchExtendedKey(KeyRoutedEventArgs e)
    {
        if (_activeInputHandler is TextAreaInputHandler active && !e.Handled) active.HandleKey(e);
        foreach (var binding in CommandBindings.ToArray())
            if (!e.Handled && binding.Command.Gesture?.Matches(e) == true && binding.Command.CanExecute(null, this))
            { binding.Command.Execute(null, this); e.Handled = true; }
        return e.Handled || !ReferenceEquals(_activeInputHandler, DefaultInputHandler);
    }
    private void OnExtendedKeyUp(object sender, KeyRoutedEventArgs e)
    {
        foreach (var handler in _stackedHandlers.AsEnumerable().Reverse().ToArray())
            if (_stackedHandlers.Contains(handler) && !e.Handled) handler.OnPreviewKeyUp(e);
    }
    private void DisposeExtensibility()
    {
        if (_stackedHandlers.Count > 0) PopStackedInputHandler(_stackedHandlers[0]);
        ActiveInputHandler = null;
        Session.Changed -= OnExtendedSessionChanged;
        PreviewKeyDown -= OnExtendedPreviewKeyDown;
        KeyUp -= OnExtendedKeyUp;
        GotFocus -= OnExtendedFocus;
        if (_lastOptions != null) _lastOptions.PropertyChanged -= OnExtendedOptionChanged;
        DocumentChanged = null; OptionChanged = null; TextEntering = null; TextEntered = null; ActiveInputHandlerChanged = null;
        CommandBindings.Clear();
    }
}
