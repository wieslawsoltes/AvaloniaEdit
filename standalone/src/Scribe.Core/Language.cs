namespace Scribe.Core;
public enum TokenKind { Text,Keyword,Number,String,Comment,Type }
public sealed record SyntaxToken(int Start,int Length,TokenKind Kind);
public sealed record Diagnostic(int Start,int Length,string Message,bool IsError=true);
public sealed record Completion(string Label,string InsertText,string? Description=null,bool IsSnippet=false);
public sealed record CompletionRequest(TextSnapshot Snapshot,int Offset,long Revision);
public interface ICompletionProvider { ValueTask<IReadOnlyList<Completion>> CompleteAsync(CompletionRequest request,CancellationToken token); }
public interface IDiagnosticProvider { ValueTask<IReadOnlyList<Diagnostic>> AnalyzeAsync(TextSnapshot snapshot,long revision,CancellationToken token); }
public sealed record FoldRegion(int FirstLine,int LastLine,string Label);
public static class LexicalLanguage
{
    private static readonly HashSet<string> Keywords=new("using namespace class record struct interface enum public private protected internal static readonly sealed partial void var string int double bool new return if else switch case break for foreach while in is as null true false async await Task try catch finally throw get set init const let function import from export def with None True False".Split(' '),StringComparer.Ordinal);
    public static IReadOnlyList<SyntaxToken> Tokenize(string line)
    {
        var tokens=new List<SyntaxToken>(); int p=0;
        while(p<line.Length)
        {
            int start=p; char c=line[p++];
            if(c=='/'&&p<line.Length&&line[p]=='/'||c=='#') { tokens.Add(new(start,line.Length-start,TokenKind.Comment)); break; }
            if(c is '"' or '\'') { while(p<line.Length) { char next=line[p++]; if(next=='\\'&&p<line.Length) p++; else if(next==c) break; } tokens.Add(new(start,p-start,TokenKind.String)); }
            else if(char.IsDigit(c)) { while(p<line.Length&&(char.IsLetterOrDigit(line[p])||line[p] is '.' or '_')) p++; tokens.Add(new(start,p-start,TokenKind.Number)); }
            else if(char.IsLetter(c)||c=='_') { while(p<line.Length&&EditSession.IsWord(line[p])) p++; string word=line[start..p]; if(Keywords.Contains(word)) tokens.Add(new(start,p-start,TokenKind.Keyword)); else if(char.IsUpper(c)) tokens.Add(new(start,p-start,TokenKind.Type)); }
        }
        return tokens;
    }
    public static IReadOnlyList<FoldRegion> Folds(TextSnapshot snapshot,int maximumLength=2_000_000)
    {
        if(snapshot.Length>maximumLength) return [];
        var stack=new Stack<(char Brace,int Line)>(); var folds=new List<FoldRegion>(); bool block=false; char quote='\0';
        for(int lineIndex=0;lineIndex<snapshot.LineCount;lineIndex++)
        {
            var line=snapshot.GetLine(lineIndex); string text=snapshot.GetText(line.Start,line.Length);
            for(int i=0;i<text.Length;i++)
            {
                char c=text[i],next=i+1<text.Length?text[i+1]:'\0';
                if(block) { if(c=='*'&&next=='/') { block=false; i++; } continue; }
                if(quote!='\0') { if(c=='\\') i++; else if(c==quote) quote='\0'; continue; }
                if(c=='/'&&next=='/') break;
                if(c=='/'&&next=='*') { block=true; i++; continue; }
                if(c is '"' or '\'') { quote=c; continue; }
                if(c is '{' or '[' or '(') stack.Push((c,lineIndex));
                else if(c is '}' or ']' or ')')
                {
                    if(stack.TryPeek(out var open)&&(open.Brace,c) is ('{','}') or ('[',']') or ('(',')')) { stack.Pop(); if(lineIndex>open.Line) folds.Add(new(open.Line,lineIndex,"…")); }
                    else stack.Clear();
                }
            }
            quote='\0';
        }
        return folds.OrderBy(f=>f.FirstLine).ThenByDescending(f=>f.LastLine).ToArray();
    }
}
