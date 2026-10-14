using System.Text.Json;
using Scribe.Core;
int assertions=0,failures=0; var results=new List<object>();
void Eq<T>(T expected,T actual) { assertions++; if(!EqualityComparer<T>.Default.Equals(expected,actual)) throw new Exception($"Expected {expected}, got {actual}"); }
void Check(bool value) { assertions++; if(!value) throw new Exception("Assertion failed"); }
void Throws<T>(Action action) where T:Exception { assertions++; try { action(); } catch(T) { return; } throw new Exception("Expected "+typeof(T).Name); }
void Test(string name,Action test) { try { test(); results.Add(new {name,passed=true}); Console.WriteLine("PASS "+name); } catch(Exception e) { failures++; results.Add(new {name,passed=false,error=e.ToString()}); Console.WriteLine("FAIL "+name+" "+e); } }
List<int> Starts(string text) { var starts=new List<int>{0}; for(int i=0;i<text.Length;i++) if(text[i] is '\r' or '\n') { if(text[i]=='\r'&&i+1<text.Length&&text[i+1]=='\n') i++; starts.Add(i+1); } return starts; }
Test("empty snapshot",()=>{ var b=TextSnapshot.Empty; Eq(0,b.Length); Eq(1,b.LineCount); Eq(0,b.GetLine(0).Length); Eq("",b.ToString()); });
Test("mixed newline indexing",()=>{ string text="a\r\nb\rc\nd\r\n"; var b=new TextSnapshot(text); var starts=Starts(text); Eq(starts.Count,b.LineCount); for(int i=0;i<starts.Count;i++) Eq(starts[i],b.GetLineStart(i)); Eq(0,b.LineAt(2)); Eq(1,b.LineAt(3)); });
Test("CRLF crossing rope boundary",()=>{ for(int p=1000;p<1050;p++) { string text=new string('a',p)+"\r\n"+new string('b',2050-p); var b=new TextSnapshot(text); Eq(2,b.LineCount); Eq(p+2,b.GetLineStart(1)); b.ValidateTree(); } });
Test("persistent snapshots",()=>{ var old=new TextSnapshot("hello\r\nworld"); var next=old.Replace(0,5,"goodbye"); Eq("hello\r\nworld",old.ToString()); Eq("goodbye\r\nworld",next.ToString()); Eq(9,next.GetLineStart(1)); });
Test("rope random edits and line index",()=>
{
    var random=new Random(88771); string text=new('x',5000); var b=new TextSnapshot(text); var history=new List<(TextSnapshot,string)>();
    for(int n=0;n<1500;n++)
    {
        int p=random.Next(text.Length+1),length=random.Next(Math.Min(90,text.Length-p)+1);
        string insert=string.Concat(Enumerable.Range(0,random.Next(60)).Select(_=>"ab\r\nc"[random.Next(5)]));
        if(n%100==0) history.Add((b,text)); text=text.Remove(p,length).Insert(p,insert); b=b.Replace(p,length,insert);
        Eq(text,b.ToString()); b.ValidateTree(); var starts=Starts(text); Eq(starts.Count,b.LineCount);
        for(int k=0;k<8;k++) { int line=random.Next(starts.Count); Eq(starts[line],b.GetLineStart(line)); int offset=random.Next(text.Length+1); Eq(starts.FindLastIndex(s=>s<=offset),b.LineAt(offset)); }
    }
    foreach(var (snapshot,original) in history) Eq(original,snapshot.ToString());
});
Test("random snapshot slices",()=>{ var b=new TextSnapshot(string.Concat(Enumerable.Range(0,20000).Select(i=>(char)('a'+i%26)))); var text=b.ToString(); for(int i=0;i<1000;i++) { int p=i*13%19000; Eq(text.Substring(p,777),b.GetText(p,777)); } });
Test("rope boundaries rejected",()=>{ var b=new TextSnapshot("abc"); Throws<ArgumentOutOfRangeException>(()=>b.Replace(-1,0,"")); Throws<ArgumentOutOfRangeException>(()=>b.GetText(2,2)); Throws<ArgumentOutOfRangeException>(()=>b.GetLine(1)); });
Test("atomic edit preflight",()=>{ var d=new Document("abc"); Throws<ArgumentOutOfRangeException>(()=>d.Apply([new(0,1,"A"),new(5,1,"B")])); Eq("abc",d.Snapshot.ToString()); Eq(0L,d.Revision); });
Test("overlap rejection",()=>{ var d=new Document("abcd"); Throws<ArgumentException>(()=>d.Apply([new(0,2,"x"),new(1,2,"y")])); Throws<ArgumentException>(()=>d.Apply([new(0,0,"x"),new(0,0,"y")])); Eq("abcd",d.Snapshot.ToString()); });
Test("multi-edit undo",()=>{ var d=new Document("abcd"); d.Apply([new(3,1,"D"),new(0,1,"A")]); Eq("AbcD",d.Snapshot.ToString()); Check(d.IsDirty); d.Undo(); Eq("abcd",d.Snapshot.ToString()); Check(!d.IsDirty); d.Redo(); Eq("AbcD",d.Snapshot.ToString()); });
Test("read-only edit guard",()=>{ var d=new Document("a"){IsReadOnly=true}; Throws<InvalidOperationException>(()=>d.Apply([new(0,1,"b")])); Eq("a",d.Snapshot.ToString()); });
Test("stale edit and save guard",()=>{ var d=new Document("a"); d.Apply([new(0,1,"b")]); Throws<InvalidOperationException>(()=>d.Apply([new(0,1,"c")],expectedRevision:0)); Check(!d.MarkSaved(0)); Check(d.IsDirty); Check(d.MarkSaved(d.Revision)); Check(!d.IsDirty); });
Test("anchors track replace and undo",()=>{ var d=new Document("abcd"); var a=d.CreateAnchor(2),z=d.CreateAnchor(4); d.Apply([new(0,1,"XYZ")]); Eq(4,a.Offset); Eq(6,z.Offset); d.Undo(); Eq(2,a.Offset); Eq(4,z.Offset); });
Test("typing grouped undo",()=>{ using var v=new EditSession(); v.Insert("a",true); v.Insert("b",true); v.Insert("c",true); Eq("abc",v.Document.Snapshot.ToString()); v.Document.Undo(); Eq("",v.Document.Snapshot.ToString()); Eq(0,v.Primary.Caret); v.Document.Redo(); Eq("abc",v.Document.Snapshot.ToString()); Eq(3,v.Primary.Caret); });
Test("save divides typing groups",()=>{ using var v=new EditSession(); v.Insert("a",true); v.Document.MarkSaved(); v.Insert("b",true); v.Document.Undo(); Eq("a",v.Document.Snapshot.ToString()); Check(!v.Document.IsDirty); });
Test("navigation divides typing groups",()=>{ using var v=new EditSession(); v.Insert("abc",true); v.MoveHorizontal(-1); v.MoveHorizontal(1); v.Insert("x",true); v.Document.Undo(); Eq("abc",v.Document.Snapshot.ToString()); });
Test("shared independent views",()=>{ var d=new Document("abc"); using var a=new EditSession(d); using var b=new EditSession(d); a.GoTo(1,4); b.GoTo(1,2); a.Insert("X"); Eq(1,b.Primary.Caret); b.Insert("Y"); Eq("aYbcX",d.Snapshot.ToString()); d.Undo(); Eq("abcX",d.Snapshot.ToString()); Eq(1,b.Primary.Caret); });
Test("multiple carets replace atomically",()=>{ using var v=new EditSession(new Document("cat dog cat")); v.Select(new(0,3),new(8,11)); v.Insert("tiger"); Eq("tiger dog tiger",v.Document.Snapshot.ToString()); Eq(2,v.Selections.Count); Eq(5,v.Selections[0].Caret); Eq(15,v.Selections[1].Caret); v.Document.Undo(); Eq("cat dog cat",v.Document.Snapshot.ToString()); Eq(3,v.Selections[0].Length); });
Test("selection normalization",()=>{ using var v=new EditSession(new Document("abcdef")); v.Select(new(1,4),new(2,5),Selection.At(3)); Eq(1,v.Selections.Count); Eq(4,v.Primary.Length); });
Test("grapheme and emoji navigation",()=>{ using var v=new EditSession(new Document("a🌍e\u0301👩‍👩‍👧‍👦z")); v.GoTo(1,2); v.MoveHorizontal(1); Eq(3,v.Primary.Caret); v.MoveHorizontal(1); Eq(5,v.Primary.Caret); v.MoveHorizontal(1); Eq(16,v.Primary.Caret); v.Delete(true); Eq("a🌍e\u0301z",v.Document.Snapshot.ToString()); });
Test("CRLF deletion indivisible",()=>{ using var v=new EditSession(new Document("a\r\nb")); v.GoTo(2); v.Delete(true); Eq("ab",v.Document.Snapshot.ToString()); });
Test("multi-line indentation",()=>{ using var v=new EditSession(new Document("a\nb\nc")); v.Select(new(0,4)); v.Indent(); Eq("    a\n    b\nc",v.Document.Snapshot.ToString()); v.Document.Undo(); Eq("a\nb\nc",v.Document.Snapshot.ToString()); });
Test("comment roundtrip",()=>{ using var v=new EditSession(new Document("  a\n b")); v.SelectAll(); v.ToggleComment(); Eq("  // a\n // b",v.Document.Snapshot.ToString()); v.SelectAll(); v.ToggleComment(); Eq("  a\n b",v.Document.Snapshot.ToString()); });
Test("new line auto indentation",()=>{ using var v=new EditSession(new Document("    if (x) {")); v.MoveHomeEnd(true,true); v.Enter(); Eq("    if (x) {\n        ",v.Document.Snapshot.ToString()); });
Test("duplicate final line",()=>{ using var v=new EditSession(new Document("abc")); v.Duplicate(); Eq("abc\nabc",v.Document.Snapshot.ToString()); });
Test("trailing whitespace keeps EOL",()=>{ using var v=new EditSession(new Document("a \t\r\n b \n")); v.TrimWhitespace(); Eq("a\r\n b\n",v.Document.Snapshot.ToString()); });
Test("next occurrence multi-selection",()=>{ using var v=new EditSession(new Document("cat dog cat")); v.Select(new(0,3)); Check(v.SelectNextOccurrence()); v.Insert("x"); Eq("x dog x",v.Document.Snapshot.ToString()); });
Test("literal search",()=>Eq(2,Search.Find("a.b aXb a.b",new("a.b")).Matches.Count));
Test("Unicode whole word search",()=>Eq(2,Search.Find("żółw żółwik żółw",new("żółw",WholeWord:true)).Matches.Count));
Test("invalid regex returns error",()=>Check(Search.Find("abc",new("[",Regex:true)).Error is not null));
Test("search bounded and replace refuses partial",()=>{ Check(Search.Find("aaaa",new("a",Limit:2)).Truncated); Throws<InvalidOperationException>(()=>Search.Replace("aaaa",new("a",Limit:2),"b")); });
Test("regex capture replacement",()=>{ var d=new Document("x12 x34"); d.Apply(Search.Replace(d.Snapshot.ToString(),new("x(\\d+)",Regex:true),"$1!")); Eq("12! 34!",d.Snapshot.ToString()); });
Test("zero width search",()=>Eq(3,Search.Find("aaa",new("(?=a)",Regex:true)).Matches.Count));
Test("selection scoped replacement",()=>{ var d=new Document("cat cat cat"); d.Apply(Search.Replace(d.Snapshot.ToString(),new("cat"),"dog",new(4,7))); Eq("cat dog cat",d.Snapshot.ToString()); });
Test("snippet expansion mirrors",()=>{ var e=Snippets.Expand("${1:name} = $1; $0"); Eq("name = name; ",e.Text); Eq(3,e.Stops.Count); });
Test("linked snippet editing and tab stops",()=>{ using var v=new EditSession(); var snippet=new SnippetSession(v,"${1:name} = $1; ${2:value}$0"); Eq(2,v.Selections.Count); v.Insert("counter"); Eq("counter = counter; value",v.Document.Snapshot.ToString()); snippet.Next(); Eq("value",v.SelectedText()); v.Insert("42"); snippet.Next(); Check(!snippet.IsActive); Eq("counter = counter; 42",v.Document.Snapshot.ToString()); });
Test("folding ignores ordinary strings comments",()=>{ var b=new TextSnapshot("class A\n{\n // }\n string s = \"}\";\n void M()\n {\n }\n}"); Eq(2,LexicalLanguage.Folds(b).Count); });
Test("large document original buffer",()=>{ string text=string.Join('\n',Enumerable.Range(0,100000).Select(i=>$"row {i}")); var b=new TextSnapshot(text); Eq(100000,b.LineCount); b.ValidateTree(); Check(b.TreeHeight<30); var last=b.GetLine(99999); Eq("row 99999",b.GetText(last.Start,last.Length)); });
Directory.CreateDirectory("artifacts/validation"); File.WriteAllText("artifacts/validation/core.json",JsonSerializer.Serialize(new{tests=results.Count,assertions,failures,results},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"SCRIBE_CORE: {results.Count} suites, {assertions} assertions, {failures} failures"); return failures==0?0:1;
