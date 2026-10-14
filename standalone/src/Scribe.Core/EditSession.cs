using System.Globalization;
namespace Scribe.Core;

/// <summary>Independent view state and command model. Multiple sessions can share one Document.</summary>
public sealed class EditSession : IDisposable
{
    private Selection[] _selections=[Selection.At(0)];
    private bool _disposed;
    public Guid Id { get; }=Guid.NewGuid();
    public Document Document { get; }
    public IReadOnlyList<Selection> Selections=>Array.AsReadOnly(_selections);
    public Selection Primary=>_selections[^1];
    public int TabSize { get; set; }=4;
    public bool UseTabs { get; set; }
    public bool IsReadOnly { get; set; }
    public bool CanEdit=>!IsReadOnly&&!Document.IsReadOnly&&!_disposed;
    public string NewLine { get; set; }="\n";
    public List<TextAnchor> Bookmarks { get; }=[];
    public event EventHandler? Changed;
    public EditSession(Document? document=null)
    {
        Document=document??new(); Document.Changed+=DocumentChanged; Document.StateChanged+=StateChanged;
        if(Document.Snapshot.LineCount>1) { var line=Document.Snapshot.GetLine(0); NewLine=Document.Snapshot.GetText(line.End,line.DelimiterLength); }
    }
    public void Select(params Selection[] selections)
    {
        ObjectDisposedException.ThrowIf(_disposed,this); Document.BreakUndoGroup(); SetSelections(selections); Changed?.Invoke(this,EventArgs.Empty);
    }
    private void SetSelections(IEnumerable<Selection> selections)
    {
        var normalized=new List<Selection>(); int length=Document.Length;
        foreach(var s in selections.Select(s=>new Selection(Math.Clamp(s.Anchor,0,length),Math.Clamp(s.Caret,0,length))).OrderBy(s=>s.Start).ThenBy(s=>s.End))
        {
            if(normalized.Count>0&&s.Start<=normalized[^1].End)
            {
                var previous=normalized[^1];
                if(s.Start==previous.End&&s.Length>0&&previous.Length>0) normalized.Add(s);
                else normalized[^1]=new(previous.Start,Math.Max(previous.End,s.End));
            }
            else normalized.Add(s);
        }
        if(normalized.Count>4096) throw new ArgumentException("Maximum 4096 selections.");
        _selections=normalized.Count==0?[Selection.At(0)]:normalized.ToArray();
    }
    public void SelectAll()=>Select(new(0,Document.Length));
    public string SelectedText(string separator="\n")=>string.Join(separator,_selections.Select(s=>Document.Snapshot.GetText(s.Start,s.Length)));
    public void Insert(string text,bool typing=false)
    {
        if(!CanEdit) return; ArgumentNullException.ThrowIfNull(text);
        ReplaceSelections(_selections.Select(_=>text).ToArray(),typing&&_selections.All(s=>s.Length==0)&&!text.Contains('\n')&&!text.Contains('\r')?"typing":null);
    }
    public void InsertPerSelection(IReadOnlyList<string> texts)
    {
        if(!CanEdit) return; if(texts.Count!=_selections.Length) throw new ArgumentException("One replacement is required per selection.");
        ReplaceSelections(texts,null);
    }
    private void ReplaceSelections(IReadOnlyList<string> texts,string? key)
    {
        var edits=new List<TextEdit>(); var after=new List<Selection>(); int delta=0;
        for(int i=0;i<_selections.Length;i++) { var s=_selections[i]; string text=texts[i]; edits.Add(new(s.Start,s.Length,text)); after.Add(Selection.At(s.Start+delta+text.Length)); delta+=text.Length-s.Length; }
        Document.Apply(edits,Id,_selections,after,key);
    }
    public void Enter()
    {
        if(!CanEdit) return;
        var buffer=Document.Snapshot; var texts=_selections.Select(s=>
        {
            var line=buffer.GetLine(buffer.LineAt(s.Start)); string prefix=buffer.GetText(line.Start,Math.Min(line.Length,s.Start-line.Start));
            string indent=new(prefix.TakeWhile(c=>c is ' ' or '\t').ToArray());
            if(prefix.TrimEnd().EndsWith('{')) indent+=UseTabs?"\t":new string(' ',Math.Clamp(TabSize,1,16));
            return NewLine+indent;
        }).ToArray();
        ReplaceSelections(texts,null);
    }
    public void Delete(bool backwards=false,bool word=false)
    {
        if(!CanEdit) return; var snapshot=Document.Snapshot; var before=_selections;
        var ranges=_selections.Select(s=>s.Length>0?s:backwards?new Selection(word?WordBoundary(snapshot,s.Caret,-1):GraphemeBoundary(snapshot,s.Caret,-1),s.Caret):new Selection(s.Caret,word?WordBoundary(snapshot,s.Caret,1):GraphemeBoundary(snapshot,s.Caret,1))).ToArray();
        SetSelections(ranges);
        var selected=_selections; int delta=0; var edits=new List<TextEdit>(); var after=new List<Selection>();
        foreach(var s in selected) { edits.Add(new(s.Start,s.Length,"")); after.Add(Selection.At(s.Start+delta)); delta-=s.Length; }
        _selections=before; Document.Apply(edits,Id,before,after,backwards?"backspace":"delete");
    }
    public void MoveHorizontal(int direction,bool extend=false,bool word=false)
    {
        var snapshot=Document.Snapshot;
        Select(_selections.Select(s=>
        {
            int target=!extend&&s.Length>0?(direction<0?s.Start:s.End):word?WordBoundary(snapshot,s.Caret,direction):GraphemeBoundary(snapshot,s.Caret,direction);
            return new Selection(extend?s.Anchor:target,target);
        }).ToArray());
    }
    public void MoveVertical(int direction,bool extend=false)
    {
        var snapshot=Document.Snapshot;
        Select(_selections.Select(s=>
        {
            var current=snapshot.GetLine(snapshot.LineAt(s.Caret)); var next=snapshot.GetLine(Math.Clamp(current.Index+direction,0,snapshot.LineCount-1));
            int target=Snap(snapshot,next.Start+Math.Min(next.Length,s.Caret-current.Start));
            return new Selection(extend?s.Anchor:target,target);
        }).ToArray());
    }
    public void MoveHomeEnd(bool end,bool document=false,bool extend=false)
    {
        var snapshot=Document.Snapshot;
        Select(_selections.Select(s=>
        {
            var line=snapshot.GetLine(snapshot.LineAt(s.Caret)); int target=document?(end?snapshot.Length:0):(end?line.End:line.Start);
            return new Selection(extend?s.Anchor:target,target);
        }).ToArray());
    }
    public void GoTo(int line,int column=1)
    {
        var snapshot=Document.Snapshot; var target=snapshot.GetLine(Math.Clamp(line-1,0,snapshot.LineCount-1));
        Select(Selection.At(Snap(snapshot,target.Start+Math.Clamp(column-1,0,target.Length))));
    }
    public void SelectWord(int offset)
    {
        var b=Document.Snapshot; offset=Snap(b,Math.Clamp(offset,0,b.Length)); int a=offset,z=offset;
        while(a>0&&IsWord(b[a-1])) a=GraphemeBoundary(b,a,-1);
        while(z<b.Length&&IsWord(b[z])) z=GraphemeBoundary(b,z,1);
        if(a==z) z=GraphemeBoundary(b,z,1);
        Select(new(a,z));
    }
    public IReadOnlyList<int> SelectedLines()
    {
        var b=Document.Snapshot; var lines=new SortedSet<int>();
        foreach(var s in _selections) { int first=b.LineAt(s.Start),last=b.LineAt(s.Length>0?s.End-1:s.End); for(int i=first;i<=last;i++) lines.Add(i); }
        return lines.ToArray();
    }
    public void ApplyEdits(IEnumerable<TextEdit> edits)
    {
        if(!CanEdit) return; var ordered=edits.OrderBy(e=>e.Start).ToArray();
        var after=_selections.Select(s=>new Selection(Document.MapOffset(s.Anchor,ordered,s.Length>0?Affinity.Before:Affinity.After),Document.MapOffset(s.Caret,ordered))).ToArray();
        Document.Apply(ordered,Id,_selections,after);
    }
    public void Indent(bool unindent=false)
    {
        if(!CanEdit) return; var b=Document.Snapshot; int size=Math.Clamp(TabSize,1,16); var edits=new List<TextEdit>();
        foreach(int index in SelectedLines())
        {
            var line=b.GetLine(index);
            if(!unindent) edits.Add(new(line.Start,0,UseTabs?"\t":new string(' ',size)));
            else { int n=0; if(line.Length>0&&b[line.Start]=='\t') n=1; else while(n<Math.Min(size,line.Length)&&b[line.Start+n]==' ') n++; if(n>0) edits.Add(new(line.Start,n,"")); }
        }
        ApplyEdits(edits);
    }
    public void ToggleComment(string prefix="//")
    {
        if(string.IsNullOrWhiteSpace(prefix)||prefix.Contains('\n')||prefix.Contains('\r')) throw new ArgumentException("Invalid comment prefix.");
        var b=Document.Snapshot; var targets=SelectedLines().Select(i=>b.GetLine(i)).Where(l=>l.Length>0).Select(l=>
        { int p=l.Start; while(p<l.End&&b[p] is ' ' or '\t') p++; return (p,l.End); }).Where(p=>p.p<p.End).ToArray();
        bool remove=targets.Length>0&&targets.All(t=>t.End-t.p>=prefix.Length&&b.GetText(t.p,prefix.Length)==prefix);
        ApplyEdits(targets.Select(t=>{ int count=remove?prefix.Length:0; if(remove&&t.p+count<t.End&&b[t.p+count]==' ') count++; return new TextEdit(t.p,count,remove?"":prefix+" "); }));
    }
    public void TrimWhitespace()
    {
        var b=Document.Snapshot; var edits=new List<TextEdit>();
        for(int i=0;i<b.LineCount;i++) { var l=b.GetLine(i); int end=l.End; while(end>l.Start&&b[end-1] is ' ' or '\t') end--; if(end<l.End) edits.Add(new(end,l.End-end,"")); }
        ApplyEdits(edits);
    }
    public void Duplicate()
    {
        if(!CanEdit) return;
        if(_selections.Any(s=>s.Length>0)) { InsertPerSelection(_selections.Select(s=>{var text=Document.Snapshot.GetText(s.Start,s.Length); return text+text;}).ToArray()); return; }
        var b=Document.Snapshot; var edits=SelectedLines().Select(i=>
        {
            var l=b.GetLine(i); string text=b.GetText(l.Start,l.TotalLength);
            return l.DelimiterLength>0?new TextEdit(l.Start,0,text):new TextEdit(l.End,0,NewLine+text);
        });
        ApplyEdits(edits);
    }
    public void ChangeCase(bool upper)
    {
        InsertPerSelection(_selections.Select(s=>{ string text=Document.Snapshot.GetText(s.Start,s.Length); return upper?text.ToUpperInvariant():text.ToLowerInvariant(); }).ToArray());
    }
    public bool SelectNextOccurrence()
    {
        var primary=Primary; if(primary.Length==0) { SelectWord(primary.Caret); return Primary.Length>0; }
        string text=Document.Snapshot.ToString(),needle=text.Substring(primary.Start,primary.Length);
        int offset=text.IndexOf(needle,primary.End,StringComparison.Ordinal);
        if(offset<0) offset=text.IndexOf(needle,StringComparison.Ordinal);
        if(offset<0||_selections.Any(s=>s.Start==offset)) return false;
        Select(_selections.Append(new Selection(offset,offset+needle.Length)).ToArray()); return true;
    }
    public void ToggleBookmark()
    {
        int line=Document.Snapshot.LineAt(Primary.Caret); var existing=Bookmarks.FirstOrDefault(b=>Document.Snapshot.LineAt(b.Offset)==line);
        if(existing is not null) Bookmarks.Remove(existing); else Bookmarks.Add(Document.CreateAnchor(Document.Snapshot.GetLineStart(line)));
        Changed?.Invoke(this,EventArgs.Empty);
    }
    public void NextBookmark(bool backwards=false)
    {
        var offsets=Bookmarks.Select(b=>b.Offset).Distinct().Order().ToArray(); if(offsets.Length==0) return;
        int offset=backwards?offsets.LastOrDefault(p=>p<Primary.Caret,offsets[^1]):offsets.FirstOrDefault(p=>p>Primary.Caret,offsets[0]); Select(Selection.At(offset));
    }
    private void DocumentChanged(object? sender,DocumentChange e)
    {
        if(e.Origin==Id&&e.RestoreSelection is {Count:>0}) SetSelections(e.RestoreSelection);
        else SetSelections(_selections.Select(s=>new Selection(Document.MapOffset(s.Anchor,e.Edits),Document.MapOffset(s.Caret,e.Edits))));
        Changed?.Invoke(this,EventArgs.Empty);
    }
    private void StateChanged(object? sender,EventArgs e)=>Changed?.Invoke(this,EventArgs.Empty);
    public static bool IsWord(char c)=>char.IsLetterOrDigit(c)||c=='_'||char.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;
    public static int WordBoundary(TextSnapshot b,int offset,int direction)
    {
        int p=offset;
        if(direction<0) { while(p>0&&char.IsWhiteSpace(b[p-1])) p=GraphemeBoundary(b,p,-1); while(p>0&&IsWord(b[p-1])) p=GraphemeBoundary(b,p,-1); }
        else { while(p<b.Length&&IsWord(b[p])) p=GraphemeBoundary(b,p,1); while(p<b.Length&&char.IsWhiteSpace(b[p])) p=GraphemeBoundary(b,p,1); }
        return p==offset?GraphemeBoundary(b,offset,direction):p;
    }
    public static int GraphemeBoundary(TextSnapshot b,int offset,int direction)
    {
        offset=Math.Clamp(offset,0,b.Length);
        if(direction<0&&offset==0||direction>0&&offset==b.Length) return offset;
        int index=b.LineAt(direction<0?offset-1:offset); var line=b.GetLine(index); string text=b.GetText(line.Start,line.TotalLength);
        int relative=offset-line.Start; var starts=StringInfo.ParseCombiningCharacters(text);
        if(direction<0) { for(int i=starts.Length-1;i>=0;i--) if(starts[i]<relative) return line.Start+starts[i]; return line.Start; }
        foreach(int p in starts) if(p>relative) return line.Start+p;
        return line.Start+line.TotalLength;
    }
    public static int Snap(TextSnapshot b,int offset)
    {
        if(offset<=0||offset>=b.Length) return Math.Clamp(offset,0,b.Length);
        int previous=GraphemeBoundary(b,offset,-1); int next=GraphemeBoundary(b,previous,1);
        return next==offset?offset:previous;
    }
    public void Dispose() { if(_disposed) return; _disposed=true; Document.Changed-=DocumentChanged; Document.StateChanged-=StateChanged; Changed=null; }
}
