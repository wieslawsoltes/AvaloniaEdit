namespace Scribe.Avalonia;
/// <summary>Fenwick index of logical-line visual row counts. Unmeasured lines start at one row; folded lines occupy zero.</summary>
internal sealed class VisualRows
{
    private readonly int[] _weights,_tree;
    public int Count=>_weights.Length;
    public int Total=>Prefix(Count);
    public VisualRows(int count)
    {
        _weights=new int[Math.Max(1,count)]; Array.Fill(_weights,1); _tree=new int[_weights.Length+1];
        for(int i=1;i<_tree.Length;i++) _tree[i]=i&-i;
    }
    public int this[int index]=>_weights[index];
    public void Set(int index,int value)
    {
        value=Math.Max(0,value); int delta=value-_weights[index]; if(delta==0) return;
        _weights[index]=value; for(int i=index+1;i<_tree.Length;i+=i&-i) _tree[i]+=delta;
    }
    public int Prefix(int count) { int sum=0; for(int i=Math.Clamp(count,0,Count);i>0;i-=i&-i) sum+=_tree[i]; return sum; }
    public int Find(int visualRow)
    {
        visualRow=Math.Clamp(visualRow,0,Math.Max(0,Total-1)); int index=0,sum=0,bit=1;
        while(bit<Count) bit<<=1;
        for(;bit!=0;bit>>=1) { int next=index+bit; if(next<=Count&&sum+_tree[next]<=visualRow) { index=next; sum+=_tree[next]; } }
        return Math.Min(index,Count-1);
    }
}
