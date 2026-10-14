using System.Diagnostics;
using System.Text.RegularExpressions;
namespace Scribe.Core;
public sealed record SearchQuery(string Pattern,bool CaseSensitive=false,bool WholeWord=false,bool Regex=false,int Limit=10000,int TimeoutMs=150);
public sealed record SearchMatch(int Start,int Length) { public int End=>Start+Length; }
public sealed record SearchResults(IReadOnlyList<SearchMatch> Matches,bool Truncated=false,string? Error=null);
public static class Search
{
    private static Regex Compile(SearchQuery q)
    {
        if(q.Limit<1||q.Limit>1_000_000||q.TimeoutMs<1||q.TimeoutMs>5000) throw new ArgumentException("Invalid search budget.");
        string pattern=q.Regex?q.Pattern:System.Text.RegularExpressions.Regex.Escape(q.Pattern);
        if(q.WholeWord) pattern=@"(?<![\p{L}\p{M}\p{Nd}\p{Pc}])(?:"+pattern+@")(?![\p{L}\p{M}\p{Nd}\p{Pc}])";
        return new(pattern,RegexOptions.CultureInvariant|RegexOptions.Multiline|(q.CaseSensitive?RegexOptions.None:RegexOptions.IgnoreCase),TimeSpan.FromMilliseconds(q.TimeoutMs));
    }
    private static IEnumerable<Match> Matches(string text,SearchQuery query,Selection? scope,CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(text); ArgumentNullException.ThrowIfNull(query); ArgumentNullException.ThrowIfNull(query.Pattern);
        int start=scope?.Start??0,end=scope?.End??text.Length;
        if(start<0||end>text.Length) throw new ArgumentException("Invalid search scope.");
        token.ThrowIfCancellationRequested(); if(query.Pattern.Length==0) yield break;
        var watch=Stopwatch.StartNew(); var regex=Compile(query);
        for(var match=regex.Match(text,start);match.Success&&match.Index<=end;match=match.NextMatch())
        {
            token.ThrowIfCancellationRequested(); if(watch.ElapsedMilliseconds>Math.Max(500,query.TimeoutMs*4)) throw new TimeoutException("Search time budget exceeded.");
            if(match.Index+match.Length<=end) yield return match;
        }
    }
    public static SearchResults Find(string text,SearchQuery query,Selection? scope=null,CancellationToken token=default)
    {
        var results=new List<SearchMatch>();
        try
        {
            foreach(var m in Matches(text,query,scope,token)) { if(results.Count==query.Limit) return new(results,true); results.Add(new(m.Index,m.Length)); }
            return new(results);
        }
        catch(Exception e) when(e is ArgumentException or RegexMatchTimeoutException or TimeoutException) { return new([],Error:e.Message); }
    }
    public static IReadOnlyList<TextEdit> Replace(string text,SearchQuery query,string replacement,Selection? scope=null,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(replacement); var results=new List<TextEdit>();
        foreach(var m in Matches(text,query,scope,token))
        {
            if(results.Count==query.Limit) throw new InvalidOperationException("Replacement cancelled: result limit exceeded. Narrow the search.");
            results.Add(new(m.Index,m.Length,query.Regex?m.Result(replacement):replacement));
        }
        return results;
    }
}
