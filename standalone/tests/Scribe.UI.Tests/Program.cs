using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Scribe.Avalonia;
using Scribe.Core;
int assertions=0,failures=0;var results=new List<object>();
AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=true}).SetupWithoutStarting();
void Check(bool value,string reason="Assertion failed"){assertions++;if(!value)throw new Exception(reason);}
void Eq<T>(T expected,T actual){assertions++;if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new Exception($"Expected {expected}, got {actual}");}
void Test(string name,Action test){try{test();results.Add(new{name,passed=true});Console.WriteLine("PASS "+name);}catch(Exception e){failures++;results.Add(new{name,passed=false,error=e.ToString()});Console.WriteLine("FAIL "+name+" "+e);}}
Window Mount(ScribeEditor editor){var window=new Window{Width=850,Height=600,Content=editor};window.Show();editor.ApplyTemplate();window.Measure(new Size(850,600));window.Arrange(new Rect(0,0,850,600));Dispatcher.UIThread.RunJobs();return window;}
Test("control directly uses framework primitives",()=>{Eq(typeof(Avalonia.Controls.Primitives.TemplatedControl),typeof(ScribeEditor).BaseType);Eq(typeof(Control),typeof(EditorViewport).BaseType);Check(typeof(Document).Assembly.GetReferencedAssemblies().All(a=>a.Name is not null&&!a.Name.Contains("Avalonia")));Check(typeof(ScribeEditor).Assembly.GetReferencedAssemblies().All(a=>a.Name is not null&&!a.Name.Contains("AvaloniaEdit")&&!a.Name.Contains("AvalonEdit")&&!a.Name.Contains("TextMate")));});
Test("default template hosts original viewport",()=>{using var editor=new ScribeEditor{Document=new("abc")};var window=Mount(editor);Check(editor.Viewport.Bounds.Width>200);Check(editor.Viewport.Bounds.Height>100);window.Close();});
Test("template replacement preserves original surface and selection",()=>{using var editor=new ScribeEditor{Document=new("base")};var window=Mount(editor);var viewport=editor.Viewport;editor.Session.GoTo(1,5);editor.Session.Insert(" edited");editor.Session.Select(new Selection(1,3));editor.Theme=(ControlTheme)editor.FindResource("ScribeMinimalTheme")!;editor.ApplyTemplate();Dispatcher.UIThread.RunJobs();Check(ReferenceEquals(viewport,editor.Viewport));Eq("base edited",editor.Document!.Snapshot.ToString());Eq(1,editor.Session.Primary.Start);Eq(2,editor.Session.Primary.Length);editor.UndoCommand.Execute(null);Eq("base",editor.Document.Snapshot.ToString());editor.Theme=null;editor.ApplyTemplate();window.Close();});
Test("view read-only does not freeze another view",()=>{var document=new Document("abc");using var a=new ScribeEditor{Document=document,IsReadOnly=true};using var b=new ScribeEditor{Document=document};a.Session.Insert("X");Eq("abc",document.Snapshot.ToString());b.Session.Insert("Y");Eq("Yabc",document.Snapshot.ToString());document.IsReadOnly=true;b.Session.Insert("Z");Eq("Yabc",document.Snapshot.ToString());});
Test("headless routed text input and undo",()=>{using var editor=new ScribeEditor{Document=new(),AutoPairs=false};var window=Mount(editor);editor.FocusEditor();editor.Viewport.RaiseEvent(new TextInputEventArgs{RoutedEvent=InputElement.TextInputEvent,Text="hello"});Eq("hello",editor.Document!.Snapshot.ToString());editor.UndoCommand.Execute(null);Eq("",editor.Document.Snapshot.ToString());window.Close();});
Test("headless pair and closing skip",()=>{using var editor=new ScribeEditor{Document=new()};var window=Mount(editor);editor.Viewport.RaiseEvent(new TextInputEventArgs{RoutedEvent=InputElement.TextInputEvent,Text="("});Eq("()",editor.Document!.Snapshot.ToString());Eq(1,editor.Session.Primary.Caret);editor.Viewport.RaiseEvent(new TextInputEventArgs{RoutedEvent=InputElement.TextInputEvent,Text=")"});Eq("()",editor.Document.Snapshot.ToString());Eq(2,editor.Session.Primary.Caret);window.Close();});
Test("headless multiple selection input",()=>{using var editor=new ScribeEditor{Document=new("cat dog cat"),AutoPairs=false};var window=Mount(editor);editor.Session.Select(new(0,3),new(8,11));editor.Viewport.RaiseEvent(new TextInputEventArgs{RoutedEvent=InputElement.TextInputEvent,Text="tiger"});Eq("tiger dog tiger",editor.Document!.Snapshot.ToString());Eq(2,editor.Session.Selections.Count);window.Close();});
Test("diagnostics reject stale results",()=>{using var editor=new ScribeEditor{Document=new("abc")};Check(editor.SetDiagnostics(0,[new(0,1,"problem")]));editor.Session.Insert("x");Check(!editor.SetDiagnostics(0,[new(0,1,"stale")]));Eq(0,editor.Diagnostics.Count);});
Test("typed search controls and atomic replacement",()=>{using var editor=new ScribeEditor{Document=new("cat cat")};var window=Mount(editor);editor.Search.Pattern="cat";editor.Search.Replacement="dog";editor.Search.Refresh();Eq(2,editor.Search.Results.Matches.Count);editor.Search.Replace(true);Eq("dog dog",editor.Document!.Snapshot.ToString());editor.UndoCommand.Execute(null);Eq("cat cat",editor.Document.Snapshot.ToString());window.Close();});
Test("shaped hit testing follows tab expansion",()=>{using var editor=new ScribeEditor{Document=new("a\tbcd")};var window=Mount(editor);var a=editor.Viewport.GetCaretRectangle(2);int offset=editor.Viewport.HitTestOffset(new Point(a.X+.01,a.Y+a.Height*.5));Eq(2,offset);window.Close();});
Test("wrapping adds physical rows",()=>{using var editor=new ScribeEditor{Document=new(new string('x',1000)),WordWrap=true};var window=Mount(editor);editor.Viewport.GetCaretRectangle(1000);Check(editor.Viewport.VisualRowCount>1);window.Close();});
Test("linked snippets use original multi-selection",()=>{using var editor=new ScribeEditor{Document=new(),AutoPairs=false};var window=Mount(editor);editor.InsertSnippet("${1:name} = $1; ${2:value}$0");Eq(2,editor.Session.Selections.Count);editor.Session.Insert("total");Eq("total = total; value",editor.Document!.Snapshot.ToString());editor.Snippet!.Next();Eq("value",editor.Session.SelectedText());window.Close();});
Test("disposed control detaches document observers",()=>{var document=new Document("abc");var editor=new ScribeEditor{Document=document};editor.Dispose();document.Apply([new(0,1,"A")]);Eq("Abc",document.Snapshot.ToString());});
Directory.CreateDirectory("artifacts/validation");File.WriteAllText("artifacts/validation/ui.json",JsonSerializer.Serialize(new{tests=results.Count,assertions,failures,results},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"SCRIBE_UI: {results.Count} suites, {assertions} assertions, {failures} failures");return failures==0?0:1;
public sealed class TestApp:Application
{
    public override void Initialize(){RequestedThemeVariant=ThemeVariant.Dark;Styles.Add(new FluentTheme());Styles.Add(new StyleInclude(new Uri("avares://Scribe.UI.Tests/")){Source=new Uri("avares://Scribe.Avalonia/Themes/Generic.axaml")});}
}
