using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Scribe.Core;
namespace Scribe.Avalonia;
public sealed class SearchController : INotifyPropertyChanged,IDisposable
{
    private readonly ScribeEditor _owner;
    private readonly DispatcherTimer _timer;
    private string _pattern="",_replacement="",_status="Find in document";
    private bool _case,_word,_regex,_selection;
    private TextAnchor? _start,_end;
    private long _revision=-1;
    public SearchResults Results {get;private set;}=new([]);
    public string Pattern {get=>_pattern;set{if(Set(ref _pattern,value??"")) Schedule();}}
    public string Replacement {get=>_replacement;set=>Set(ref _replacement,value??"");}
    public string Status {get=>_status;private set=>Set(ref _status,value);}
    public bool CaseSensitive {get=>_case;set{if(Set(ref _case,value)) Schedule();}}
    public bool WholeWord {get=>_word;set{if(Set(ref _word,value)) Schedule();}}
    public bool UseRegex {get=>_regex;set{if(Set(ref _regex,value)) Schedule();}}
    public bool InSelection
    {
        get=>_selection;
        set
        {
            if(value&&_owner.Session.Primary.Length==0) {Status="Select text before restricting the search."; Notify(); return;}
            if(!Set(ref _selection,value)) return;
            var s=_owner.Session.Primary;
            _start=value?_owner.Session.Document.CreateAnchor(s.Start,Affinity.Before):null;
            _end=value?_owner.Session.Document.CreateAnchor(s.End,Affinity.After):null; Schedule();
        }
    }
    public Selection? Scope=>_start is not null&&_end is not null?new(_start.Offset,_end.Offset):null;
    public SearchQuery Query=>new(Pattern,CaseSensitive,WholeWord,UseRegex);
    public EditorCommand NextCommand {get;}
    public EditorCommand PreviousCommand {get;}
    public EditorCommand ReplaceCommand {get;}
    public EditorCommand ReplaceAllCommand {get;}
    public EditorCommand CloseCommand {get;}
    public event PropertyChangedEventHandler? PropertyChanged;
    public SearchController(ScribeEditor owner)
    {
        _owner=owner; _timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(120)}; _timer.Tick+=(_,_)=>{_timer.Stop();Refresh();};
        NextCommand=new(()=>Navigate()); PreviousCommand=new(()=>Navigate(true)); ReplaceCommand=new(()=>Replace(false),()=>owner.CanEdit); ReplaceAllCommand=new(()=>Replace(true),()=>owner.CanEdit);
        CloseCommand=new(()=>{owner.IsSearchOpen=false;owner.FocusEditor();});
    }
    public void Schedule(){_revision=-1;_timer.Stop();if(_owner.IsSearchOpen) _timer.Start();}
    public void Refresh()
    {
        if(_owner.Session is null) return;
        Results=Core.Search.Find(_owner.Session.Document.Snapshot.ToString(),Query,Scope); _revision=_owner.Session.Document.Revision;
        Status=Results.Error??(Pattern.Length==0?"Find in document":$"{Results.Matches.Count:N0}{(Results.Truncated?"+":"")} matches");
        _owner.Viewport.InvalidateVisual(); Notify(nameof(Results));
    }
    public void Navigate(bool backwards=false)
    {
        if(_owner.Session.Document.Revision!=_revision) Refresh(); var hits=Results.Matches; if(hits.Count==0) return;
        var current=_owner.Session.Primary; int found=-1;
        for(int i=0;i<hits.Count;i++) if(hits[i].Start==current.Start&&hits[i].Length==current.Length){found=i;break;}
        int index=found>=0?(found+(backwards?-1:1)+hits.Count)%hits.Count:backwards?hits.ToList().FindLastIndex(h=>h.Start<current.Start):hits.ToList().FindIndex(h=>h.Start>=current.Caret);
        if(index<0) index=backwards?hits.Count-1:0;
        var hit=hits[index]; _owner.Session.Select(new Selection(hit.Start,hit.End)); _owner.RevealCaret(); Status=$"{index+1:N0} / {hits.Count:N0}";
    }
    public void Replace(bool all)
    {
        if(!_owner.CanEdit) return;
        try
        {
            var document=_owner.Session.Document; if(document.Revision!=_revision) Refresh();
            var edits=Core.Search.Replace(document.Snapshot.ToString(),Query,Replacement,Scope);
            if(!all)
            {
                var s=_owner.Session.Primary; var selected=edits.Where(e=>e.Start==s.Start&&e.Length==s.Length).Take(1).ToArray();
                if(selected.Length==0){Navigate();return;} edits=selected;
            }
            document.Apply(edits,_owner.Session.Id,expectedRevision:_revision); Refresh(); Status=$"Replaced {edits.Count:N0} matches"; _owner.RevealCaret();
        }
        catch(Exception e) {Status=e.Message;}
    }
    internal void ResetScope(){_selection=false;_start=_end=null;_revision=-1;Notify(nameof(InSelection));if(_owner.IsSearchOpen)Refresh();}
    internal void RefreshCommands(){ReplaceCommand.Refresh();ReplaceAllCommand.Refresh();}
    private bool Set<T>(ref T field,T value,[CallerMemberName]string? name=null){if(EqualityComparer<T>.Default.Equals(field,value))return false;field=value;Notify(name);return true;}
    private void Notify([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
    public void Dispose(){_timer.Stop();PropertyChanged=null;}
}
public class SearchBar : TemplatedControl
{
    public static readonly StyledProperty<SearchController?> ControllerProperty=AvaloniaProperty.Register<SearchBar,SearchController?>(nameof(Controller));
    public SearchController? Controller {get=>GetValue(ControllerProperty);set=>SetValue(ControllerProperty,value);}
    private TextBox? _query;
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e){base.OnApplyTemplate(e);_query=e.NameScope.Find<TextBox>("PART_Query");}
    public void FocusQuery(){_query?.Focus();_query?.SelectAll();}
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e); if(e.Key==Key.Enter){Controller?.Navigate(e.KeyModifiers.HasFlag(KeyModifiers.Shift));e.Handled=true;}
        else if(e.Key==Key.Escape){Controller?.CloseCommand.Execute(null);e.Handled=true;}
    }
}
