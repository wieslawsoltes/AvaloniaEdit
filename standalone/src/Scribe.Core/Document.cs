namespace Scribe.Core;
public readonly record struct TextEdit(int Start,int Length,string Text) { public int End=>checked(Start+Length); }
public readonly record struct Selection(int Anchor,int Caret)
{
    public int Start=>Math.Min(Anchor,Caret);
    public int End=>Math.Max(Anchor,Caret);
    public int Length=>End-Start;
    public static Selection At(int offset)=>new(offset,offset);
}
public sealed record DocumentChange(TextSnapshot Before,TextSnapshot After,IReadOnlyList<TextEdit> Edits,Guid Origin,IReadOnlyList<Selection>? RestoreSelection=null);
public enum Affinity { Before,After }
public sealed class TextAnchor
{
    public int Offset { get; internal set; }
    public Affinity Affinity { get; }
    internal TextAnchor(int offset,Affinity affinity) { Offset=offset; Affinity=affinity; }
}

/// <summary>UI-independent transactional document. Single-writer ownership is required; immutable snapshots are thread-safe.</summary>
public sealed class Document
{
    private sealed record Step(TextSnapshot Before,TextSnapshot After,TextEdit[] Forward,TextEdit[] Inverse,Guid Owner,Selection[] BeforeSelection,Selection[] AfterSelection);
    private sealed class History(string? key,Guid owner)
    {
        public string? Key=key; public Guid Owner=owner; public long Timestamp=Environment.TickCount64;
        public List<Step> Steps=[];
    }
    private readonly List<History> _undo=[],_redo=[];
    private readonly List<WeakReference<TextAnchor>> _anchors=[];
    private TextSnapshot _saved;
    private bool _readOnly,_notifying;
    public Document(string text="") { Snapshot=new(text); _saved=Snapshot; }
    public TextSnapshot Snapshot { get; private set; }
    public long Revision { get; private set; }
    public int Length=>Snapshot.Length;
    public int LineCount=>Snapshot.LineCount;
    public bool IsDirty=>!ReferenceEquals(_saved,Snapshot);
    public bool CanUndo=>!IsReadOnly&&_undo.Count>0;
    public bool CanRedo=>!IsReadOnly&&_redo.Count>0;
    public int HistoryLimit { get; set; }=500;
    public int MaximumLength { get; set; }=50_000_000;
    public bool IsReadOnly { get=>_readOnly; set { _readOnly=value; StateChanged?.Invoke(this,EventArgs.Empty); } }
    public event EventHandler<DocumentChange>? Changed;
    public event EventHandler? StateChanged;
    public TextAnchor CreateAnchor(int offset,Affinity affinity=Affinity.After)
    {
        Snapshot.Validate(offset,0); var anchor=new TextAnchor(offset,affinity); _anchors.Add(new(anchor)); return anchor;
    }
    public bool MarkSaved(long? expectedRevision=null)
    {
        if(expectedRevision is not null&&Revision!=expectedRevision) return false;
        _saved=Snapshot; StateChanged?.Invoke(this,EventArgs.Empty); return true;
    }
    public void Reset(string text)
    {
        if(_notifying) throw new InvalidOperationException("Reentrant document mutation is not allowed.");
        ArgumentNullException.ThrowIfNull(text); if(text.Length>MaximumLength) throw new ArgumentException("Document limit exceeded.");
        var before=Snapshot; Snapshot=new(text); _saved=Snapshot; _undo.Clear(); _redo.Clear(); Revision++;
        var edits=new[] { new TextEdit(0,before.Length,text) }; RemapAnchors(edits);
        Notify(new(before,Snapshot,edits,Guid.Empty));
    }
    public void Apply(IEnumerable<TextEdit> edits,Guid owner=default,IReadOnlyList<Selection>? beforeSelection=null,IReadOnlyList<Selection>? afterSelection=null,string? mergeKey=null,long? expectedRevision=null)
    {
        Guard(); if(expectedRevision is not null&&Revision!=expectedRevision) throw new InvalidOperationException("Stale document revision.");
        ArgumentNullException.ThrowIfNull(edits);
        var ordered=edits.OrderBy(e=>e.Start).ToArray(); long resulting=Length;
        for(int i=0;i<ordered.Length;i++)
        {
            var e=ordered[i]; ArgumentNullException.ThrowIfNull(e.Text); Snapshot.Validate(e.Start,e.Length);
            if(i>0&&(ordered[i-1].End>e.Start||ordered[i-1].Start==e.Start)) throw new ArgumentException("Overlapping edit ranges or duplicate insertion boundaries.");
            resulting+=(long)e.Text.Length-e.Length;
        }
        if(resulting>MaximumLength) throw new ArgumentException("Document limit exceeded.");
        ordered=ordered.Where(e=>e.Length!=0||e.Text.Length!=0).ToArray(); if(ordered.Length==0) return;
        var before=Snapshot; var inverse=new TextEdit[ordered.Length]; int delta=0;
        for(int i=0;i<ordered.Length;i++) { var e=ordered[i]; inverse[i]=new(e.Start+delta,e.Text.Length,before.GetText(e.Start,e.Length)); delta+=e.Text.Length-e.Length; }
        var after=before;
        for(int i=ordered.Length-1;i>=0;i--) { var e=ordered[i]; after=after.Replace(e.Start,e.Length,e.Text); }
        var step=new Step(before,after,ordered,inverse,owner,beforeSelection?.ToArray()??[],afterSelection?.ToArray()??[]);
        var last=_undo.LastOrDefault(); long now=Environment.TickCount64;
        bool merge=mergeKey is not null&&last is not null&&last.Key==mergeKey&&last.Owner==owner&&now-last.Timestamp<1200&&last.Steps.Count<1024&&ReferenceEquals(last.Steps[^1].After,before)&&!ReferenceEquals(before,_saved);
        if(!merge) { last=new(mergeKey,owner); _undo.Add(last); }
        last!.Steps.Add(step); last.Timestamp=now; _redo.Clear();
        while(_undo.Count>Math.Max(1,HistoryLimit)) _undo.RemoveAt(0);
        Snapshot=after; Revision++; RemapAnchors(ordered);
        Notify(new(before,after,ordered,owner,afterSelection));
    }
    public bool Undo()
    {
        Guard(); if(_undo.Count==0) return false;
        var entry=_undo[^1]; _undo.RemoveAt(_undo.Count-1); _redo.Add(entry);
        foreach(var step in entry.Steps.AsEnumerable().Reverse())
        {
            Snapshot=step.Before; Revision++; RemapAnchors(step.Inverse);
            Notify(new(step.After,step.Before,step.Inverse,step.Owner,step.BeforeSelection));
        }
        return true;
    }
    public bool Redo()
    {
        Guard(); if(_redo.Count==0) return false;
        var entry=_redo[^1]; _redo.RemoveAt(_redo.Count-1); _undo.Add(entry);
        foreach(var step in entry.Steps)
        {
            Snapshot=step.After; Revision++; RemapAnchors(step.Forward);
            Notify(new(step.Before,step.After,step.Forward,step.Owner,step.AfterSelection));
        }
        entry.Key=null; return true;
    }
    public void BreakUndoGroup() { if(_undo.Count>0) _undo[^1].Key=null; }
    public static int MapOffset(int offset,IReadOnlyList<TextEdit> edits,Affinity affinity=Affinity.After)
    {
        int delta=0;
        foreach(var e in edits)
        {
            if(offset<e.Start) break;
            if(offset<=e.End) return e.Start+delta+(affinity==Affinity.After?e.Text.Length:0);
            delta+=e.Text.Length-e.Length;
        }
        return offset+delta;
    }
    private void RemapAnchors(IReadOnlyList<TextEdit> edits)
    {
        for(int i=_anchors.Count-1;i>=0;i--)
            if(_anchors[i].TryGetTarget(out var a)) a.Offset=MapOffset(a.Offset,edits,a.Affinity); else _anchors.RemoveAt(i);
    }
    private void Guard() { if(_readOnly) throw new InvalidOperationException("Document is read-only."); if(_notifying) throw new InvalidOperationException("Reentrant document mutation is not allowed."); }
    private void Notify(DocumentChange change)
    {
        _notifying=true;
        try { Changed?.Invoke(this,change); StateChanged?.Invoke(this,EventArgs.Empty); }
        finally { _notifying=false; }
    }
}
