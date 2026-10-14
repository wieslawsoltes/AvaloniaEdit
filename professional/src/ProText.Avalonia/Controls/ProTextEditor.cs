using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Indentation;
using AvaloniaEdit.Indentation.CSharp;
using AvaloniaEdit.Rendering;
using ProText.Core;
using ProText.Avalonia.Services;
namespace ProText.Avalonia.Controls;

/// <summary>A replaceable editor chrome around a stable AvaloniaEdit surface. Document state belongs to Session.</summary>
[TemplatePart("PART_EditorHost",typeof(ContentPresenter),IsRequired=true)]
[PseudoClasses(":readonly",":large-file")]
public partial class ProTextEditor : TemplatedControl, IDisposable
{
    public static readonly StyledProperty<EditorSession?> SessionProperty=AvaloniaProperty.Register<ProTextEditor,EditorSession?>(nameof(Session));
    public static readonly StyledProperty<bool> IsReadOnlyProperty=AvaloniaProperty.Register<ProTextEditor,bool>(nameof(IsReadOnly));
    public static readonly StyledProperty<bool> WordWrapProperty=AvaloniaProperty.Register<ProTextEditor,bool>(nameof(WordWrap));
    public static readonly StyledProperty<bool> ShowLineNumbersProperty=AvaloniaProperty.Register<ProTextEditor,bool>(nameof(ShowLineNumbers),true);
    public static readonly StyledProperty<bool> ShowWhitespaceProperty=AvaloniaProperty.Register<ProTextEditor,bool>(nameof(ShowWhitespace));
    public static readonly StyledProperty<bool> ShowStatusBarProperty=AvaloniaProperty.Register<ProTextEditor,bool>(nameof(ShowStatusBar),true);
    public static readonly StyledProperty<bool> ShowOverviewProperty=AvaloniaProperty.Register<ProTextEditor,bool>(nameof(ShowOverview),true);
    public static readonly StyledProperty<bool> EnableFoldingProperty=AvaloniaProperty.Register<ProTextEditor,bool>(nameof(EnableFolding),true);
    public static readonly StyledProperty<bool> EnableAutoPairsProperty=AvaloniaProperty.Register<ProTextEditor,bool>(nameof(EnableAutoPairs),true);
    public static readonly StyledProperty<bool> IsSearchOpenProperty=AvaloniaProperty.Register<ProTextEditor,bool>(nameof(IsSearchOpen));
    public static readonly StyledProperty<bool> IsGoToOpenProperty=AvaloniaProperty.Register<ProTextEditor,bool>(nameof(IsGoToOpen));
    public static readonly StyledProperty<string> GoToTextProperty=AvaloniaProperty.Register<ProTextEditor,string>(nameof(GoToText),"1");
    public static readonly StyledProperty<double> ZoomProperty=AvaloniaProperty.Register<ProTextEditor,double>(nameof(Zoom),1,validate:v=>double.IsFinite(v)&&v>=.5&&v<=3);
    public static readonly StyledProperty<int> TabSizeProperty=AvaloniaProperty.Register<ProTextEditor,int>(nameof(TabSize),4,validate:v=>v>=1&&v<=16);
    public static readonly StyledProperty<bool> UseTabsProperty=AvaloniaProperty.Register<ProTextEditor,bool>(nameof(UseTabs));
    public static readonly StyledProperty<object?> HeaderProperty=AvaloniaProperty.Register<ProTextEditor,object?>(nameof(Header));
    public static readonly StyledProperty<object?> FooterProperty=AvaloniaProperty.Register<ProTextEditor,object?>(nameof(Footer));
    public static readonly StyledProperty<ControlTheme?> EditorThemeProperty=AvaloniaProperty.Register<ProTextEditor,ControlTheme?>(nameof(EditorTheme));
    public static readonly DirectProperty<ProTextEditor,string> StatusTextProperty=AvaloniaProperty.RegisterDirect<ProTextEditor,string>(nameof(StatusText),o=>o.StatusText);
    public static readonly DirectProperty<ProTextEditor,string> DocumentStatusProperty=AvaloniaProperty.RegisterDirect<ProTextEditor,string>(nameof(DocumentStatus),o=>o.DocumentStatus);
    public static readonly DirectProperty<ProTextEditor,string> LastMessageProperty=AvaloniaProperty.RegisterDirect<ProTextEditor,string>(nameof(LastMessage),o=>o.LastMessage);
    public EditorSession? Session { get=>GetValue(SessionProperty); set=>SetValue(SessionProperty,value); }
    public bool IsReadOnly { get=>GetValue(IsReadOnlyProperty); set=>SetValue(IsReadOnlyProperty,value); }
    public bool WordWrap { get=>GetValue(WordWrapProperty); set=>SetValue(WordWrapProperty,value); }
    public bool ShowLineNumbers { get=>GetValue(ShowLineNumbersProperty); set=>SetValue(ShowLineNumbersProperty,value); }
    public bool ShowWhitespace { get=>GetValue(ShowWhitespaceProperty); set=>SetValue(ShowWhitespaceProperty,value); }
    public bool ShowStatusBar { get=>GetValue(ShowStatusBarProperty); set=>SetValue(ShowStatusBarProperty,value); }
    public bool ShowOverview { get=>GetValue(ShowOverviewProperty); set=>SetValue(ShowOverviewProperty,value); }
    public bool EnableFolding { get=>GetValue(EnableFoldingProperty); set=>SetValue(EnableFoldingProperty,value); }
    public bool EnableAutoPairs { get=>GetValue(EnableAutoPairsProperty); set=>SetValue(EnableAutoPairsProperty,value); }
    public bool IsSearchOpen { get=>GetValue(IsSearchOpenProperty); set=>SetValue(IsSearchOpenProperty,value); }
    public bool IsGoToOpen { get=>GetValue(IsGoToOpenProperty); set=>SetValue(IsGoToOpenProperty,value); }
    public string GoToText { get=>GetValue(GoToTextProperty); set=>SetValue(GoToTextProperty,value); }
    public double Zoom { get=>GetValue(ZoomProperty); set=>SetValue(ZoomProperty,value); }
    public int TabSize { get=>GetValue(TabSizeProperty); set=>SetValue(TabSizeProperty,value); }
    public bool UseTabs { get=>GetValue(UseTabsProperty); set=>SetValue(UseTabsProperty,value); }
    public object? Header { get=>GetValue(HeaderProperty); set=>SetValue(HeaderProperty,value); }
    public object? Footer { get=>GetValue(FooterProperty); set=>SetValue(FooterProperty,value); }
    public ControlTheme? EditorTheme { get=>GetValue(EditorThemeProperty); set=>SetValue(EditorThemeProperty,value); }
    private string _statusText="",_documentStatus="",_lastMessage="";
    public string StatusText { get=>_statusText; private set=>SetAndRaise(StatusTextProperty,ref _statusText,value); }
    public string DocumentStatus { get=>_documentStatus; private set=>SetAndRaise(DocumentStatusProperty,ref _documentStatus,value); }
    public string LastMessage { get=>_lastMessage; private set=>SetAndRaise(LastMessageProperty,ref _lastMessage,value); }
    public TextEditor Editor { get; }
    public EditorSearchController Search { get; }
    public bool EffectiveReadOnly=>IsReadOnly||Session is null||Session.IsReadOnly;
    public int MaxAnalysisLength { get; set; }=2_000_000;
    public bool IsLargeFile=>(Session?.Length??0)>MaxAnalysisLength;
    public IFoldingProvider? FoldingProvider { get; set; }=new BraceFoldingProvider();
    public ICompletionProvider? CompletionProvider { get; set; }
    public IDiagnosticProvider? DiagnosticProvider { get; set; }
    public int FoldingCount=>_folding?.AllFoldings.Count()??0;
    public int FoldedCount=>_folding?.AllFoldings.Count(f=>f.IsFolded)??0;
    public string Text { get=>Session?.Text??""; set { if(Session is null) Session=new(value); else Session.ReplaceAllText(value); } }
    public EditorCommand UndoCommand { get; }
    public EditorCommand RedoCommand { get; }
    public EditorCommand FindCommand { get; }
    public EditorCommand GoToCommand { get; }
    public EditorCommand CommentCommand { get; }
    public EditorCommand DuplicateCommand { get; }
    public EditorCommand TrimCommand { get; }
    public EditorCommand BookmarkCommand { get; }
    public event EventHandler? CaretChanged;
    public event EventHandler? ViewChanged;
    public event EventHandler<Exception>? ServiceError;
    private ContentPresenter? _host;
    private EditorSearchBar? _searchBar;
    private TextBox? _goToBox;
    private FoldingManager? _folding;
    private EditorSession? _subscribedSession;
    private readonly DispatcherTimer _searchTimer,_analysisTimer;
    private bool _attached,_disposed;
    public ProTextEditor()
    {
        Editor=new ProTextSurface { ShowLineNumbers=true, Background=Brushes.Transparent };
        Search=new EditorSearchController(this);
        Editor.TextArea.TextView.BackgroundRenderers.Add(new EditorAdornmentRenderer(this));
        Editor.TextArea.Caret.PositionChanged+=OnCaretChanged;
        Editor.TextArea.SelectionChanged+=OnCaretChanged;
        Editor.TextArea.TextView.ScrollOffsetChanged+=OnScrollChanged;
        Editor.TextArea.TextEntering+=OnTextEntering;
        Editor.TextArea.TextEntered+=OnTextEntered;
        Editor.AddHandler(KeyDownEvent,OnEditorKeyDown,RoutingStrategies.Tunnel);
        Editor.AddHandler(PointerWheelChangedEvent,OnEditorWheel,RoutingStrategies.Tunnel);
        ActualThemeVariantChanged+=OnThemeVariantChanged;
        _searchTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(140) };
        _searchTimer.Tick+=(_,_)=>{ _searchTimer.Stop(); Search.Refresh(); };
        _analysisTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(300) };
        _analysisTimer.Tick+=async(_,_)=>{ _analysisTimer.Stop(); await RefreshAnalysisAsync(); };
        Search.ResultsChanged+=(_,_)=>Redraw();
        UndoCommand=new(()=>Editor.Undo(),()=>!EffectiveReadOnly&&Editor.CanUndo);
        RedoCommand=new(()=>Editor.Redo(),()=>!EffectiveReadOnly&&Editor.CanRedo);
        FindCommand=new(OpenSearch); GoToCommand=new(OpenGoTo);
        CommentCommand=new(ToggleComment,()=>!EffectiveReadOnly);
        DuplicateCommand=new(DuplicateSelectionOrLine,()=>!EffectiveReadOnly);
        TrimCommand=new(TrimTrailingWhitespace,()=>!EffectiveReadOnly);
        BookmarkCommand=new(ToggleBookmark);
        Editor.ContextMenu=CreateContextMenu();
        Session=new EditorSession();
    }
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if(_host is not null) _host.Content=null;
        if(_goToBox is not null) _goToBox.KeyDown-=OnGoToKey;
        base.OnApplyTemplate(e);
        _host=e.NameScope.Find<ContentPresenter>("PART_EditorHost")??throw new InvalidOperationException("ProTextEditor requires a ContentPresenter named PART_EditorHost.");
        _host.Content=Editor;
        _searchBar=e.NameScope.Find<EditorSearchBar>("PART_SearchBar");
        if(_searchBar is not null) _searchBar.Controller=Search;
        _goToBox=e.NameScope.Find<TextBox>("PART_GoToBox");
        if(_goToBox is not null) _goToBox.KeyDown+=OnGoToKey;
        ApplyOptions();
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if(Editor is null) return;
        if(change.Property==SessionProperty) SwitchSession();
        else if(change.Property==IsSearchOpenProperty) { if(IsSearchOpen) { Search.Refresh(); Dispatcher.UIThread.Post(()=>_searchBar?.FocusQuery()); } Redraw(); }
        else if(change.Property==IsGoToOpenProperty) { if(IsGoToOpen) Dispatcher.UIThread.Post(()=>{ _goToBox?.Focus(); _goToBox?.SelectAll(); }); }
        else if(change.Property==IsReadOnlyProperty||change.Property==WordWrapProperty||change.Property==ShowLineNumbersProperty||change.Property==ShowWhitespaceProperty||change.Property==EnableFoldingProperty||change.Property==TabSizeProperty||change.Property==UseTabsProperty||change.Property==ZoomProperty||change.Property==FontSizeProperty||change.Property==FontFamilyProperty||change.Property==ForegroundProperty||change.Property==EditorThemeProperty) ApplyOptions();
    }
    private void OnThemeVariantChanged(object? sender,EventArgs e)=>ApplyOptions();
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e); ObjectDisposedException.ThrowIf(_disposed,this); _attached=true;
        HookSession(); ApplyOptions(); ScheduleAnalysis();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached=false; _searchTimer.Stop(); _analysisTimer.Stop(); CancelCompletion(); CancelDiagnostics(); UnhookSession();
        base.OnDetachedFromVisualTree(e);
    }
    private void SwitchSession()
    {
        UnhookSession(); CancelCompletion(); CancelDiagnostics(); _autoPairs.Clear();
        if(_folding is not null) { FoldingManager.Uninstall(_folding); _folding=null; }
        Editor.Document=Session?.Document??new TextDocument();
        HookSession(); Search.SessionChanged(); ApplyOptions(); ScheduleAnalysis();
    }
    private void HookSession()
    {
        if(_subscribedSession==Session) return; UnhookSession(); _subscribedSession=Session;
        if(_subscribedSession is not null)
        {
            _subscribedSession.Changed+=OnDocumentChanged; _subscribedSession.DiagnosticsChanged+=OnDecorationsChanged;
            _subscribedSession.PropertyChanged+=OnSessionPropertyChanged; _subscribedSession.Bookmarks.CollectionChanged+=OnBookmarksChanged;
        }
    }
    private void UnhookSession()
    {
        if(_subscribedSession is null) return;
        _subscribedSession.Changed-=OnDocumentChanged; _subscribedSession.DiagnosticsChanged-=OnDecorationsChanged;
        _subscribedSession.PropertyChanged-=OnSessionPropertyChanged; _subscribedSession.Bookmarks.CollectionChanged-=OnBookmarksChanged;
        _subscribedSession=null;
    }
    private void OnDocumentChanged(object? sender,EventArgs e) { CancelCompletion(); CancelDiagnostics(); UpdateStatus(); ScheduleSearch(); ScheduleAnalysis(); Redraw(); }
    private void OnDecorationsChanged(object? sender,EventArgs e)=>Redraw();
    private void OnBookmarksChanged(object? sender,NotifyCollectionChangedEventArgs e)=>Redraw();
    private void OnSessionPropertyChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName is nameof(EditorSession.Language) or nameof(EditorSession.IsReadOnly)) ApplyOptions();
        else UpdateStatus();
    }
    private void OnCaretChanged(object? sender,EventArgs e) { UpdateStatus(); CaretChanged?.Invoke(this,EventArgs.Empty); }
    private void OnScrollChanged(object? sender,EventArgs e)=>ViewChanged?.Invoke(this,EventArgs.Empty);
    public void ApplyOptions()
    {
        if(Editor is null||Search is null) return;
        bool dark=ActualThemeVariant==ThemeVariant.Dark;
        Editor.IsReadOnly=EffectiveReadOnly; Editor.WordWrap=WordWrap; Editor.ShowLineNumbers=ShowLineNumbers;
        Editor.FontFamily=FontFamily; Editor.FontSize=Math.Max(8,FontSize*Zoom); Editor.Foreground=Foreground; Editor.Theme=EditorTheme;
        Editor.Options.ShowSpaces=ShowWhitespace; Editor.Options.ShowTabs=ShowWhitespace; Editor.Options.ShowEndOfLine=ShowWhitespace;
        Editor.Options.IndentationSize=TabSize; Editor.Options.ConvertTabsToSpaces=!UseTabs; Editor.Options.HighlightCurrentLine=true;
        Editor.TextArea.IndentationStrategy=Session?.Language=="C#"?new CSharpIndentationStrategy(Editor.Options):new DefaultIndentationStrategy();
        Editor.SyntaxHighlighting=EditorSyntax.Get(Session?.Language??"Plain Text",dark);
        Editor.LineNumbersForeground=Brush.Parse(dark?"#718198":"#758094");
        Editor.TextArea.SelectionBrush=Brush.Parse(dark?"#80506FAD":"#604E82C4");
        Editor.TextArea.SelectionForeground=Foreground;
        Editor.TextArea.TextView.CurrentLineBackground=Brush.Parse(dark?"#182334":"#F1F5FA");
        if(EnableFolding&&!IsLargeFile&&_folding is null) _folding=FoldingManager.Install(Editor.TextArea);
        else if((!EnableFolding||IsLargeFile)&&_folding is not null) { FoldingManager.Uninstall(_folding); _folding=null; }
        PseudoClasses.Set(":readonly",EffectiveReadOnly); PseudoClasses.Set(":large-file",IsLargeFile);
        Search.UpdateReadOnly(); UpdateStatus(); Redraw();
    }
    internal void ScheduleSearch()
    {
        if(!IsSearchOpen&&Search.Pattern.Length==0) return;
        _searchTimer.Stop(); if(_attached) _searchTimer.Start(); else Search.Refresh();
    }
    private void ScheduleAnalysis() { _analysisTimer.Stop(); if(_attached) _analysisTimer.Start(); }
    private void UpdateStatus()
    {
        if(Editor is null) return;
        var caret=Editor.TextArea.Caret;
        StatusText=$"Ln {caret.Line:N0}, Col {caret.Column:N0}"+(Editor.SelectionLength>0?$"  •  {Editor.SelectionLength:N0} selected":"");
        var session=Session;
        DocumentStatus=$"{session?.Language??"Plain Text"}  •  {session?.EncodingName??"UTF-8"}  •  {session?.LineEnding.ToString()??"Lf"}  •  {(UseTabs?"Tabs":"Spaces")}: {TabSize}  •  {Zoom:P0}"+(EffectiveReadOnly?"  •  Read only":"")+(session?.IsDirty==true?"  •  Modified":"");
        UndoCommand?.Refresh(); RedoCommand?.Refresh(); CommentCommand?.Refresh(); DuplicateCommand?.Refresh(); TrimCommand?.Refresh();
    }
    internal void Redraw() { Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection); ViewChanged?.Invoke(this,EventArgs.Empty); }
    private void ReportServiceError(Exception ex) { LastMessage=ex.Message; ServiceError?.Invoke(this,ex); }
    public void FocusEditor()=>Editor.TextArea.Focus();
    public void Dispose()
    {
        if(_disposed) return; _disposed=true; _searchTimer.Stop(); _analysisTimer.Stop(); UnhookSession(); CancelCompletion(); CancelDiagnostics(); ActualThemeVariantChanged-=OnThemeVariantChanged;
        if(_folding is not null) { FoldingManager.Uninstall(_folding); _folding=null; }
        if(_goToBox is not null) _goToBox.KeyDown-=OnGoToKey;
        if(_host is not null) _host.Content=null;
    }
}
