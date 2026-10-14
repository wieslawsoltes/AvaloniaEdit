using System.Diagnostics;
using System.Text.RegularExpressions;
namespace ProText.Core;
public sealed record SearchOptions(string Pattern, bool MatchCase=false, bool WholeWord=false, bool UseRegex=false, int MaxMatches=10000, int TimeoutMilliseconds=150);
public sealed record SearchHit(TextRange Range, string Value);
public sealed record SearchResult(IReadOnlyList<SearchHit> Hits, bool IsTruncated, string? Error, TimeSpan Elapsed) { public bool IsValid => Error is null; }
/// <summary>Culture-independent, bounded search. Replacement plans are complete or fail without mutation.</summary>
public static class SearchEngine
{
    private static Regex CreateRegex(SearchOptions options)
    {
        if(options.MaxMatches is < 1 or > 1_000_000) throw new ArgumentOutOfRangeException(nameof(options.MaxMatches));
        if(options.TimeoutMilliseconds is < 1 or > 5000) throw new ArgumentOutOfRangeException(nameof(options.TimeoutMilliseconds));
        string pattern=options.UseRegex ? options.Pattern : Regex.Escape(options.Pattern);
        if(options.WholeWord) pattern=@"(?<![\p{L}\p{M}\p{Nd}\p{Pc}])(?:"+pattern+@")(?![\p{L}\p{M}\p{Nd}\p{Pc}])";
        var flags=RegexOptions.CultureInvariant|RegexOptions.Multiline;
        if(!options.MatchCase) flags|=RegexOptions.IgnoreCase;
        return new Regex(pattern,flags,TimeSpan.FromMilliseconds(options.TimeoutMilliseconds));
    }
    public static SearchResult Find(string text, SearchOptions options, TextRange? scope=null, CancellationToken cancellationToken=default)
    {
        var watch=Stopwatch.StartNew(); var hits=new List<SearchHit>();
        try
        {
            foreach(var match in Enumerate(text,options,scope,cancellationToken,watch))
            {
                if(hits.Count==options.MaxMatches) return new(hits,true,null,watch.Elapsed);
                hits.Add(new(new(match.Index,match.Length),match.Value));
            }
            return new(hits,false,null,watch.Elapsed);
        }
        catch(ArgumentException ex) { return new([],false,ex.Message,watch.Elapsed); }
        catch(RegexMatchTimeoutException) { return new([],false,"Search exceeded the regular expression time limit.",watch.Elapsed); }
        catch(TimeoutException) { return new([],false,"Search exceeded the total time limit.",watch.Elapsed); }
    }
    public static IReadOnlyList<TextEdit> CreateReplacementEdits(string text, SearchOptions options, string replacement, TextRange? scope=null, CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(replacement); var watch=Stopwatch.StartNew(); var edits=new List<TextEdit>();
        foreach(var match in Enumerate(text,options,scope,cancellationToken,watch))
        {
            if(edits.Count==options.MaxMatches) throw new InvalidOperationException("Too many matches. Narrow the selection or raise MaxMatches before replacing all.");
            edits.Add(new(new(match.Index,match.Length),options.UseRegex ? match.Result(replacement) : replacement));
        }
        return edits;
    }
    private static IEnumerable<Match> Enumerate(string text,SearchOptions options,TextRange? scope,CancellationToken token,Stopwatch watch)
    {
        ArgumentNullException.ThrowIfNull(text); ArgumentNullException.ThrowIfNull(options); ArgumentNullException.ThrowIfNull(options.Pattern);
        var range=scope ?? new TextRange(0,text.Length); range.ValidateFor(text);
        if(options.Pattern.Length==0) yield break;
        var regex=CreateRegex(options); var match=regex.Match(text,range.Offset);
        while(match.Success && match.Index<=range.End)
        {
            token.ThrowIfCancellationRequested();
            if(watch.ElapsedMilliseconds>Math.Max(1000,options.TimeoutMilliseconds*4)) throw new TimeoutException();
            if(match.Index+match.Length<=range.End) yield return match;
            match=match.NextMatch();
        }
    }
}
