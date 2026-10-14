using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Scribe.Avalonia;
using Scribe.Core;
namespace Scribe.Sample;
public partial class MainView : UserControl
{
    public ScribeEditor Editor {get;}
    public ScribeEditor SecondaryEditor {get;}
    public bool Split {get;private set;}
    public bool Minimal {get;private set;}
    public long LoadMilliseconds {get;private set;}
    public List<(string Name,Document Document,string Language)> Documents {get;}=[];
    private int _current;
    private readonly TextBlock _info,_title;
    public MainView()
    {
        AvaloniaXamlLoader.Load(this);Editor=this.FindControl<ScribeEditor>("Primary")!;SecondaryEditor=this.FindControl<ScribeEditor>("Secondary")!;
        _info=this.FindControl<TextBlock>("Information")!;_title=this.FindControl<TextBlock>("DocumentTitle")!;
        Documents.Add(("Welcome.cs",new(SampleContent.Code),"C#"));Documents.Add(("Unicode notes.txt",new(SampleContent.Notes),"Plain Text"));Documents.Add(("settings.json",new(SampleContent.Json),"JSON"));
        Editor.CompletionProvider=new SampleCompletion();Editor.DiagnosticProvider=new SampleDiagnostics();SecondaryEditor.CompletionProvider=Editor.CompletionProvider;SelectDocument(0);
        Wire("NewButton",()=>{Documents.Add(($"Untitled-{Documents.Count}.txt",new(),"Plain Text"));SelectDocument(Documents.Count-1);});
        this.FindControl<Button>("OpenButton")!.Click+=async(_,_)=>await OpenAsync();
        this.FindControl<Button>("SaveButton")!.Click+=async(_,_)=>await SaveAsync();
        Wire("UndoButton",()=>Editor.UndoCommand.Execute(null));Wire("RedoButton",()=>Editor.RedoCommand.Execute(null));Wire("FindButton",Editor.OpenSearch);
        Wire("SplitButton",ToggleSplit);Wire("ThemeButton",ToggleTheme);Wire("TemplateButton",ToggleTemplate);
        Wire("CodeButton",()=>SelectDocument(0));Wire("NotesButton",()=>SelectDocument(1));Wire("JsonButton",()=>SelectDocument(2));Wire("LargeButton",()=>LoadLarge());
        Wire("FoldButton",()=>Editor.FoldAll(Editor.CollapsedLines.Count==0));
        this.FindControl<Button>("CompletionButton")!.Click+=async(_,_)=>{Editor.FocusEditor();await Editor.ShowCompletionAsync();};
        Wire("SnippetButton",()=>Editor.InsertSnippet("for (var ${1:i} = 0; $1 < ${2:10}; $1++)\n{\n    $0\n}"));
        Wire("GoToButton",()=>{GoTo(this.FindControl<TextBox>("LineBox")!.Text??"1");});
        this.FindControl<TextBox>("LineBox")!.KeyDown+=(_,e)=>{if(e.Key==Avalonia.Input.Key.Enter)GoTo(this.FindControl<TextBox>("LineBox")!.Text??"1");};
        this.FindControl<CheckBox>("WrapCheck")!.IsCheckedChanged+=(_,_)=>Editor.WordWrap=this.FindControl<CheckBox>("WrapCheck")!.IsChecked==true;
        this.FindControl<CheckBox>("WhitespaceCheck")!.IsCheckedChanged+=(_,_)=>Editor.ShowWhitespace=this.FindControl<CheckBox>("WhitespaceCheck")!.IsChecked==true;
        this.FindControl<CheckBox>("ReadOnlyCheck")!.IsCheckedChanged+=(_,_)=>Editor.IsReadOnly=this.FindControl<CheckBox>("ReadOnlyCheck")!.IsChecked==true;
        SizeChanged+=(_,e)=>
        {
            bool wide=e.NewSize.Width>=900;this.FindControl<Border>("Sidebar")!.IsVisible=wide;this.FindControl<Grid>("Workspace")!.ColumnDefinitions[0].Width=new GridLength(wide?216:0);
            this.FindControl<Border>("IdentityBadge")!.IsVisible=e.NewSize.Width>=650;
        };
        this.FindControl<TextBlock>("Runtime")!.Text=$"Scribe 0.2 preview · {(OperatingSystem.IsBrowser()?"Avalonia WebAssembly":"Avalonia Desktop")} · Original document engine + original viewport";
    }
    private void Wire(string name,Action action)=>this.FindControl<Button>(name)!.Click+=(_,_)=>action();
    public void SelectDocument(int index)
    {
        index=Math.Clamp(index,0,Documents.Count-1);_current=index;var d=Documents[index];Editor.Document=d.Document;Editor.Language=d.Language;SecondaryEditor.Document=d.Document;SecondaryEditor.Language=d.Language;
        _title.Text=d.Name;this.FindControl<TextBlock>("Mode")!.Text=d.Language=="Plain Text"?"PLAIN TEXT":"CODE EDITOR";
        _info.Text=$"{d.Document.LineCount:N0} lines · {d.Document.Length:N0} UTF-16 units · Each view has independent selections and scrolling.";Editor.FocusEditor();
    }
    public void ToggleSplit()
    {
        Split=!Split;SecondaryEditor.IsVisible=Split;var columns=this.FindControl<Grid>("Editors")!.ColumnDefinitions;
        columns[1].Width=new GridLength(Split?5:0);columns[2].Width=Split?new GridLength(1,GridUnitType.Star):new GridLength(0);this.FindControl<GridSplitter>("SplitDivider")!.IsVisible=Split;
        _info.Text=Split?"One original Document, two independent editing sessions. Undo restores the initiating view's selections.":"Single editor view.";
    }
    public void ToggleTheme()=>Application.Current!.RequestedThemeVariant=Application.Current.ActualThemeVariant==ThemeVariant.Dark?ThemeVariant.Light:ThemeVariant.Dark;
    public void ToggleTemplate()
    {
        Minimal=!Minimal;Editor.Theme=Minimal?(ControlTheme?)Editor.FindResource("ScribeMinimalTheme"):null;Editor.ApplyTemplate();Editor.FocusEditor();
        _info.Text="The original EditorViewport instance, document, selections and undo history survive template replacement.";
    }
    public void GoTo(string expression)
    {
        var parts=expression.Split(':');int col=1;
        if(parts.Length>2||!int.TryParse(parts[0],out int line)||line<1||parts.Length==2&&(!int.TryParse(parts[1],out col)||col<1)){_info.Text="Use a positive line number, optionally :column.";return;}
        Editor.Session.GoTo(line,col);Editor.RevealCaret();Editor.FocusEditor();
    }
    public void LoadLarge(int count=100000)
    {
        if(count<1||count>250000)throw new ArgumentOutOfRangeException(nameof(count));var watch=Stopwatch.StartNew();var text=new StringBuilder(count*75);
        for(int i=0;i<count;i++){if(i>0)text.Append('\n');text.Append("Row ").Append((i+1).ToString("D6")).Append("  |  original persistent text buffer  |  visible-line rendering");}
        Documents.Add(($"Large-{count:N0}.txt",new(text.ToString()),"Plain Text"));SelectDocument(Documents.Count-1);LoadMilliseconds=watch.ElapsedMilliseconds;
        _info.Text=$"{count:N0} lines constructed and assigned in {LoadMilliseconds:N0} ms; first paint measured separately by browser validation.";
    }
    public async Task OpenAsync()
    {
        try
        {
            var storage=TopLevel.GetTopLevel(this)?.StorageProvider;if(storage is null||!storage.CanOpen){_info.Text="File picker is unavailable in this host.";return;}
            var files=await storage.OpenFilePickerAsync(new FilePickerOpenOptions{Title="Open text",AllowMultiple=false});if(files.Count==0)return;
            using var file=files[0];await using var stream=await file.OpenReadAsync();using var reader=new StreamReader(stream,new UTF8Encoding(false,true),true);
            var text=new StringBuilder();var buffer=new char[65536];int read;
            while((read=await reader.ReadAsync(buffer))>0){if(text.Length+read>20_000_000)throw new InvalidOperationException("Sample file limit: 20 million characters.");text.Append(buffer,0,read);}
            string language=Path.GetExtension(file.Name).ToLowerInvariant() switch{".cs"=>"C#",".json"=>"JSON",".py"=>"Python",_=>"Plain Text"};
            Documents.Add((file.Name,new(text.ToString()),language));SelectDocument(Documents.Count-1);
        }
        catch(Exception e){_info.Text="Open failed: "+e.Message;}
    }
    public async Task SaveAsync()
    {
        try
        {
            var storage=TopLevel.GetTopLevel(this)?.StorageProvider;if(storage is null||!storage.CanSave){_info.Text="Save picker is unavailable in this host.";return;}
            var document=Editor.Session.Document;string name=Documents[_current].Name;
            using var file=await storage.SaveFilePickerAsync(new FilePickerSaveOptions{Title="Save UTF-8 text",SuggestedFileName=name,ShowOverwritePrompt=true});if(file is null)return;
            long revision=document.Revision;var snapshot=document.Snapshot;
            await using(var stream=await file.OpenWriteAsync())
            {
                if(stream.CanSeek)stream.SetLength(0);await using var writer=new StreamWriter(stream,new UTF8Encoding(false));
                for(int offset=0;offset<snapshot.Length;){int length=Math.Min(65536,snapshot.Length-offset);if(offset+length<snapshot.Length&&char.IsHighSurrogate(snapshot[offset+length-1]))length--;await writer.WriteAsync(snapshot.GetText(offset,length));offset+=length;}
                await writer.FlushAsync();
            }
            _info.Text=document.MarkSaved(revision)?$"Saved {file.Name} as UTF-8.":"Snapshot saved; newer changes remain unsaved.";
        }
        catch(Exception e){_info.Text="Save failed: "+e.Message;}
    }
}
