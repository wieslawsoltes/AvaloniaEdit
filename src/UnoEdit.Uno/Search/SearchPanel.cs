using System;
using System.Collections.Generic;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using UnoEdit.Document;
using UnoEdit.Rendering.Skia;
using Windows.System;
using Windows.UI.Core;

namespace UnoEdit.Search;

/// <summary>
/// Native templated search/replace overlay. Uses the original search engine,
/// cached search state and a marker layer independent of syntax highlighting.
/// Install/Uninstall and Open/Close manage document and template subscriptions.
/// </summary>
public class SearchPanel : Control, IDisposable
{
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly List<Action> _detachTemplate = new();
    private SearchSession _session;
    private SearchResultMarkerSource _markers;
    private TextEditor _editor;
    private TextBox _searchBox;
    private TextBox _replaceBox;
    private CheckBox _caseBox, _wordsBox, _regexBox, _replaceModeBox;
    private FrameworkElement _replaceRow;
    private bool _syncing;
    private bool _disposed;
    private long _readOnlyToken;

    public static readonly DependencyProperty SearchPatternProperty = DependencyProperty.Register(nameof(SearchPattern), typeof(string), typeof(SearchPanel), new PropertyMetadata(string.Empty, QueryChanged));
    public static readonly DependencyProperty MatchCaseProperty = DependencyProperty.Register(nameof(MatchCase), typeof(bool), typeof(SearchPanel), new PropertyMetadata(false, QueryChanged));
    public static readonly DependencyProperty WholeWordsProperty = DependencyProperty.Register(nameof(WholeWords), typeof(bool), typeof(SearchPanel), new PropertyMetadata(false, QueryChanged));
    public static readonly DependencyProperty UseRegexProperty = DependencyProperty.Register(nameof(UseRegex), typeof(bool), typeof(SearchPanel), new PropertyMetadata(false, QueryChanged));
    public static readonly DependencyProperty ReplacePatternProperty = DependencyProperty.Register(nameof(ReplacePattern), typeof(string), typeof(SearchPanel), new PropertyMetadata(string.Empty, (sender, _) => ((SearchPanel)sender).SyncTemplate()));
    public static readonly DependencyProperty IsReplaceModeProperty = DependencyProperty.Register(nameof(IsReplaceMode), typeof(bool), typeof(SearchPanel), new PropertyMetadata(false, (sender, _) => ((SearchPanel)sender).UpdateReplaceMode()));
    public static readonly DependencyProperty StatusTextProperty = DependencyProperty.Register(nameof(StatusText), typeof(string), typeof(SearchPanel), new PropertyMetadata(string.Empty));

