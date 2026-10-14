namespace ProText.Core;
public enum DiffKind { Equal,Insert,Delete }
public sealed record DiffRow(DiffKind Kind,int? LeftLine,int? RightLine,string Text);
public sealed record DiffHunk(int LeftStart,int LeftCount,int RightStart,int RightCount);
public sealed record DiffResult(IReadOnlyList<DiffRow> Rows,IReadOnlyList<DiffHunk> Hunks,bool IsCoarse);
/// <summary>Exact LCS within a memory budget, with a bounded coarse fallback for very different large documents.</summary>
public static class LineDiff
{
    public static DiffResult Compare(string left,string right,bool ignoreWhitespace=false,int maxCells=2_000_000,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(left); ArgumentNullException.ThrowIfNull(right); if(maxCells<1) throw new ArgumentOutOfRangeException(nameof(maxCells));
        var a=Split(left); var b=Split(right); string Key(string s)=>ignoreWhitespace?s.Trim():s; bool Equal(int i,int j)=>string.Equals(Key(a[i]),Key(b[j]),StringComparison.Ordinal);
        int prefix=0; while(prefix<a.Length&&prefix<b.Length&&Equal(prefix,prefix)) prefix++;
        int suffix=0; while(suffix<a.Length-prefix&&suffix<b.Length-prefix&&Equal(a.Length-suffix-1,b.Length-suffix-1)) suffix++;
        int n=a.Length-prefix-suffix,m=b.Length-prefix-suffix; var rows=new List<DiffRow>(a.Length+b.Length);
        for(int i=0;i<prefix;i++) rows.Add(new(DiffKind.Equal,i+1,i+1,a[i]));
        bool coarse=(long)(n+1)*(m+1)>maxCells;
        if(coarse)
        {
            for(int i=0;i<n;i++) rows.Add(new(DiffKind.Delete,prefix+i+1,null,a[prefix+i]));
            for(int j=0;j<m;j++) rows.Add(new(DiffKind.Insert,null,prefix+j+1,b[prefix+j]));
        }
        else
        {
            var matrix=new int[(n+1)*(m+1)]; int stride=m+1;
            for(int i=n-1;i>=0;i--) { cancellationToken.ThrowIfCancellationRequested(); for(int j=m-1;j>=0;j--) matrix[i*stride+j]=Equal(prefix+i,prefix+j)?matrix[(i+1)*stride+j+1]+1:Math.Max(matrix[(i+1)*stride+j],matrix[i*stride+j+1]); }
            int x=0,y=0;
            while(x<n||y<m)
            {
                if(x<n&&y<m&&Equal(prefix+x,prefix+y)) { rows.Add(new(DiffKind.Equal,prefix+x+1,prefix+y+1,a[prefix+x])); x++;y++; }
                else if(y<m&&(x==n||matrix[x*stride+y+1]>matrix[(x+1)*stride+y])) { rows.Add(new(DiffKind.Insert,null,prefix+y+1,b[prefix+y])); y++; }
                else { rows.Add(new(DiffKind.Delete,prefix+x+1,null,a[prefix+x])); x++; }
            }
        }
        for(int i=suffix;i>0;i--) rows.Add(new(DiffKind.Equal,a.Length-i+1,b.Length-i+1,a[a.Length-i]));
        var hunks=new List<DiffHunk>(); int leftLine=1,rightLine=1,index=0;
        while(index<rows.Count)
        {
            if(rows[index].Kind==DiffKind.Equal) { leftLine++;rightLine++;index++;continue; }
            int sl=leftLine,sr=rightLine; while(index<rows.Count&&rows[index].Kind!=DiffKind.Equal) { if(rows[index].Kind==DiffKind.Delete) leftLine++; else rightLine++; index++; } hunks.Add(new(sl,leftLine-sl,sr,rightLine-sr));
        }
        return new(rows,hunks,coarse);
    }
    private static string[] Split(string text)=>new LineMap(text).Lines.Select(l=>text.Substring(l.Offset,l.Length)).ToArray();
}
