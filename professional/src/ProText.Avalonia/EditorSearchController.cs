using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AvaloniaEdit.Document;
using ProText.Core;
using ProText.Avalonia.Controls;
namespace ProText.Avalonia;
public sealed class EditorSearchController : INotifyPropertyChanged
{
    private readonly ProTextEditor _owner;
    private string _pattern="",_replacement="",_message="";
    private bool _matchCase,_wholeWord,_useRegex,_inSelection;
    private SearchResult _result=new([],false,null,TimeSpan.Zero);
    private TextAnchor? _scopeStart,_scopeEnd;
    private long _revision=-1;
    public EditorSearchController(ProTextEditor owner)
    {
        _owner=owner;
        NextCommand=new(()=>Navigate()); PreviousCommand=new(()=>Navigate(true));
        ReplaceCommand=new(ReplaceCurrent,()=>!_owner.EffectiveReadOnly);
        ReplaceAllCommand=new(ReplaceAll,()=>!_owner.EffectiveReadOnly);
        CloseCommand=new(()=>{ _owner.IsSearchOpen=false; _owner.FocusEditor(); });
    }
    public string Pattern { get=>_pattern; set { if(Set(ref _pattern,value??"")) Schedule(); } }
    public string Replacement { get=>_replacement; set=>Set(ref _replacement,value??""); }
    public bool MatchCase { get=>_matchCase; set { if(Set(ref _matchCase,value)) Schedule(); } }
    public bool WholeWord { get=>_wholeWord; set { if(Set(ref _wholeWord,value)) Schedule(); } }
    public bool UseRegex { get=>_useRegex; set { if(Set(ref _useRegex,value)) Schedule(); } }
    public bool InSelection
    {
        get=>_inSelection;
        set
        {
            if(value && _owner.Editor.SelectionLength==0) { Message="Select text before restricting the search."; Notify(); return; }
            if(!Set(ref _inSelection,value)) return;
            _scopeStart=_scopeEnd=null;
            if(value)
            {
                _scopeStart=_owner.Editor.Document.CreateAnchor(_owner.Editor.SelectionStart); _scopeStart.SurviveDeletion=true; _scopeStart.MovementType=AnchorMovementType.BeforeInsertion;
                _scopeEnd=_owner.Editor.Document.CreateAnchor(_owner.Editor.SelectionStart+_owner.Editor.SelectionLength); _scopeEnd.SurviveDeletion=true; _scopeEnd.MovementType=AnchorMovementType.AfterInsertion;
            }
            Schedule();
        }
    }
    public string Message { get=>_message; private set=>Set(ref _message,value); }
    public bool HasError=>_result.Error is not null;
    public IReadOnlyList<SearchHit> Hits=>_result.Hits;
    public SearchOptions Options=>new(Pattern,MatchCase,WholeWord,UseRegex);
    public TextRange? Scope=>_inSelection && _scopeStart is not null && _scopeEnd is not null ? new(_scopeStart.Offset,Math.Max(0,_scopeEnd.Offset-_scopeStart.Offset)) : null;
    public EditorCommand NextCommand { get; }
    public EditorCommand PreviousCommand { get; }
    public EditorCommand ReplaceCommand { get; }
    public EditorCommand ReplaceAllCommand { get; }
    public EditorCommand CloseCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? ResultsChanged;
    private void Schedule() { _revision=-1; _owner.ScheduleSearch(); }
    public void Refresh()
    {
        var session=_owner.Session;
        _result=SearchEngine.Find(session?.Text??"",Options,Scope);
        _revision=session?.Revision??-1;
        Message=_result.Error??(Pattern.Length==0?"Find in document":Hits.Count==0?"No results":$"{Hits.Count:N0}{(_result.IsTruncated?"+":"")} results");
        Notify(nameof(Hits)); Notify(nameof(HasError)); UpdateReadOnly(); ResultsChanged?.Invoke(this,EventArgs.Empty);
    }
    private void EnsureFresh() { if(_revision!=_owner.Session?.Revision) Refresh(); }
    public void Navigate(bool backwards=false)
    {
        EnsureFresh(); if(Hits.Count==0) return;
        int start=_owner.Editor.SelectionStart, length=_owner.Editor.SelectionLength;
        int current=-1;
        for(int i=0;i<Hits.Count;i++) if(Hits[i].Range.Offset==start && Hits[i].Range.Length==length) { current=i; break; }
        int index;
        if(current>=0) index=(current+(backwards?-1:1)+Hits.Count)%Hits.Count;
        else if(backwards) { index=Hits.Count-1; for(int i=Hits.Count-1;i>=0;i--) if(Hits[i].Range.Offset<start) { index=i; break; } }
        else { index=0; for(int i=0;i<Hits.Count;i++) if(Hits[i].Range.Offset>=start) { index=i; break; } }
        _owner.Reveal(Hits[index].Range); Message=$"{index+1:N0} of {Hits.Count:N0}";
    }
    public void ReplaceCurrent()
    {
        if(_owner.EffectiveReadOnly || _owner.Session is null) return;
        EnsureFresh(); var selected=new TextRange(_owner.Editor.SelectionStart,_owner.Editor.SelectionLength);
        if(!Hits.Any(h=>h.Range==selected)) { Navigate(); return; }
        TryReplace(()=>
        {
            var session=_owner.Session!;
            var edit=SearchEngine.CreateReplacementEdits(session.Text,Options,Replacement,Scope).First(e=>e.Range==selected);
            session.ApplyEdits([edit],_revision);
            _owner.Editor.Select(edit.Range.Offset+edit.NewText.Length,0); Refresh(); Navigate();
        });
    }
    public void ReplaceAll()
    {
        if(_owner.EffectiveReadOnly || _owner.Session is null) return;
        EnsureFresh();
        TryReplace(()=>
        {
            var session=_owner.Session!; var edits=SearchEngine.CreateReplacementEdits(session.Text,Options,Replacement,Scope);
            session.ApplyEdits(edits,_revision); Refresh(); Message=$"Replaced {edits.Count:N0} matches";
        });
    }
    private void TryReplace(Action action)
    {
        try { action(); }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or RegexMatchTimeoutException or TimeoutException) { Message=ex.Message; }
    }
    internal void SessionChanged() { _scopeStart=_scopeEnd=null; _inSelection=false; Notify(nameof(InSelection)); _revision=-1; Refresh(); }
    internal void UpdateReadOnly() { ReplaceCommand.Refresh(); ReplaceAllCommand.Refresh(); }
    private bool Set<T>(ref T field,T value,[CallerMemberName]string? name=null)
    {
        if(EqualityComparer<T>.Default.Equals(field,value)) return false; field=value; Notify(name); return true;
    }
    private void Notify([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
}
