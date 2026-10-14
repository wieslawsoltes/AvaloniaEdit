using System.Text;
namespace Scribe.Core;

public readonly record struct TextLine(int Index,int Start,int Length,int DelimiterLength)
{
    public int End=>Start+Length;
    public int TotalLength=>Length+DelimiterLength;
}

/// <summary>Immutable persistent AVL rope. Snapshots and undo roots share unchanged nodes.
/// Newline metadata handles CR, LF and CRLF even across leaf boundaries. All offsets are UTF-16.</summary>
public sealed class TextSnapshot
{
    private const int LeafLimit=1024;
    private sealed class Node
    {
        public readonly Node? Left,Right;
        public readonly string? Text;
        public readonly int Length,Height,Breaks;
        public readonly char First,Last;
        public Node(string text)
        {
            Text=text; Length=text.Length; Height=1; First=text[0]; Last=text[^1];
            int count=0; for(int i=0;i<text.Length;i++) if(text[i]=='\r'||text[i]=='\n'&&(i==0||text[i-1]!='\r')) count++;
            Breaks=count;
        }
        public Node(Node left,Node right)
        {
            Left=left; Right=right; Length=checked(left.Length+right.Length); Height=1+Math.Max(left.Height,right.Height);
            First=left.First; Last=right.Last;
            Breaks=left.Breaks+right.Breaks-(left.Last=='\r'&&right.First=='\n'?1:0);
        }
    }
    private readonly Node? _root;
    private TextSnapshot(Node? root)=>_root=root;
    public TextSnapshot(string text) { ArgumentNullException.ThrowIfNull(text); _root=Build(text,0,text.Length); }
    public static TextSnapshot Empty { get; }=new((Node?)null);
    public int Length=>_root?.Length??0;
    public int LineCount=>(_root?.Breaks??0)+1;
    public int TreeHeight=>_root?.Height??0;
    public char this[int offset]
    {
        get
        {
            if((uint)offset>=(uint)Length) throw new ArgumentOutOfRangeException(nameof(offset));
            var n=_root!;
            while(n.Text is null) { if(offset<n.Left!.Length) n=n.Left; else { offset-=n.Left.Length; n=n.Right!; } }
            return n.Text[offset];
        }
    }
    public TextSnapshot Replace(int start,int length,string text)
    {
        Validate(start,length); ArgumentNullException.ThrowIfNull(text);
        if(length==0&&text.Length==0) return this;
        _=checked(Length-length+text.Length);
        var (left,tail)=Split(_root,start); var (_,right)=Split(tail,length);
        return new(Join(Join(left,Build(text,0,text.Length)),right));
    }
    public string GetText(int start,int length)
    {
        Validate(start,length); if(length==0) return "";
        var result=new StringBuilder(length); Append(_root,start,length,result); return result.ToString();
    }
    public override string ToString()=>GetText(0,Length);
    public int GetLineStart(int index)
    {
        if((uint)index>=(uint)LineCount) throw new ArgumentOutOfRangeException(nameof(index));
        return index==0?0:BreakEnd(_root!,index-1);
    }
    public TextLine GetLine(int index)
    {
        int start=GetLineStart(index),next=index+1<LineCount?GetLineStart(index+1):Length,delimiter=0;
        if(next>start&&this[next-1]=='\n') { delimiter=1; if(next>start+1&&this[next-2]=='\r') delimiter=2; }
        else if(next>start&&this[next-1]=='\r') delimiter=1;
        return new(index,start,next-start-delimiter,delimiter);
    }
    public int LineAt(int offset)
    {
        if((uint)offset>(uint)Length) throw new ArgumentOutOfRangeException(nameof(offset));
        int low=0,high=LineCount-1;
        while(low<high) { int mid=(low+high+1)/2; if(GetLineStart(mid)<=offset) low=mid; else high=mid-1; }
        return low;
    }
    public void Validate(int start,int length)
    {
        if(start<0||length<0||start>Length||length>Length-start) throw new ArgumentOutOfRangeException(nameof(start),"Range must be contained in the snapshot.");
    }
    public void ValidateTree()=>ValidateNode(_root);
    private static int ValidateNode(Node? n)
    {
        if(n is null) return 0;
        if(n.Text is not null) { if(n.Text.Length!=n.Length||n.Length>LeafLimit) throw new InvalidOperationException("Leaf invariant."); return 1; }
        int a=ValidateNode(n.Left),b=ValidateNode(n.Right);
        if(Math.Abs(a-b)>1||n.Height!=Math.Max(a,b)+1||n.Length!=n.Left!.Length+n.Right!.Length) throw new InvalidOperationException("AVL invariant.");
        return n.Height;
    }
    private static Node? Build(string text,int start,int length)
    {
        if(length==0) return null;
        if(length<=LeafLimit) return new(text.Substring(start,length));
        int half=length/2; return new(Build(text,start,half)!,Build(text,start+half,length-half)!);
    }
    private static Node? Join(Node? a,Node? b)
    {
        if(a is null) return b; if(b is null) return a;
        if(a.Text is not null&&b.Text is not null&&a.Length+b.Length<=LeafLimit) return new(a.Text+b.Text);
        if(a.Height>b.Height+1) return Balance(new(a.Left!,Join(a.Right,b)!));
        if(b.Height>a.Height+1) return Balance(new(Join(a,b.Left)!,b.Right!));
        return new(a,b);
    }
    private static Node Balance(Node n)
    {
        var a=n.Left!; var b=n.Right!;
        if(a.Height>b.Height+1)
        {
            if(a.Left!.Height>=a.Right!.Height) return new(a.Left,new(a.Right,b));
            var c=a.Right; return new(new(a.Left,c.Left!),new(c.Right!,b));
        }
        if(b.Height>a.Height+1)
        {
            if(b.Right!.Height>=b.Left!.Height) return new(new(a,b.Left),b.Right);
            var c=b.Left; return new(new(a,c.Left!),new(c.Right!,b.Right));
        }
        return n;
    }
    private static (Node? Left,Node? Right) Split(Node? n,int offset)
    {
        if(n is null) return (null,null);
        if(offset==0) return (null,n); if(offset==n.Length) return (n,null);
        if(n.Text is not null) return (new(n.Text[..offset]),new(n.Text[offset..]));
        if(offset<n.Left!.Length) { var (a,b)=Split(n.Left,offset); return (a,Join(b,n.Right)); }
        var (c,d)=Split(n.Right,offset-n.Left.Length); return (Join(n.Left,c),d);
    }
    private static void Append(Node? n,int start,int count,StringBuilder target)
    {
        if(n is null||count==0) return;
        if(n.Text is not null) { target.Append(n.Text,start,count); return; }
        int left=n.Left!.Length;
        if(start<left) { int take=Math.Min(count,left-start); Append(n.Left,start,take,target); count-=take; start=0; }
        else start-=left;
        if(count>0) Append(n.Right,start,count,target);
    }
    private static int BreakEnd(Node n,int index)
    {
        if(n.Text is not null)
        {
            for(int i=0;i<n.Text.Length;i++)
            {
                if(n.Text[i] is not ('\r' or '\n')) continue;
                if(n.Text[i]=='\r'&&i+1<n.Text.Length&&n.Text[i+1]=='\n') i++;
                if(index--==0) return i+1;
            }
            throw new InvalidOperationException("Newline metadata invariant.");
        }
        var left=n.Left!; var right=n.Right!; bool cross=left.Last=='\r'&&right.First=='\n';
        if(index<left.Breaks)
        {
            if(cross&&index==left.Breaks-1) return left.Length+1;
            return BreakEnd(left,index);
        }
        return left.Length+BreakEnd(right,index-left.Breaks+(cross?1:0));
    }
}
