using System.Text;
using System.Text.RegularExpressions;
namespace Scribe.Core;
public sealed record SnippetStop(int Index,int Start,int Length);
public sealed record SnippetExpansion(string Text,IReadOnlyList<SnippetStop> Stops);
/// <summary>Original ${1:default}, $1, ${1} and $0 snippet expansion. Nested syntax is intentionally rejected.</summary>
public static class Snippets
{
    public static SnippetExpansion Expand(string template)
    {
        ArgumentNullException.ThrowIfNull(template); if(template.Length>1_000_000) throw new ArgumentException("Snippet too long.");
        var regex=new Regex(@"(?<!\\)\$(?:\{(?<n>\d+)(?::(?<d>[^{}]*))?\}|(?<n>\d+))",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        var matches=regex.Matches(template).Cast<Match>().ToArray(); var defaults=new Dictionary<int,string>();
        foreach(var m in matches) if(m.Groups["d"].Success) defaults.TryAdd(int.Parse(m.Groups["n"].Value),m.Groups["d"].Value);
        var b=new StringBuilder(); var stops=new List<SnippetStop>(); int p=0;
        foreach(var m in matches)
        {
            b.Append(template,p,m.Index-p); int index=int.Parse(m.Groups["n"].Value); string value=defaults.GetValueOrDefault(index,"");
            stops.Add(new(index,b.Length,value.Length)); b.Append(value); p=m.Index+m.Length;
        }
        b.Append(template,p,template.Length-p);
        if(!stops.Any(s=>s.Index==0)) stops.Add(new(0,b.Length,0));
        return new(b.ToString(),stops);
    }
}
public sealed class SnippetSession
{
    private readonly EditSession _view;
    private readonly SortedDictionary<int,List<(TextAnchor Start,TextAnchor End)>> _groups=[];
    private readonly int[] _order;
    private int _index=-1;
    public bool IsActive { get; private set; }=true;
    public SnippetSession(EditSession view,string template)
    {
        _view=view; if(view.Selections.Count!=1) throw new InvalidOperationException("Start snippets with one selection; linked placeholders create their own selections.");
        if(!view.CanEdit) throw new InvalidOperationException("View is read-only.");
        var expanded=Snippets.Expand(template); int start=view.Primary.Start; view.Insert(expanded.Text);
        foreach(var stop in expanded.Stops)
        {
            if(!_groups.TryGetValue(stop.Index,out var list)) _groups[stop.Index]=list=[];
            list.Add((view.Document.CreateAnchor(start+stop.Start,Affinity.Before),view.Document.CreateAnchor(start+stop.Start+stop.Length,Affinity.After)));
        }
        _order=_groups.Keys.Where(k=>k!=0).Append(0).ToArray(); Next();
    }
    public void Next(bool backwards=false)
    {
        if(!IsActive) return;
        _index=Math.Clamp(_index+(backwards?-1:1),0,_order.Length-1); int key=_order[_index];
        _view.Select(_groups[key].Select(a=>new Selection(a.Start.Offset,a.End.Offset)).ToArray());
        if(key==0) IsActive=false;
    }
    public void Cancel()=>IsActive=false;
}