    public SearchPanel()
    {
        DefaultStyleKey = typeof(SearchPanel);
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(6);
        Width = 440;
        IsTabStop = false;
        TabFocusNavigation = KeyboardNavigationMode.Cycle;
        _debounce.Tick += OnDebounce;
        KeyDown += OnPanelKeyDown;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public string SearchPattern { get => (string)GetValue(SearchPatternProperty); set => SetValue(SearchPatternProperty, value ?? string.Empty); }
    public bool MatchCase { get => (bool)GetValue(MatchCaseProperty); set => SetValue(MatchCaseProperty, value); }
    public bool WholeWords { get => (bool)GetValue(WholeWordsProperty); set => SetValue(WholeWordsProperty, value); }
    public bool UseRegex { get => (bool)GetValue(UseRegexProperty); set => SetValue(UseRegexProperty, value); }
    public string ReplacePattern { get => (string)GetValue(ReplacePatternProperty); set => SetValue(ReplacePatternProperty, value ?? string.Empty); }
    public bool IsReplaceMode { get => (bool)GetValue(IsReplaceModeProperty); set => SetValue(IsReplaceModeProperty, value); }
    public string StatusText { get => (string)GetValue(StatusTextProperty); private set => SetValue(StatusTextProperty, value); }
    public TextEditor TextEditor => _editor;
    public bool IsClosed { get; private set; } = true;
    public bool IsOpened => !IsClosed;
    public bool IsInstalled => _editor != null && !_disposed;
    public SearchSession Session => _session;
    public Exception LastError { get; private set; }
    public int ResultCount { get; private set; }
    public int LastReplaceCount => _session?.LastReplaceCount ?? 0;
    public int LastSkippedCount => _session?.LastSkippedCount ?? 0;
    public event EventHandler<SearchOptionsChangedEventArgs> SearchOptionsChanged;

    public static SearchPanel Install(TextEditor editor)
    {
        if (editor == null) throw new ArgumentNullException(nameof(editor));
        if (editor.ExistingSearchPanel != null) return editor.ExistingSearchPanel;
        var panel = new SearchPanel();
        panel.Attach(editor);
        editor.ExistingSearchPanel = panel;
        return panel;
    }

    private void Attach(TextEditor editor)
    {
        _editor = editor;
        _session = new SearchSession(editor.TextArea.Session) { IsActive = false };
        _markers = new SearchResultMarkerSource(_session);
        _session.ResultsInvalidated += OnResultsInvalidated;
        _session.ResultsChanged += OnResultsChanged;
        editor.DocumentChanged += OnDocumentChanged;
        editor.TextArea.SearchRequested += OnFindRequested;
        editor.TextArea.ReplaceRequested += OnReplaceRequested;
        editor.TextArea.FindNextRequested += OnFindNextRequested;
        editor.TextArea.FindPreviousRequested += OnFindPreviousRequested;
        editor.TextArea.SizeChanged += OnEditorSizeChanged;
        _readOnlyToken = editor.RegisterPropertyChangedCallback(TextEditor.IsReadOnlyProperty, (_, _) => UpdateReplaceMode());
        SetSearchResultsBrush(editor.SearchResultsBrush);
    }

    public void SetSearchResultsBrush(Brush brush)
    {
        if (_markers == null || _disposed) return;
        if (brush is SolidColorBrush solid)
        {
            var c = solid.Color;
            _markers.Color = new SKColor(c.R, c.G, c.B, (byte)Math.Round(c.A * Math.Clamp(brush.Opacity, 0, 1)));
        }
        else if (brush == null) _markers.Color = SKColors.Transparent;
        else throw new NotSupportedException("Native search markers currently require a SolidColorBrush.");
    }

    public void Open()
    {
        VerifyInstalled();
        if (!IsClosed) return;
        IsClosed = false;
        _editor.TextArea.AddChild(this);
        UpdateSize();
        _session.IsActive = _editor.Document != null;
        _editor.TextArea.TextView.Viewport.MarkerSource = _markers;
        Refresh(false);
    }

    public void Close()
    {
        if (_disposed || _editor == null || IsClosed) return;
        CloseCore(true);
    }

    private void CloseCore(bool restoreFocus)
    {
        IsClosed = true;
        _debounce.Stop();
        _session.IsActive = false;
        var viewport = _editor.TextArea.TextView.Viewport;
        if (ReferenceEquals(viewport.MarkerSource, _markers)) viewport.MarkerSource = null;
        _editor.TextArea.RemoveChild(this);
        StatusText = string.Empty;
        if (restoreFocus) _editor.TextArea.Focus(FocusState.Programmatic);
    }

    public void Reactivate()
    {
        VerifyInstalled();
        Open();
        ApplyTemplate();
        _searchBox?.Focus(FocusState.Programmatic);
        _searchBox?.SelectAll();
    }

    public void FindNext() { VerifyInstalled(); Open(); Refresh(false); _session.FindNext(); ShowCurrentResult(); }
    public void FindPrevious() { VerifyInstalled(); Open(); Refresh(false); _session.FindPrevious(); ShowCurrentResult(); }
    public void ReplaceNext() => Replace(false);
    public void ReplaceAll() => Replace(true);

    private void Replace(bool all)
    {
        VerifyInstalled();
        if (!IsReplaceMode || _editor.Document == null || _editor.IsReadOnly) return;
        Open();
        Refresh(false);
        try
        {
            if (all) _session.ReplaceAll(ReplacePattern);
            else _session.ReplaceNext(ReplacePattern);
            LastError = _session.Error;
            ShowCurrentResult();
            if (LastError == null) StatusText = $"Replaced {LastReplaceCount:N0}; skipped {LastSkippedCount:N0} protected matches";
        }
        catch (Exception error) when (error is ArgumentException || error is InvalidOperationException)
        {
            LastError = error;
            StatusText = error.Message;
        }
    }

    private static void QueryChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var panel = (SearchPanel)sender;
        if (panel._disposed) return;
        panel.SyncTemplate();
        panel._session?.Configure(panel.SearchPattern, panel.MatchCase, panel.WholeWords, panel.UseRegex);
        panel.OnSearchOptionsChanged(new SearchOptionsChangedEventArgs(panel.SearchPattern, panel.MatchCase, panel.UseRegex, panel.WholeWords));
        panel.ScheduleSearch();
    }

    protected virtual void OnSearchOptionsChanged(SearchOptionsChangedEventArgs args) => SearchOptionsChanged?.Invoke(this, args);

    private void ScheduleSearch()
    {
        if (_disposed || IsClosed || _session == null) return;
        LastError = null;
        _debounce.Stop();
        _debounce.Start();
    }
    private void OnResultsInvalidated(object sender, EventArgs args) { ResultCount = 0; ScheduleSearch(); }
    private void OnResultsChanged(object sender, EventArgs args) => UpdateStatus();
    private void OnDebounce(object sender, object args) { _debounce.Stop(); Refresh(true); }

