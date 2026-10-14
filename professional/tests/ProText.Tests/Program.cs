using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using ProText.Avalonia;
using ProText.Avalonia.Controls;
using ProText.Core;
namespace ProText.Tests;
internal static class Program
{
    private static readonly List<object> Results=[];
    private static int _failures,_assertions;
    [STAThread]
    public static int Main()
    {
        AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing=true }).SetupWithoutStarting();
        Test("range validation",()=>{ Throws<ArgumentOutOfRangeException>(()=>new TextRange(-1,0)); Throws<ArgumentOutOfRangeException>(()=>new TextRange(0,-1)); Throws<OverflowException>(()=>new TextRange(int.MaxValue,1)); Equal(4,new TextRange(1,3).End); Check(!new TextRange(1,3).Contains(4)); });
        Test("atomic sorted edits",()=>Equal("aXYdeZ",EditBatch.Apply("abcdef",[new(new(5,1),"Z"),new(new(1,2),"XY")])));
        Test("atomic overlap rejection",()=>Throws<ArgumentException>(()=>EditBatch.Apply("abcd",[new(new(1,2),"x"),new(new(2,1),"y")])));
        Test("ambiguous insertion rejection",()=>Throws<ArgumentException>(()=>EditBatch.Apply("abcd",[new(new(1,0),"x"),new(new(1,0),"y")])));
        Test("range bounds rejection",()=>Throws<ArgumentOutOfRangeException>(()=>EditBatch.Apply("abc",[new(new(3,1),"")])));
        Test("empty edits preserve text",()=>Equal("🌍\r\nx",EditBatch.Apply("🌍\r\nx",[])));
        Test("mixed line map",()=>{ var map=new LineMap("a\r\nb\nc\rd"); Equal(4,map.Count); Equal(2,map[1].DelimiterLength); Equal(3,map[2].Offset); Equal(1,map.AtOffset(2).Number); Equal(2,map.AtOffset(3).Number); Equal(7,map.GetOffset(4)); });
        Test("empty and trailing line",()=>{ Equal(1,new LineMap("").Count); Equal(3,new LineMap("\n\n").Count); Equal(2,new LineMap("x\r\n").Count); });
        Test("selection end excludes next line",()=>Equal(new TextRange(0,1),new LineMap("a\nb").ExpandToLines(new(0,2))));
        Test("line offsets clamp",()=>{ var map=new LineMap("ab\ncd"); Equal(2,map.GetOffset(1,200)); Equal(5,map.GetOffset(200,200)); Equal(1,map.AtOffset(-5).Number); Equal(2,map.AtOffset(500).Number); });
        Test("normalize and detect endings",()=>{ Equal("a\r\nb\r\nc\r\n",TextOperations.NormalizeLineEndings("a\nb\rc\r\n",LineEnding.CrLf)); Equal(LineEnding.Cr,TextOperations.DetectLineEnding("a\rb")); Equal(LineEnding.Lf,TextOperations.DetectLineEnding("")); });
        Test("trim preserves delimiters",()=>Equal("a\r\n b\n",EditBatch.Apply("a \t\r\n b \n",TextOperations.TrimTrailingWhitespace("a \t\r\n b \n"))));
        Test("indent selection preserves last line",()=>{ string text="a\nb"; Equal("    a\nb",EditBatch.Apply(text,TextOperations.Indent(text,new(0,2),4,false,false))); });
        Test("tab and partial unindent",()=>{ string text="\ta\n  b"; Equal("a\nb",EditBatch.Apply(text,TextOperations.Indent(text,new(0,text.Length),4,false,true))); });
        Test("comment roundtrip",()=>{ string text="  a\r\n\r\n b"; var commented=EditBatch.Apply(text,TextOperations.ToggleLineComment(text,new(0,text.Length),"//")); Equal("  // a\r\n\r\n // b",commented); Equal(text,EditBatch.Apply(commented,TextOperations.ToggleLineComment(commented,new(0,commented.Length),"//"))); });
        Test("empty comment prefix rejected",()=>Throws<ArgumentException>(()=>TextOperations.ToggleLineComment("abc",new(0,0),"")));
        Test("literal regex metacharacters",()=>{ var r=SearchEngine.Find("a.b aXb a.b",new("a.b")); Equal(2,r.Hits.Count); Equal(8,r.Hits[1].Range.Offset); });
        Test("case sensitive search",()=>{ Equal(3,SearchEngine.Find("Cat cat CAT",new("cat")).Hits.Count); Equal(1,SearchEngine.Find("Cat cat CAT",new("cat",MatchCase:true)).Hits.Count); });
        Test("Unicode whole words",()=>{ Equal(2,SearchEngine.Find("żółw żółwik żółw",new("żółw",WholeWord:true)).Hits.Count); Equal(1,SearchEngine.Find("a á",new("a",WholeWord:true)).Hits.Count); });
        Test("selection scoped search",()=>{ var r=SearchEngine.Find("cat cat cat",new("cat"),new(4,3)); Equal(1,r.Hits.Count); Equal(4,r.Hits[0].Range.Offset); });
        Test("selection respects document anchors",()=>Equal(0,SearchEngine.Find("abcd",new("^bc",UseRegex:true),new(1,2)).Hits.Count));
        Test("zero width matches terminate",()=>Equal(3,SearchEngine.Find("aaa",new("(?=a)",UseRegex:true)).Hits.Count));
        Test("regex replacement captures",()=>Equal("21 43",EditBatch.Apply("12 34",SearchEngine.CreateReplacementEdits("12 34",new("(\\d)(\\d)",UseRegex:true),"$2$1"))));
        Test("literal dollar replacement",()=>Equal("$1 $1",EditBatch.Apply("a a",SearchEngine.CreateReplacementEdits("a a",new("a"),"$1"))));
        Test("invalid regex diagnostic",()=>Check(!SearchEngine.Find("abc",new("[",UseRegex:true)).IsValid));
        Test("search cap explicit",()=>{ var r=SearchEngine.Find("aaaa",new("a",MaxMatches:2)); Equal(2,r.Hits.Count); Check(r.IsTruncated); Throws<InvalidOperationException>(()=>SearchEngine.CreateReplacementEdits("aaaa",new("a",MaxMatches:2),"b")); });
        Test("empty search is empty",()=>Equal(0,SearchEngine.Find("abc",new("")).Hits.Count));
        Test("search cancellation",()=>{ using var cts=new CancellationTokenSource(); cts.Cancel(); Throws<OperationCanceledException>(()=>SearchEngine.Find("aaaa",new("a"),cancellationToken:cts.Token)); });
        Test("fold nested braces",()=>{ const string text="{\n  [\n  ]\n}"; var f=new BraceFoldingProvider().GetFoldings(text); Equal(2,f.Count); Equal(0,f[0].StartOffset); Equal(text.Length,f[0].EndOffset); });
        Test("fold ignores strings and comments",()=>{ var f=new BraceFoldingProvider().GetFoldings("{\n \"}\"; // }\n /* } */\n}"); Equal(1,f.Count); });
        Test("fold multiline comments",()=>Equal(1,new BraceFoldingProvider().GetFoldings("/* hello\nworld */").Count));
        Test("fold ignores single-line braces",()=>Equal(0,new BraceFoldingProvider().GetFoldings("{ [ ] }").Count));
        Test("diff insertion and deletion",()=>{ var d=LineDiff.Compare("a\nb\nc","a\nx\nc"); Equal(1,d.Hunks.Count); Equal(1,d.Hunks[0].LeftCount); Equal(1,d.Hunks[0].RightCount); Check(!d.IsCoarse); });
        Test("diff equality",()=>Equal(0,LineDiff.Compare("a\nb","a\nb").Hunks.Count));
        Test("diff whitespace mode",()=>Equal(0,LineDiff.Compare(" a ","a",true).Hunks.Count));
        Test("diff bounded fallback",()=>{ var d=LineDiff.Compare("a\nb\nc","x\ny\nz",maxCells:1); Check(d.IsCoarse); Equal(1,d.Hunks.Count); });
        Test("diff reconstruction randomized",()=>
        {
            var random=new Random(422);
            for(int i=0;i<250;i++)
            {
                string a=string.Join('\n',Enumerable.Range(0,random.Next(1,12)).Select(_=>random.Next(4).ToString()));
                string b=string.Join('\n',Enumerable.Range(0,random.Next(1,12)).Select(_=>random.Next(4).ToString()));
                var diff=LineDiff.Compare(a,b); Equal(a,string.Join('\n',diff.Rows.Where(r=>r.Kind!=DiffKind.Insert).Select(r=>r.Text))); Equal(b,string.Join('\n',diff.Rows.Where(r=>r.Kind!=DiffKind.Delete).Select(r=>r.Text)));
            }
        });
        Test("edit batches randomized",()=>
        {
            var random=new Random(721); string text=new('a',200);
            for(int i=0;i<250;i++)
            {
                int a=random.Next(0,50),b=random.Next(100,150); var edits=new[] { new TextEdit(new(a,2),"XYZ"),new TextEdit(new(b,1),"q") };
                string expected=text.Remove(b,1).Insert(b,"q").Remove(a,2).Insert(a,"XYZ"); Equal(expected,EditBatch.Apply(text,edits.Reverse()));
            }
        });
        Test("large line map",()=>{ string text=string.Join('\n',Enumerable.Repeat("document",100000)); var map=new LineMap(text); Equal(100000,map.Count); Equal(text.Length,map[100000].End); });
        Test("completion prefix and ordering",()=>
        {
            var provider=new KeywordCompletionProvider([new("Console","Console",Priority:1),new("const","const",Priority:5),new("other","other")]);
            var items=provider.GetCompletionsAsync(new("Co",2,1),default).Result; Equal(2,items.Count); Equal("const",items[0].Label);
            Equal(new TextRange(4,2),new CompletionRequest("abc żó",6,1).WordRange);
        });
        Test("session revision and grouped undo",()=>
        {
            using var session=new EditorSession("abcd"); session.ApplyEdits([new(new(0,1),"A"),new(new(3,1),"D")]); Equal("AbcD",session.Text); Equal(1L,session.Revision); Check(session.IsDirty);
            session.Document.UndoStack.Undo(); Equal("abcd",session.Text); Check(!session.IsDirty); session.Document.UndoStack.Redo(); Equal("AbcD",session.Text);
        });
        Test("session preflight leaves no partial changes",()=>
        {
            using var session=new EditorSession("abc"); Throws<ArgumentOutOfRangeException>(()=>session.ApplyEdits([new(new(0,1),"x"),new(new(9,1),"y")])); Equal("abc",session.Text); Equal(0L,session.Revision);
        });
        Test("session read-only and stale edits",()=>
        {
            using var session=new EditorSession("abc"); session.IsReadOnly=true; Throws<InvalidOperationException>(()=>session.ReplaceAllText("x")); session.IsReadOnly=false;
            session.ReplaceAllText("def"); Throws<InvalidOperationException>(()=>session.ApplyEdits([new(new(0,1),"x")],0)); Equal("def",session.Text);
        });
        Test("save race remains dirty",()=>{ using var session=new EditorSession("a"); long revision=session.Revision; session.ReplaceAllText("b"); Check(!session.MarkSaved(revision)); Check(session.IsDirty); Check(session.MarkSaved()); Check(!session.IsDirty); });
        Test("diagnostics reject stale results",()=>
        {
            using var session=new EditorSession("abc"); Check(session.SetDiagnostics(0,[new("x",new(0,1),DiagnosticSeverity.Error,"message")])); Equal(1,session.Diagnostics.Count);
            session.ReplaceAllText("x"); Equal(0,session.Diagnostics.Count); Check(!session.SetDiagnostics(0,[new("x",new(0,1),DiagnosticSeverity.Error,"old")]));
        });
        Test("bookmark follows insertion",()=>{ using var session=new EditorSession("a\nb"); session.ToggleBookmark(2); session.ApplyEdits([new(new(0,0),"x\n")]); Equal(3,session.Bookmarks[0].Line); Equal(3,session.FindBookmark(1)); session.ToggleBookmark(3); Equal(0,session.Bookmarks.Count); });
        Test("line ending reflects undo",()=>{ using var session=new EditorSession("a\r\nb"); session.ConvertLineEndings(LineEnding.Lf); Equal(LineEnding.Lf,session.LineEnding); session.Document.UndoStack.Undo(); Equal(LineEnding.CrLf,session.LineEnding); });
        Test("shared native document",()=>
        {
            using var session=new EditorSession("abc"); using var left=new ProTextEditor { Session=session }; using var right=new ProTextEditor { Session=session };
            Check(ReferenceEquals(left.Editor.Document,right.Editor.Document)); left.Editor.Document.Insert(3,"d"); Equal("abcd",right.Text); right.Editor.Undo(); Equal("abc",left.Text);
        });
        Test("read-only wrapper commands",()=>
        {
            using var editor=new ProTextEditor { Text="a",IsReadOnly=true }; editor.DuplicateSelectionOrLine(); editor.ToggleComment(); editor.TrimTrailingWhitespace(); Equal("a",editor.Text); Check(editor.Editor.IsReadOnly);
        });
        Test("duplicate final line preserves caret bounds",()=>{ using var editor=new ProTextEditor { Text="abc" }; editor.Editor.CaretOffset=1; editor.DuplicateSelectionOrLine(); Equal("abc\nabc",editor.Text); Equal(5,editor.Editor.CaretOffset); });
        Test("delete final line handles delimiter",()=>{ using var editor=new ProTextEditor { Text="a\r\nb" }; editor.Editor.CaretOffset=4; editor.DeleteSelectedLines(); Equal("a",editor.Text); });
        Test("replace all is one undo step",()=>
        {
            using var editor=new ProTextEditor { Text="cat cat" }; editor.Editor.Document.UndoStack.ClearAll(); editor.Search.Pattern="cat"; editor.Search.Replacement="dog"; editor.Search.Refresh(); editor.Search.ReplaceAll(); Equal("dog dog",editor.Text); editor.Editor.Undo(); Equal("cat cat",editor.Text);
        });
        Test("zero-width search navigation",()=>
        {
            using var editor=new ProTextEditor { Text="aaa" };
            editor.Editor.Select(0,0); // Document insertion moves the caret; set the navigation starting point explicitly.
            editor.Search.Pattern="(?=a)"; editor.Search.UseRegex=true; editor.Search.Refresh();
            editor.Search.Navigate(); Equal(1,editor.Editor.CaretOffset); editor.Search.Navigate(); Equal(2,editor.Editor.CaretOffset); editor.Search.Navigate(); Equal(0,editor.Editor.CaretOffset);
        });
        Test("template replacement preserves editing surface",()=>
        {
            using var editor=new ProTextEditor { Text="abc" }; var window=new Window { Width=800,Height=500,Content=editor }; window.Show();
            editor.ApplyTemplate(); window.Measure(new Size(800,500)); window.Arrange(new Rect(0,0,800,500));
            var native=editor.Editor; var document=native.Document; native.Document.Insert(3,"d"); native.Select(1,2);
            editor.Theme=(ControlTheme)editor.FindResource("ProTextMinimalTheme")!; editor.ApplyTemplate();
            Check(ReferenceEquals(native,editor.Editor)); Check(ReferenceEquals(document,editor.Editor.Document)); Equal("abcd",editor.Text); Equal(1,editor.Editor.SelectionStart); Equal(2,editor.Editor.SelectionLength); Check(editor.Editor.CanUndo);
            editor.Theme=null; editor.ApplyTemplate(); Check(ReferenceEquals(native,editor.Editor)); window.Close();
        });
        Test("control options validation",()=>{ using var editor=new ProTextEditor(); Throws<ArgumentException>(()=>editor.TabSize=0); Throws<ArgumentException>(()=>editor.Zoom=double.NaN); editor.TabSize=8; editor.UseTabs=true; Equal(8,editor.Editor.Options.IndentationSize); Check(!editor.Editor.Options.ConvertTabsToSpaces); });
        Directory.CreateDirectory("artifacts/validation");
        File.WriteAllText("artifacts/validation/unit-results.json",JsonSerializer.Serialize(new { tests=Results.Count,assertions=_assertions,failures=_failures,results=Results },new JsonSerializerOptions { WriteIndented=true }));
        Console.WriteLine($"PROTEXT_TESTS: {Results.Count} tests, {_assertions} assertions, {_failures} failures");
        return _failures==0?0:1;
    }
    private static void Test(string name,Action action)
    {
        var watch=Stopwatch.StartNew(); try { action(); Results.Add(new { name,passed=true,milliseconds=watch.ElapsedMilliseconds }); Console.WriteLine("PASS "+name); }
        catch(Exception ex) { _failures++; Results.Add(new { name,passed=false,error=ex.ToString(),milliseconds=watch.ElapsedMilliseconds }); Console.WriteLine("FAIL "+name+": "+ex); }
    }
    private static void Check(bool value) { _assertions++; if(!value) throw new InvalidOperationException("Assertion failed."); }
    private static void Equal<T>(T expected,T actual) { _assertions++; if(!EqualityComparer<T>.Default.Equals(expected,actual)) throw new InvalidOperationException($"Expected <{expected}>; actual <{actual}>."); }
    private static void Throws<T>(Action action) where T:Exception { _assertions++; try { action(); } catch(T) { return; } throw new InvalidOperationException("Expected "+typeof(T).Name); }
}
public sealed class TestApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant=ThemeVariant.Dark; Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://ProText.Tests/")) { Source=new Uri("avares://ProText.Avalonia/Themes/Generic.axaml") });
    }
}
