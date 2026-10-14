using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Scribe.Core;
namespace Scribe.Avalonia;
public partial class ScribeEditor
{
    private IReadOnlyList<FoldRegion> _folds=[];
    private readonly HashSet<int> _collapsed=[];
    public IReadOnlyList<FoldRegion> Folds=>_folds;
    public IReadOnlySet<int> CollapsedLines=>_collapsed;
    public IReadOnlyList<Diagnostic> Diagnostics {get;private set;}=[];
    public ICompletionProvider? CompletionProvider {get;set;}
    public IDiagnosticProvider? DiagnosticProvider {get;set;}
    private CancellationTokenSource? _completionCancellation,_diagnosticCancellation;
    private Popup? _completionPopup;
    private ListBox? _completionList;
    private IReadOnlyList<Completion> _completionItems=[];
    private int _completionStart;
    private long _completionRevision;
    public int CompletionCount=>_completionPopup?.IsOpen==true?_completionItems.Count:0;
    private void AttachCompletionTemplate(TemplateAppliedEventArgs e)
    {
        CloseCompletion();
        if(_completionList is not null)
        {
            _completionList.DoubleTapped-=CompleteDoubleTapped;
            _completionList.RemoveHandler(InputElement.KeyDownEvent,CompletionKeyDown);
            _completionList.LostFocus-=CompletionLostFocus;
        }
        _completionPopup=e.NameScope.Find<Popup>("PART_CompletionPopup");
        _completionList=e.NameScope.Find<ListBox>("PART_CompletionList");
        if(_completionPopup is not null) _completionPopup.PlacementTarget=Viewport;
        if(_completionList is not null)
        {
            _completionList.DoubleTapped+=CompleteDoubleTapped;
            _completionList.AddHandler(InputElement.KeyDownEvent,CompletionKeyDown,RoutingStrategies.Tunnel);
            _completionList.LostFocus+=CompletionLostFocus;
        }
    }
    internal bool HasCompletionFocus(IInputElement? input)=>input is Visual visual&&_completionList is not null&&
        (ReferenceEquals(visual,_completionList)||_completionList.IsVisualAncestorOf(visual));
    private void CompletionLostFocus(object? sender,FocusChangedEventArgs e)
    {
        if(!ReferenceEquals(e.NewFocusedElement,Viewport)&&!HasCompletionFocus(e.NewFocusedElement))CloseCompletion();
    }
    private void CompletionKeyDown(object? sender,KeyEventArgs e)
    {
        bool escape=e.Key==Key.Escape;
        if(HandleCompletionKey(e)&&escape)FocusEditor();
    }
    private void CompleteDoubleTapped(object? sender,TappedEventArgs e)=>AcceptCompletion();
    public async Task RefreshAnalysisAsync()
    {
        if(_disposed||Session is null) return;
        var document=Session.Document; var snapshot=document.Snapshot; long revision=document.Revision;
        if(snapshot.Length>2_000_000) {Message="Large-file mode: background lexical analysis is paused; editing and search remain available."; return;}
        var folds=Language=="Plain Text"?Array.Empty<FoldRegion>():LexicalLanguage.Folds(snapshot).ToArray();
        if(!_folds.SequenceEqual(folds)) {_folds=folds; _collapsed.RemoveWhere(i=>!folds.Any(f=>f.FirstLine==i)); Viewport.ApplyFolds();}
        if(DiagnosticProvider is null) return;
        CancelDiagnostics(); var cancellation=new CancellationTokenSource(); _diagnosticCancellation=cancellation; var token=cancellation.Token;
        try
        {
            var diagnostics=await DiagnosticProvider.AnalyzeAsync(snapshot,revision,token);
            await Dispatcher.UIThread.InvokeAsync(()=>
            {
                if(token.IsCancellationRequested||_disposed||Session.Document!=document||document.Revision!=revision) return;
                Diagnostics=diagnostics.Where(d=>d.Start>=0&&d.Length>=0&&d.Start<=document.Length&&d.Length<=document.Length-d.Start).Take(10000).OrderBy(d=>d.Start).ToArray(); UpdateView();
            });
        }
        catch(OperationCanceledException) { }
        catch(Exception e) {Message="Diagnostic provider: "+e.Message;}
    }
    public bool SetDiagnostics(long revision,IReadOnlyList<Diagnostic> diagnostics)
    {
        if(_disposed||Session.Document.Revision!=revision) return false;
        if(diagnostics.Any(d=>d.Start<0||d.Length<0||d.Start>Session.Document.Length||d.Length>Session.Document.Length-d.Start)) throw new ArgumentOutOfRangeException(nameof(diagnostics));
        Diagnostics=diagnostics.Take(10000).OrderBy(d=>d.Start).ToArray(); UpdateView(); return true;
    }
    public void ToggleFold(int firstLine)
    {
        if(!_folds.Any(f=>f.FirstLine==firstLine)) return;
        if(!_collapsed.Add(firstLine)) _collapsed.Remove(firstLine); Viewport.ApplyFolds(); UpdateView();
    }
    public void FoldAll(bool collapsed=true)
    {
        _collapsed.Clear(); if(collapsed) foreach(var f in _folds) _collapsed.Add(f.FirstLine);
        Viewport.ApplyFolds(); UpdateView();
    }
    internal void UnfoldAt(int line)
    {
        bool changed=false;
        foreach(var f in _folds) if(f.FirstLine<line&&f.LastLine>=line) changed|=_collapsed.Remove(f.FirstLine);
        if(changed) Viewport.ApplyFolds();
    }
    public async Task ShowCompletionAsync()
    {
        if(!CanEdit||CompletionProvider is null||_completionPopup is null||_completionList is null||IsLargeFile) return;
        CloseCompletion(); var cancellation=new CancellationTokenSource(); _completionCancellation=cancellation; var token=cancellation.Token;
        var session=Session; var snapshot=session.Document.Snapshot; int offset=session.Primary.Caret; long revision=session.Document.Revision;
        int start=offset; while(start>0&&EditSession.IsWord(snapshot[start-1])) start--;
        try
        {
            var items=await CompletionProvider.CompleteAsync(new(snapshot,offset,revision),token);
            await Dispatcher.UIThread.InvokeAsync(()=>
            {
                if(_disposed||token.IsCancellationRequested||session!=Session||!CanEdit||session.Primary.Caret!=offset||session.Document.Revision!=revision||TopLevel.GetTopLevel(Viewport) is null) return;
                _completionItems=items.Take(200).ToArray(); if(_completionItems.Count==0) return;
                _completionStart=start; _completionRevision=revision;
                _completionList!.ItemsSource=_completionItems; _completionList.SelectedIndex=0;
                var caret=Viewport.CaretRectangle;
                _completionPopup!.Placement=PlacementMode.BottomEdgeAlignedLeft;
                _completionPopup.HorizontalOffset=caret.X;
                _completionPopup.VerticalOffset=caret.Bottom-Viewport.Bounds.Height;
                _completionPopup.IsOpen=true; FocusEditor();
            });
        }
        catch(OperationCanceledException) { }
        catch(Exception e) {Message="Completion provider: "+e.Message;}
    }
    public void AcceptCompletion()
    {
        if(CompletionCount==0||_completionList?.SelectedItem is not Completion selected) return;
        int start=_completionStart; long revision=_completionRevision; CloseCompletion();
        if(!CanEdit||Session.Document.Revision!=revision) return;
        Session.Select(new Selection(start,Session.Primary.Caret));
        if(selected.IsSnippet) InsertSnippet(selected.InsertText); else Session.Insert(selected.InsertText);
        RevealCaret(); FocusEditor();
    }
    internal bool HandleCompletionKey(KeyEventArgs e)
    {
        if(CompletionCount==0) return false;
        switch(e.Key)
        {
            case Key.Up: _completionList!.SelectedIndex=(_completionList.SelectedIndex+_completionItems.Count-1)%_completionItems.Count; break;
            case Key.Down: _completionList!.SelectedIndex=(_completionList.SelectedIndex+1)%_completionItems.Count; break;
            case Key.Enter: case Key.Tab: AcceptCompletion(); break;
            case Key.Escape: CloseCompletion(); break;
            default: CloseCompletion(); return false;
        }
        e.Handled=true; return true;
    }
    public void CloseCompletion()
    {
        _completionCancellation?.Cancel(); _completionCancellation?.Dispose(); _completionCancellation=null;
        if(_completionPopup is not null) _completionPopup.IsOpen=false; _completionItems=[];
    }
    private void CancelDiagnostics() {_diagnosticCancellation?.Cancel(); _diagnosticCancellation?.Dispose(); _diagnosticCancellation=null;}
}