    private void Refresh(bool selectFirst)
    {
        if (_disposed || IsClosed || _session == null) return;
        _debounce.Stop();
        _session.Configure(SearchPattern, MatchCase, WholeWords, UseRegex);
        _session.Refresh();
        if (selectFirst && _editor.Document != null) _session.FindAtOrAfter(_editor.SelectionStart);
        ShowCurrentResult();
    }

    private void ShowCurrentResult()
    {
        UpdateStatus();
        if (_session?.CurrentResultIndex >= 0)
        {
            var view = _editor.TextArea.TextView;
            view.EnsureCaretVisible();
            var caret = view.Viewport.GetCaretRectangle(_editor.CaretOffset);
            // Keep matches below the top overlay when the document is scrollable.
            var margin = ActualHeight + 12;
            if (caret.Top < margin) view.ScrollToVerticalOffset(Math.Max(0, view.VerticalOffset - (margin - caret.Top)));
            view.DrawCaret = true;
        }
    }

    private void UpdateStatus()
    {
        if (_session == null || IsClosed || _disposed) return;
        var count = _session.Results.Count;
        ResultCount = count;
        LastError = _session.Error;
        StatusText = LastError is System.Text.RegularExpressions.RegexMatchTimeoutException
            ? "Search timed out. Simplify the regular expression."
            : LastError != null ? LastError.Message
            : _session.IsTruncated ? $"First {count:N0} matches shown; narrow the search before Replace All"
            : count == 0 ? (SearchPattern.Length == 0 ? "Enter a search pattern" : "No matches found")
            : _session.CurrentResultIndex >= 0 ? $"{_session.CurrentResultIndex + 1:N0} of {count:N0}"
            : $"{count:N0} matches";
    }

    private void UpdateReplaceMode()
    {
        if (_disposed) return;
        if (IsReplaceMode && _editor != null && (_editor.IsReadOnly || _editor.Document == null))
            SetValue(IsReplaceModeProperty, false);
        SyncTemplate();
    }

    protected override void OnApplyTemplate()
    {
        DetachTemplate();
        base.OnApplyTemplate();
        if (_disposed) return;
        _searchBox = GetTemplateChild("PART_searchTextBox") as TextBox;
        _replaceBox = GetTemplateChild("PART_replaceTextBox") as TextBox;
        _caseBox = GetTemplateChild("PART_matchCase") as CheckBox;
        _wordsBox = GetTemplateChild("PART_wholeWords") as CheckBox;
        _regexBox = GetTemplateChild("PART_useRegex") as CheckBox;
        _replaceModeBox = GetTemplateChild("PART_replaceMode") as CheckBox;
        _replaceRow = GetTemplateChild("PART_replaceRow") as FrameworkElement;
        HookTextBox(_searchBox, true);
        HookTextBox(_replaceBox, false);
        foreach (var box in new[] { _caseBox, _wordsBox, _regexBox, _replaceModeBox })
        {
            if (box == null) continue;
            box.Checked += OnToggleChanged;
            box.Unchecked += OnToggleChanged;
            _detachTemplate.Add(() => { box.Checked -= OnToggleChanged; box.Unchecked -= OnToggleChanged; });
        }
        HookButton("PART_findNext", (_, _) => FindNext());
        HookButton("PART_findPrevious", (_, _) => FindPrevious());
        HookButton("PART_replaceNext", (_, _) => ReplaceNext());
        HookButton("PART_replaceAll", (_, _) => ReplaceAll());
        HookButton("PART_close", (_, _) => Close());
        SyncTemplate();
    }

