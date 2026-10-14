using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using ProText.Avalonia;
using ProText.Avalonia.Controls;
using ProText.Core;
namespace ProText.Sample;
public partial class MainView : UserControl
{
    public ProTextEditor Primary { get; }
    public ProTextEditor Secondary { get; }
    public TextDiffEditor Diff { get; }
    public List<EditorSession> Documents { get; }=[];
    public long LastLoadMilliseconds { get; private set; }
    public bool IsSplit { get; private set; }
    public bool IsMinimalTemplate { get; private set; }
    private bool _folded;
    private readonly Grid _editors,_workspace;
    private readonly TextBlock _information,_title;
    public MainView()
    {
        AvaloniaXamlLoader.Load(this);
        Primary=this.FindControl<ProTextEditor>("PrimaryEditor")!; Secondary=this.FindControl<ProTextEditor>("SecondaryEditor")!;
        Diff=this.FindControl<TextDiffEditor>("DiffView")!; _editors=this.FindControl<Grid>("Editors")!; _workspace=this.FindControl<Grid>("Workspace")!;
        _information=this.FindControl<TextBlock>("Information")!; _title=this.FindControl<TextBlock>("DocumentTitle")!;
        Documents.Add(new(SampleDocuments.Code,"Welcome.cs","C#")); Documents.Add(new(SampleDocuments.Json,"settings.json","JSON")); Documents.Add(new(SampleDocuments.Notes,"Notes.txt","Plain Text"));
        Configure(Primary); Configure(Secondary); SwitchDocument(0);
        Bind("NewButton",()=>{ Documents.Add(new("",$"Untitled-{Documents.Count+1}.txt")); SwitchDocument(Documents.Count-1); });
        this.FindControl<Button>("OpenButton")!.Click+=async(_,_)=>await OpenAsync();
        this.FindControl<Button>("SaveButton")!.Click+=async(_,_)=>await SaveAsync();
        Bind("UndoButton",()=>Primary.UndoCommand.Execute(null)); Bind("RedoButton",()=>Primary.RedoCommand.Execute(null));
        Bind("FindButton",Primary.OpenSearch); Bind("GoToButton",Primary.OpenGoTo);
        Bind("SplitButton",ToggleSplit); Bind("ThemeButton",ToggleTheme); Bind("TemplateButton",ToggleTemplate);
        Bind("CodeButton",()=>SwitchDocument(0)); Bind("JsonButton",()=>SwitchDocument(1)); Bind("TextButton",()=>SwitchDocument(2));
        Bind("LargeButton",()=>LoadLarge()); Bind("DiffButton",ShowDiff);
        Bind("FoldButton",()=>{ _folded=!_folded; Primary.FoldAll(_folded); });
        this.FindControl<Button>("CompleteButton")!.Click+=async(_,_)=>{ Primary.FocusEditor(); await Primary.ShowCompletionAsync(); };
        Check("WrapCheck",value=>{ Primary.WordWrap=value; Secondary.WordWrap=value; });
        Check("WhitespaceCheck",value=>{ Primary.ShowWhitespace=value; Secondary.ShowWhitespace=value; });
        Check("ReadOnlyCheck",value=>Primary.IsReadOnly=value); Check("OverviewCheck",value=>Primary.ShowOverview=value);
        SizeChanged+=(_,e)=>
        {
            bool wide=e.NewSize.Width>=900;
            this.FindControl<Border>("Sidebar")!.IsVisible=wide;
            _workspace.ColumnDefinitions[0].Width=new GridLength(wide?220:0);
        };
        this.FindControl<TextBlock>("RuntimeLabel")!.Text=OperatingSystem.IsBrowser()?"WebAssembly · native Avalonia rendering":"Desktop · native Avalonia rendering";
    }
    private void Bind(string name,Action action)=>this.FindControl<Button>(name)!.Click+=(_,_)=>action();
    private void Check(string name,Action<bool> action)=>this.FindControl<CheckBox>(name)!.IsCheckedChanged+=(_,_)=>action(this.FindControl<CheckBox>(name)!.IsChecked==true);
    private void Configure(ProTextEditor editor)
    {
        editor.CompletionProvider=new KeywordCompletionProvider(new[] {
            new CompletionItem("Console","Console","System.Console","class",10),new CompletionItem("Console.WriteLine","Console.WriteLine(\"Hello\");","Write a line to standard output","method",20),
            new CompletionItem("public","public","Public visibility","keyword"),new CompletionItem("private","private","Private visibility","keyword"),
            new CompletionItem("async","async","Asynchronous method","keyword"),new CompletionItem("await","await","Await an operation","keyword"),
            new CompletionItem("foreach","foreach (var item in collection)\n{\n    \n}","Iteration snippet","snippet"),new CompletionItem("Task","Task","System.Threading.Tasks.Task","class") });
        editor.DiagnosticProvider=new SampleDiagnostics();
        editor.ServiceError+=(_,error)=>_information.Text="Service error: "+error.Message;
    }
    public void SwitchDocument(int index)
    {
        index=Math.Clamp(index,0,Documents.Count-1); var session=Documents[index];
        Primary.Session=session; Secondary.Session=session;
        Diff.IsVisible=false; _editors.IsVisible=true; _title.Text=session.FileName;
        this.FindControl<TextBlock>("ModeLabel")!.Text=session.Language=="Plain Text"?"TEXT EDITOR":"CODE EDITOR";
        _information.Text=$"{session.LineCount:N0} lines · {session.Length:N0} UTF-16 units · Changes remain in memory when switching documents.";
        Primary.FocusEditor();
    }
    public void ToggleSplit()
    {
        IsSplit=!IsSplit; Secondary.Session=Primary.Session; Secondary.IsVisible=IsSplit;
        this.FindControl<GridSplitter>("SplitDivider")!.IsVisible=IsSplit;
        _editors.ColumnDefinitions[1].Width=new GridLength(IsSplit?5:0);
        _editors.ColumnDefinitions[2].Width=IsSplit?new GridLength(1,GridUnitType.Star):new GridLength(0);
        _information.Text=IsSplit?"Both views share the same document and undo history. Each keeps its own selection and scroll position.":"Single editor view.";
    }
    public void ToggleTheme()=>Application.Current!.RequestedThemeVariant=Application.Current.ActualThemeVariant==ThemeVariant.Dark?ThemeVariant.Light:ThemeVariant.Dark;
    public void ToggleTemplate()
    {
        IsMinimalTemplate=!IsMinimalTemplate;
        Primary.Theme=IsMinimalTemplate?(ControlTheme?)Primary.FindResource("ProTextMinimalTheme"):null;
        Primary.ApplyTemplate(); Primary.FocusEditor();
        _information.Text=IsMinimalTemplate?"Minimal ControlTheme applied. The editing surface, document, selection and undo stack are unchanged.":"Default ControlTheme restored.";
    }
    public void LoadLarge(int lines=100000)
    {
        if(lines is <1 or >250000) throw new ArgumentOutOfRangeException(nameof(lines));
        var watch=Stopwatch.StartNew(); var builder=new StringBuilder(lines*70);
        for(int i=1;i<=lines;i++) { if(i>1) builder.Append('\n'); builder.Append("// Row ").Append(i.ToString("D6")).Append(" | virtualized text document | no visual object per document line"); }
        var session=new EditorSession(builder.ToString(),$"Large-{lines:N0}.txt","Plain Text"); Documents.Add(session); SwitchDocument(Documents.Count-1);
        LastLoadMilliseconds=watch.ElapsedMilliseconds;
        _information.Text=$"{lines:N0} lines loaded in {LastLoadMilliseconds:N0} ms (document construction and assignment, excluding first paint). Editing stays enabled; expensive analysis is paused.";
    }
    public void ShowDiff()
    {
        Diff.LeftText=SampleDocuments.Code; Diff.RightText=SampleDocuments.Code.Replace("Hello, Avalonia!","Hello, ProText!").Replace("return width * height;","var area = width * height;\n        return area;");
        Diff.LeftEditor.Session!.Language="C#"; Diff.RightEditor.Session!.Language="C#";
        _editors.IsVisible=false; Diff.IsVisible=true; _title.Text="Welcome.cs — revision comparison";
        _information.Text="Side-by-side line diff. Previous / next change navigates both views. Large dissimilar inputs use a bounded coarse comparison.";
    }
    private async Task OpenAsync()
    {
        try
        {
            var storage=TopLevel.GetTopLevel(this)?.StorageProvider;
            if(storage is null||!storage.CanOpen) { _information.Text="This host does not expose a file picker."; return; }
            var files=await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title="Open text document",AllowMultiple=false,FileTypeFilter=[FilePickerFileTypes.TextPlain,FilePickerFileTypes.All] });
            if(files.Count==0) return;
            using var file=files[0]; await using var stream=await file.OpenReadAsync();
            using var reader=new StreamReader(stream,new UTF8Encoding(false,true),true);
            var builder=new StringBuilder(); var buffer=new char[65536]; int count;
            while((count=await reader.ReadAsync(buffer))>0) { if(builder.Length+count>20_000_000) throw new InvalidOperationException("The sample limits opened documents to 20 million characters."); builder.Append(buffer,0,count); }
            string language=Path.GetExtension(file.Name).ToLowerInvariant() switch { ".cs"=>"C#",".json"=>"JSON",".py"=>"Python",".xml"=>"XML",".js"=>"JavaScript",_=>"Plain Text" };
            var session=new EditorSession(builder.ToString(),file.Name,language) { EncodingName=reader.CurrentEncoding.WebName };
            Documents.Add(session); SwitchDocument(Documents.Count-1);
        }
        catch(Exception ex) { _information.Text="Open failed: "+ex.Message; }
    }
    private async Task SaveAsync()
    {
        try
        {
            var storage=TopLevel.GetTopLevel(this)?.StorageProvider; var session=Primary.Session;
            if(storage is null||session is null||!storage.CanSave) { _information.Text="This host does not expose a save picker."; return; }
            using var file=await storage.SaveFilePickerAsync(new FilePickerSaveOptions { Title="Save UTF-8 document",SuggestedFileName=session.FileName,ShowOverwritePrompt=true });
            if(file is null) return;
            long revision=session.Revision; string text=session.Text;
            await using(var stream=await file.OpenWriteAsync())
            {
                if(stream.CanSeek) stream.SetLength(0);
                await using var writer=new StreamWriter(stream,new UTF8Encoding(false)); await writer.WriteAsync(text); await writer.FlushAsync();
            }
            session.FileName=file.Name; session.EncodingName="UTF-8"; bool unchanged=session.MarkSaved(revision);
            _information.Text=unchanged?$"Saved {file.Name} as UTF-8.":$"Saved snapshot of {file.Name}; newer edits are still unsaved.";
        }
        catch(Exception ex) { _information.Text="Save failed: "+ex.Message; }
    }
}
