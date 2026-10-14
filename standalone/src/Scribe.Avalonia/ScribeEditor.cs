using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Scribe.Core;
namespace Scribe.Avalonia;

/// <summary>Original lookless text editor. The editing surface is EditorViewport, not a framework text editor.</summary>
[TemplatePart("PART_ViewportHost",typeof(ContentPresenter),IsRequired=true)]
[PseudoClasses(":readonly",":modified",":multiple",":large")]
public partial class ScribeEditor : TemplatedControl,IDisposable
{
    public static readonly StyledProperty<Document?> DocumentProperty=AvaloniaProperty.Register<ScribeEditor,Document?>(nameof(Document));
    public static readonly StyledProperty<bool> IsReadOnlyProperty=AvaloniaProperty.Register<ScribeEditor,bool>(nameof(IsReadOnly));
    public static readonly StyledProperty<bool> WordWrapProperty=AvaloniaProperty.Register<ScribeEditor,bool>(nameof(WordWrap));
    public static readonly StyledProperty<bool> ShowLineNumbersProperty=AvaloniaProperty.Register<ScribeEditor,bool>(nameof(ShowLineNumbers),true);
    public static readonly StyledProperty<bool> ShowWhitespaceProperty=AvaloniaProperty.Register<ScribeEditor,bool>(nameof(ShowWhitespace));
    public static readonly StyledProperty<bool> ShowStatusBarProperty=AvaloniaProperty.Register<ScribeEditor,bool>(nameof(ShowStatusBar),true);
    public static readonly StyledProperty<bool> ShowOverviewProperty=AvaloniaProperty.Register<ScribeEditor,bool>(nameof(ShowOverview),true);
    public static readonly StyledProperty<bool> IsSearchOpenProperty=AvaloniaProperty.Register<ScribeEditor,bool>(nameof(IsSearchOpen));
    public static readonly StyledProperty<bool> AutoPairsProperty=AvaloniaProperty.Register<ScribeEditor,bool>(nameof(AutoPairs),true);
    public static readonly StyledProperty<int> TabSizeProperty=AvaloniaProperty.Register<ScribeEditor,int>(nameof(TabSize),4,validate:v=>v is >=1 and <=16);
    public static readonly StyledProperty<bool> UseTabsProperty=AvaloniaProperty.Register<ScribeEditor,bool>(nameof(UseTabs));
    public static readonly StyledProperty<double> ZoomProperty=AvaloniaProperty.Register<ScribeEditor,double>(nameof(Zoom),1,validate:v=>double.IsFinite(v)&&v>=.5&&v<=3);
    public static readonly StyledProperty<string> LanguageProperty=AvaloniaProperty.Register<ScribeEditor,string>(nameof(Language),"Plain Text");
    public static readonly StyledProperty<object?> HeaderProperty=AvaloniaProperty.Register<ScribeEditor,object?>(nameof(Header));
    public static readonly StyledProperty<object?> FooterProperty=AvaloniaProperty.Register<ScribeEditor,object?>(nameof(Footer));
    public static readonly StyledProperty<IBrush?> SelectionBrushProperty=AvaloniaProperty.Register<ScribeEditor,IBrush?>(nameof(SelectionBrush),Brush.Parse("#705B8CCD"));
    public static readonly StyledProperty<IBrush?> CaretBrushProperty=AvaloniaProperty.Register<ScribeEditor,IBrush?>(nameof(CaretBrush),Brushes.White);
    public static readonly StyledProperty<IBrush?> CurrentLineBrushProperty=AvaloniaProperty.Register<ScribeEditor,IBrush?>(nameof(CurrentLineBrush),Brush.Parse("#203858"));
    public static readonly StyledProperty<IBrush?> GutterBrushProperty=AvaloniaProperty.Register<ScribeEditor,IBrush?>(nameof(GutterBrush),Brush.Parse("#8B9BB1"));
    public static readonly DirectProperty<ScribeEditor,string> StatusProperty=AvaloniaProperty.RegisterDirect<ScribeEditor,string>(nameof(Status),c=>c.Status);
    public static readonly DirectProperty<ScribeEditor,string> MessageProperty=AvaloniaProperty.RegisterDirect<ScribeEditor,string>(nameof(Message),c=>c.Message);
    public Document? Document { get=>GetValue(DocumentProperty); set=>SetValue(DocumentProperty,value); }
    public bool IsReadOnly { get=>GetValue(IsReadOnlyProperty); set=>SetValue(IsReadOnlyProperty,value); }
    public bool WordWrap { get=>GetValue(WordWrapProperty); set=>SetValue(WordWrapProperty,value); }
    public bool ShowLineNumbers { get=>GetValue(ShowLineNumbersProperty); set=>SetValue(ShowLineNumbersProperty,value); }
    public bool ShowWhitespace { get=>GetValue(ShowWhitespaceProperty); set=>SetValue(ShowWhitespaceProperty,value); }
    public bool ShowStatusBar { get=>GetValue(ShowStatusBarProperty); set=>SetValue(ShowStatusBarProperty,value); }
    public bool ShowOverview { get=>GetValue(ShowOverviewProperty); set=>SetValue(ShowOverviewProperty,value); }
    public bool IsSearchOpen { get=>GetValue(IsSearchOpenProperty); set=>SetValue(IsSearchOpenProperty,value); }
    public bool AutoPairs { get=>GetValue(AutoPairsProperty); set=>SetValue(AutoPairsProperty,value); }
    public int TabSize { get=>GetValue(TabSizeProperty); set=>SetValue(TabSizeProperty,value); }
    public bool UseTabs { get=>GetValue(UseTabsProperty); set=>SetValue(UseTabsProperty,value); }
    public double Zoom { get=>GetValue(ZoomProperty); set=>SetValue(ZoomProperty,value); }
    public string Language { get=>GetValue(LanguageProperty); set=>SetValue(LanguageProperty,value); }
    public object? Header { get=>GetValue(HeaderProperty); set=>SetValue(HeaderProperty,value); }
    public object? Footer { get=>GetValue(FooterProperty); set=>SetValue(FooterProperty,value); }
    public IBrush? SelectionBrush { get=>GetValue(SelectionBrushProperty); set=>SetValue(SelectionBrushProperty,value); }
    public IBrush? CaretBrush { get=>GetValue(CaretBrushProperty); set=>SetValue(CaretBrushProperty,value); }
    public IBrush? CurrentLineBrush { get=>GetValue(CurrentLineBrushProperty); set=>SetValue(CurrentLineBrushProperty,value); }
    public IBrush? GutterBrush { get=>GetValue(GutterBrushProperty); set=>SetValue(GutterBrushProperty,value); }
    private string _status="",_message="";
    public string Status { get=>_status; private set=>SetAndRaise(StatusProperty,ref _status,value); }
    public string Message { get=>_message; internal set=>SetAndRaise(MessageProperty,ref _message,value); }
    public EditSession Session { get; private set; }=null!;
    public EditorViewport Viewport { get; }
    public SearchController Search { get; }
    public bool CanEdit=>Session is not null&&Session.CanEdit;
    public bool IsLargeFile=>(Document?.Length??0)>2_000_000;
    public bool IsDark=>ActualThemeVariant==ThemeVariant.Dark;
    public EditorCommand UndoCommand { get; }
    public EditorCommand RedoCommand { get; }
    public EditorCommand FindCommand { get; }
    public EditorCommand CommentCommand { get; }
    public EditorCommand DuplicateCommand { get; }
    public EditorCommand BookmarkCommand { get; }
    public event EventHandler? ViewChanged;
    private ContentPresenter? _host;
    private ScrollBar? _vertical,_horizontal;
    private SearchBar? _searchBar;
    private bool _updatingScroll,_disposed;
    private Document? _observed;
    private readonly DispatcherTimer _analysisTimer;
    public ScribeEditor()
    {
        Viewport=new(this); Search=new(this);
        UndoCommand=new(()=>{ if(Document?.CanUndo==true) Document.Undo(); RevealCaret(); },()=>CanEdit&&Document?.CanUndo==true);
        RedoCommand=new(()=>{ if(Document?.CanRedo==true) Document.Redo(); RevealCaret(); },()=>CanEdit&&Document?.CanRedo==true);
        FindCommand=new(OpenSearch); CommentCommand=new(()=>{Session.ToggleComment(Language=="Python"?"#":"//"); RevealCaret();},()=>CanEdit);
        DuplicateCommand=new(()=>{Session.Duplicate(); RevealCaret();},()=>CanEdit); BookmarkCommand=new(()=>Session.ToggleBookmark());
        _analysisTimer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(250)};
        _analysisTimer.Tick+=async(_,_)=>{_analysisTimer.Stop(); await RefreshAnalysisAsync();};
        ActualThemeVariantChanged+=ThemeChanged;
        Document=new Document();
        MenuItem Item(string name,EditorCommand command)=>new(){Header=name,Command=command};
        Viewport.ContextMenu=new ContextMenu {ItemsSource=new object[]{
            Item("Undo",UndoCommand),Item("Redo",RedoCommand),new Separator(),
            Item("Copy",new(async()=>await CopyAsync())),Item("Cut",new(async()=>await CopyAsync(true),()=>CanEdit)),Item("Paste",new(async()=>await PasteAsync(),()=>CanEdit)),
            Item("Select all",new(()=>Session.SelectAll())),new Separator(),Item("Find / replace",FindCommand),Item("Toggle comment",CommentCommand),Item("Duplicate",DuplicateCommand),
            Item("Bookmark",BookmarkCommand),Item("Fold all",new(()=>FoldAll())),Item("Unfold all",new(()=>FoldAll(false)))}};
    }
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if(_host is not null) _host.Content=null;
        if(_vertical is not null) _vertical.PropertyChanged-=ScrollChanged;
        if(_horizontal is not null) _horizontal.PropertyChanged-=ScrollChanged;
        base.OnApplyTemplate(e);
        _host=e.NameScope.Find<ContentPresenter>("PART_ViewportHost")??throw new InvalidOperationException("ScribeEditor requires PART_ViewportHost.");
        _host.Content=Viewport;
        _vertical=e.NameScope.Find<ScrollBar>("PART_VerticalScrollBar"); _horizontal=e.NameScope.Find<ScrollBar>("PART_HorizontalScrollBar");
        if(_vertical is not null) _vertical.PropertyChanged+=ScrollChanged;
        if(_horizontal is not null) _horizontal.PropertyChanged+=ScrollChanged;
        _searchBar=e.NameScope.Find<SearchBar>("PART_SearchBar"); if(_searchBar is not null) _searchBar.Controller=Search;
        AttachCompletionTemplate(e); UpdateView();
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e); if(Viewport is null||Search is null) return;
        if(e.Property==DocumentProperty) SwitchDocument();
        else if(e.Property==IsSearchOpenProperty)
        {
            if(IsSearchOpen) {Search.Refresh(); Dispatcher.UIThread.Post(()=>_searchBar?.FocusQuery());} Viewport.InvalidateVisual();
        }
        else if(e.Property==IsReadOnlyProperty||e.Property==TabSizeProperty||e.Property==UseTabsProperty) ApplyOptions();
        else if(e.Property==WordWrapProperty||e.Property==ZoomProperty||e.Property==FontSizeProperty||e.Property==FontFamilyProperty||e.Property==ForegroundProperty||e.Property==ShowLineNumbersProperty||e.Property==ShowWhitespaceProperty||e.Property==LanguageProperty)
        {Viewport.ResetLayout(); ScheduleAnalysis();}
    }
    private void SwitchDocument()
    {
        if(_observed is not null) _observed.Changed-=DocumentChanged;
        if(Session is not null) {Session.Changed-=SessionChanged; Session.Dispose();}
        CloseCompletion(); Snippet?.Cancel(); _pairs.Clear(); _folds=[]; _collapsed.Clear(); Diagnostics=[];
        _observed=Document??new Document(); Session=new EditSession(_observed); Session.Changed+=SessionChanged; _observed.Changed+=DocumentChanged;
        Search.ResetScope(); ApplyOptions(); Viewport.ResetLayout(); Viewport.ScrollTo(0,0); ScheduleAnalysis();
    }
    private void DocumentChanged(object? sender,DocumentChange e)
    {
        CloseCompletion(); CancelDiagnostics(); Diagnostics=[];
        var previous=_folds; var collapsed=_collapsed.ToHashSet(); _collapsed.Clear();
        _folds=previous.Select(f=>new FoldRegion(e.After.LineAt(Core.Document.MapOffset(e.Before.GetLineStart(Math.Min(f.FirstLine,e.Before.LineCount-1)),e.Edits)),e.After.LineAt(Core.Document.MapOffset(e.Before.GetLineStart(Math.Min(f.LastLine,e.Before.LineCount-1)),e.Edits)),f.Label)).Where(f=>f.LastLine>f.FirstLine).ToArray();
        for(int i=0;i<Math.Min(previous.Count,_folds.Count);i++) if(collapsed.Contains(previous[i].FirstLine)) _collapsed.Add(_folds[i].FirstLine);
        Viewport.DocumentChanged(e); Search.Schedule(); ScheduleAnalysis();
    }
    private void SessionChanged(object? sender,EventArgs e)
    {
        if(Session is null) return;
        var b=Session.Document.Snapshot; var p=Session.Primary; var line=b.GetLine(b.LineAt(p.Caret));
        Status=$"Ln {line.Index+1:N0}, Col {p.Caret-line.Start+1:N0}   ·   {Session.Selections.Count} caret{(Session.Selections.Count==1?"":"s")}   ·   {Session.Selections.Sum(s=>s.Length):N0} selected   ·   {Language}   ·   {Zoom:P0}"+(CanEdit?"":"   ·   Read only")+(Session.Document.IsDirty?"   ·   Modified":"");
        PseudoClasses.Set(":readonly",!CanEdit); PseudoClasses.Set(":modified",Session.Document.IsDirty); PseudoClasses.Set(":multiple",Session.Selections.Count>1); PseudoClasses.Set(":large",IsLargeFile);
        UndoCommand.Refresh(); RedoCommand.Refresh(); CommentCommand.Refresh(); DuplicateCommand.Refresh(); Search.RefreshCommands();
        Viewport.RefreshInputState(); UpdateView();
    }
    private void ApplyOptions()
    {
        if(Session is null) return;
        Session.IsReadOnly=IsReadOnly; Session.TabSize=TabSize; Session.UseTabs=UseTabs;
        if(!CanEdit) {CloseCompletion(); Snippet?.Cancel();}
        Viewport.ResetLayout(); SessionChanged(this,EventArgs.Empty);
    }
    private void ThemeChanged(object? sender,EventArgs e)=>Viewport.ResetLayout();
    internal void UpdateView()
    {
        if(_disposed) return; Viewport.InvalidateVisual(); _updatingScroll=true;
        try
        {
            if(_vertical is not null) { _vertical.Maximum=Math.Max(0,Viewport.ExtentHeight-Viewport.Bounds.Height); _vertical.ViewportSize=Viewport.Bounds.Height; _vertical.SmallChange=Viewport.LineHeight; _vertical.LargeChange=Math.Max(Viewport.LineHeight,Viewport.Bounds.Height-Viewport.LineHeight); _vertical.Value=Viewport.OffsetY; }
            if(_horizontal is not null) { _horizontal.IsVisible=!WordWrap; _horizontal.Maximum=Math.Max(0,Viewport.ExtentWidth-Viewport.TextWidth); _horizontal.ViewportSize=Viewport.TextWidth; _horizontal.SmallChange=20; _horizontal.Value=Viewport.OffsetX; }
        }
        finally {_updatingScroll=false;}
        ViewChanged?.Invoke(this,EventArgs.Empty);
    }
    private void ScrollChanged(object? sender,AvaloniaPropertyChangedEventArgs e)
    {
        if(!_updatingScroll&&e.Property==RangeBase.ValueProperty) Viewport.ScrollTo(_horizontal?.Value??0,_vertical?.Value??0);
    }
    public void FocusEditor()=>Viewport.Focus();
    public void RevealCaret() {Viewport.EnsureCaretVisible(); Viewport.RefreshInputState(); UpdateView();}
    public void OpenSearch()
    {
        if(Session.Primary.Length is >0 and <256) {var text=Session.SelectedText(); if(!text.Contains('\n')) Search.Pattern=text;}
        IsSearchOpen=true; _searchBar?.FocusQuery();
    }
    private void ScheduleAnalysis() {if(_analysisTimer is null||_disposed) return; _analysisTimer.Stop(); if(this.IsAttachedToVisualTree()) _analysisTimer.Start();}
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {base.OnAttachedToVisualTree(e); ScheduleAnalysis();}
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {_analysisTimer.Stop(); CloseCompletion(); CancelDiagnostics(); base.OnDetachedFromVisualTree(e);}
    public void Dispose()
    {
        if(_disposed) return; _disposed=true; _analysisTimer.Stop(); CloseCompletion(); CancelDiagnostics(); Search.Dispose();
        if(_observed is not null) _observed.Changed-=DocumentChanged;
        Session.Changed-=SessionChanged; Session.Dispose(); ActualThemeVariantChanged-=ThemeChanged;
        if(_vertical is not null) _vertical.PropertyChanged-=ScrollChanged; if(_horizontal is not null) _horizontal.PropertyChanged-=ScrollChanged;
        if(_host is not null) _host.Content=null; Viewport.Dispose(); ViewChanged=null;
    }
}