    private void HookTextBox(TextBox box, bool search)
    {
        if (box == null) return;
        var token = box.RegisterPropertyChangedCallback(TextBox.TextProperty, (_, _) =>
        {
            if (_syncing) return;
            if (search) SearchPattern = box.Text;
            else ReplacePattern = box.Text;
        });
        _detachTemplate.Add(() => box.UnregisterPropertyChangedCallback(TextBox.TextProperty, token));
    }
    private void HookButton(string name, RoutedEventHandler handler)
    {
        if (GetTemplateChild(name) is not ButtonBase button) return;
        button.Click += handler;
        _detachTemplate.Add(() => button.Click -= handler);
    }
    private void OnToggleChanged(object sender, RoutedEventArgs args)
    {
        if (_syncing) return;
        if (ReferenceEquals(sender, _caseBox)) MatchCase = _caseBox.IsChecked == true;
        else if (ReferenceEquals(sender, _wordsBox)) WholeWords = _wordsBox.IsChecked == true;
        else if (ReferenceEquals(sender, _regexBox)) UseRegex = _regexBox.IsChecked == true;
        else if (ReferenceEquals(sender, _replaceModeBox)) IsReplaceMode = _replaceModeBox.IsChecked == true;
    }
    private void SyncTemplate()
    {
        if (_syncing || _disposed) return;
        _syncing = true;
        try
        {
            if (_searchBox != null && _searchBox.Text != SearchPattern) _searchBox.Text = SearchPattern;
            if (_replaceBox != null && _replaceBox.Text != ReplacePattern) _replaceBox.Text = ReplacePattern;
            if (_caseBox != null) _caseBox.IsChecked = MatchCase;
            if (_wordsBox != null) _wordsBox.IsChecked = WholeWords;
            if (_regexBox != null) _regexBox.IsChecked = UseRegex;
            if (_replaceModeBox != null)
            {
                _replaceModeBox.IsChecked = IsReplaceMode;
                _replaceModeBox.IsEnabled = _editor?.Document != null && !_editor.IsReadOnly;
            }
            if (_replaceRow != null) _replaceRow.Visibility = IsReplaceMode ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _syncing = false; }
    }
    private void DetachTemplate()
    {
        foreach (var detach in _detachTemplate) detach();
        _detachTemplate.Clear();
        _searchBox = _replaceBox = null;
        _caseBox = _wordsBox = _regexBox = _replaceModeBox = null;
        _replaceRow = null;
    }

    private void OnPanelKeyDown(object sender, KeyRoutedEventArgs args)
    {
        var shift = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0;
        if (args.Key == VirtualKey.Escape) { Close(); args.Handled = true; }
        else if (args.Key == VirtualKey.Enter || args.Key == VirtualKey.F3)
        {
            if (shift) FindPrevious(); else FindNext();
            args.Handled = true;
        }
    }
    private void OnFindRequested(object sender, EventArgs args) { IsReplaceMode = false; UseSelectionAsPattern(); Reactivate(); }
    private void OnReplaceRequested(object sender, EventArgs args) { IsReplaceMode = true; UseSelectionAsPattern(); Reactivate(); }
    private void OnFindNextRequested(object sender, EventArgs args) => FindNext();
    private void OnFindPreviousRequested(object sender, EventArgs args) => FindPrevious();
    private void UseSelectionAsPattern()
    {
        if (_editor.SelectionLength == 0 || _editor.SelectionLength > 2048) return;
        var text = _editor.SelectedText;
        if (!text.Contains('\n') && !text.Contains('\r')) SearchPattern = text;
    }
    private void OnDocumentChanged(object sender, DocumentChangedEventArgs args)
    {
        UpdateReplaceMode();
        if (IsClosed) return;
        _session.IsActive = args.NewDocument != null;
        _editor.TextArea.TextView.Viewport.MarkerSource = _markers;
        ScheduleSearch();
    }
    private void OnEditorSizeChanged(object sender, SizeChangedEventArgs args) => UpdateSize();
    private void UpdateSize() => MaxWidth = Math.Max(0, (_editor?.TextArea.ActualWidth ?? 452) - 12);
    private void OnLoaded(object sender, RoutedEventArgs args) { UpdateSize(); ScheduleSearch(); }
    private void OnUnloaded(object sender, RoutedEventArgs args) => _debounce.Stop();
    private void VerifyInstalled()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SearchPanel));
        if (_editor == null) throw new InvalidOperationException("Install the search panel on an editor before opening it.");
    }
    public void Uninstall() => Dispose();
    public new void Dispose()
    {
        if (_disposed) return;
        if (_editor != null)
        {
            CloseCore(false);
            _editor.DocumentChanged -= OnDocumentChanged;
            _editor.TextArea.SearchRequested -= OnFindRequested;
            _editor.TextArea.ReplaceRequested -= OnReplaceRequested;
            _editor.TextArea.FindNextRequested -= OnFindNextRequested;
            _editor.TextArea.FindPreviousRequested -= OnFindPreviousRequested;
            _editor.TextArea.SizeChanged -= OnEditorSizeChanged;
            _editor.UnregisterPropertyChangedCallback(TextEditor.IsReadOnlyProperty, _readOnlyToken);
            if (ReferenceEquals(_editor.ExistingSearchPanel, this)) _editor.ExistingSearchPanel = null;
        }
        _disposed = true;
        _debounce.Stop();
        _debounce.Tick -= OnDebounce;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        KeyDown -= OnPanelKeyDown;
        DetachTemplate();
        if (_session != null)
        {
            _session.ResultsInvalidated -= OnResultsInvalidated;
            _session.ResultsChanged -= OnResultsChanged;
        }
        _markers?.Dispose();
        _session?.Dispose();
        _markers = null;
        _session = null;
        _editor = null;
        SearchOptionsChanged = null;
        base.Dispose();
    }
}
