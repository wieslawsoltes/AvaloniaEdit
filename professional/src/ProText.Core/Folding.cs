namespace ProText.Core;
public sealed record FoldingRegion(int StartOffset,int EndOffset,string Name);
public interface IFoldingProvider { IReadOnlyList<FoldingRegion> GetFoldings(string text,CancellationToken cancellationToken=default); }
/// <summary>Linear lexical folding for brace languages; ignores strings, characters and comments.</summary>
public sealed class BraceFoldingProvider : IFoldingProvider
{
    public IReadOnlyList<FoldingRegion> GetFoldings(string text,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(text); var stack=new Stack<(char Brace,int Offset,int Line)>(); var regions=new List<FoldingRegion>(); int line=0,i=0;
        bool NewLine(int p)=>text[p]=='\n'||(text[p]=='\r'&&(p+1==text.Length||text[p+1]!='\n'));
        while(i<text.Length)
        {
            if((i&4095)==0) cancellationToken.ThrowIfCancellationRequested(); char c=text[i]; if(NewLine(i)) line++;
            if(c=='/'&&i+1<text.Length&&text[i+1]=='/') { i+=2; while(i<text.Length&&text[i] is not ('\r' or '\n')) i++; continue; }
            if(c=='/'&&i+1<text.Length&&text[i+1]=='*')
            {
                int start=i,startLine=line; i+=2;
                while(i<text.Length) { if(NewLine(i)) line++; if(text[i]=='*'&&i+1<text.Length&&text[i+1]=='/') { i+=2; if(line>startLine) regions.Add(new(start,i,"/* … */")); break; } i++; }
                continue;
            }
            if(c is '\'' or '"')
            {
                char quote=c; bool verbatim=quote=='"'&&i>0&&text[i-1]=='@'; int run=1; while(quote=='"'&&i+run<text.Length&&text[i+run]==quote) run++;
                if(run>=3)
                {
                    i+=run; while(i<text.Length) { if(NewLine(i)) line++; int close=0; while(i+close<text.Length&&text[i+close]=='"') close++; if(close>=run) { i+=run; break; } i+=Math.Max(1,close); } continue;
                }
                i++; while(i<text.Length)
                {
                    if(NewLine(i)) line++; if(!verbatim&&text[i]=='\\') { i=Math.Min(text.Length,i+2); continue; }
                    if(text[i]==quote) { if(verbatim&&i+1<text.Length&&text[i+1]==quote) { i+=2; continue; } i++; break; } i++;
                }
                continue;
            }
            if(c is '{' or '[' or '(') stack.Push((c,i,line));
            else if(c is '}' or ']' or ')')
            {
                if(stack.TryPeek(out var open)&&Matches(open.Brace,c)) { stack.Pop(); if(line>open.Line) regions.Add(new(open.Offset,i+1,$"{open.Brace} … {c}")); }
                else stack.Clear();
            }
            i++;
        }
        return regions.OrderBy(r=>r.StartOffset).ThenByDescending(r=>r.EndOffset).ToArray();
    }
    private static bool Matches(char a,char b)=>(a,b) is ('{','}') or ('[',']') or ('(',')');
}
