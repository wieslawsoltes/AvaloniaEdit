using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Scribe.Avalonia;
using Scribe.Core;
using Scribe.Sample;
namespace Scribe.Browser;

/// <summary>Sample-only state observation and fixture setup. Never included in reusable packages.</summary>
public static partial class TestBridge
{
    public static bool Enabled {get;set;}
    private static MainView View=>Enabled&&App.Gallery is not null?App.Gallery:throw new InvalidOperationException("Validation bridge disabled.");
    [JSExport]
    public static string State()
    {
        var view=View;var editor=view.Editor;var session=editor.Session;var document=session.Document;var buffer=document.Snapshot;var selection=session.Primary;var viewport=editor.Viewport;
        var state=new EditorState
        {
            Text=buffer.GetText(0,Math.Min(buffer.Length,200000)),TextTruncated=buffer.Length>200000,Length=buffer.Length,Lines=buffer.LineCount,
            Revision=document.Revision,Dirty=document.IsDirty,CanUndo=document.CanUndo,CanRedo=document.CanRedo,Caret=selection.Caret,
            Starts=session.Selections.Select(s=>s.Start).ToArray(),Lengths=session.Selections.Select(s=>s.Length).ToArray(),Carets=session.Selections.Select(s=>s.Caret).ToArray(),
            Line=buffer.LineAt(selection.Caret)+1,Column=selection.Caret-buffer.GetLineStart(buffer.LineAt(selection.Caret))+1,
            ReadOnly=!editor.CanEdit,Wrap=editor.WordWrap,SearchOpen=editor.IsSearchOpen,SearchMatches=editor.Search.Results.Matches.Count,SearchStatus=editor.Search.Status,
            FoldCount=editor.Folds.Count,CollapsedCount=editor.CollapsedLines.Count,CompletionCount=editor.CompletionCount,DiagnosticCount=editor.Diagnostics.Count,
            Bookmarks=session.Bookmarks.Count,SnippetActive=editor.Snippet?.IsActive==true,Split=view.Split,Minimal=view.Minimal,Large=editor.IsLargeFile,LoadMs=view.LoadMilliseconds,
            VisualLines=viewport.RealizedLineCount,CacheCount=viewport.CachedLayoutCount,VisualRows=viewport.VisualRowCount,FirstVisibleLine=viewport.FirstVisibleLine+1,
            ShapeCount=viewport.ShapedLineCount,RenderCount=viewport.RenderCount,OffsetY=viewport.OffsetY,Theme=Application.Current!.ActualThemeVariant.ToString(),
            ViewportType=viewport.GetType().FullName??"",ViewportBase=viewport.GetType().BaseType?.FullName??"",ViewportId=RuntimeHelpers.GetHashCode(viewport),
            Preedit=viewport.PreeditText,Status=editor.Status,Message=editor.Message,SecondaryCaret=view.SecondaryEditor.Session.Primary.Caret,
            SharedDocument=ReferenceEquals(document,view.SecondaryEditor.Session.Document),SecondaryLength=view.SecondaryEditor.Session.Document.Length,
            UndoSourceAssembly=typeof(Document).Assembly.GetName().Name??""
        };
        return JsonSerializer.Serialize(state,BridgeJson.Default.EditorState);
    }
    private static Control Find(string name)
    {
        var view=View;
        return name switch
        {
            "editor"=>view.Editor.Viewport,
            "secondary"=>view.SecondaryEditor.Viewport,
            "find-query"=>view.Editor.GetVisualDescendants().OfType<TextBox>().First(c=>c.Name=="PART_Query"),
            "replace-query"=>view.Editor.GetVisualDescendants().OfType<TextBox>().First(c=>AutomationProperties.GetName(c)=="Replacement text"),
            _=>view.FindControl<Control>(name)??throw new ArgumentException("Unknown control: "+name)
        };
    }
    [JSExport]
    public static string Bounds(string name)
    {
        var control=Find(name);var point=control.TranslatePoint(new global::Avalonia.Point(0,0),View)??throw new InvalidOperationException("Control detached.");
        return FormattableString.Invariant($"{{\"x\":{point.X},\"y\":{point.Y},\"width\":{control.Bounds.Width},\"height\":{control.Bounds.Height}}}");
    }
    [JSExport]
    public static string Point(int offset)
    {
        var viewport=View.Editor.Viewport;var rect=viewport.GetCaretRectangle(offset);var point=viewport.TranslatePoint(new global::Avalonia.Point(rect.X,rect.Y+rect.Height*.5),View)??throw new InvalidOperationException("Viewport detached.");
        return FormattableString.Invariant($"{{\"x\":{point.X},\"y\":{point.Y}}}");
    }
    [JSExport]
    public static async Task<string> Execute(string command,string argument)
    {
        var view=View;var editor=view.Editor;
        switch(command)
        {
            case "reset":
                editor.CloseCompletion();editor.Snippet?.Cancel();editor.IsReadOnly=false;editor.Session.Document.IsReadOnly=false;
                editor.IsSearchOpen=false;editor.Search.Pattern="";editor.Search.InSelection=false;editor.Search.UseRegex=false;editor.Search.WholeWord=false;editor.Search.CaseSensitive=false;
                editor.FoldAll(false);editor.Session.Bookmarks.Clear();editor.Session.Document.Reset(argument);editor.Language="Plain Text";editor.AutoPairs=false;editor.WordWrap=false;
                editor.Session.Select(Selection.At(0));editor.Viewport.ScrollTo(0,0);editor.FocusEditor();break;
            case "document":view.SelectDocument(int.Parse(argument,CultureInfo.InvariantCulture));break;
            case "language":editor.Language=argument;await editor.RefreshAnalysisAsync();break;
            case "select":
                editor.Session.Select(argument.Split(';').Select(s=>{var parts=s.Split(':');return new Selection(int.Parse(parts[0],CultureInfo.InvariantCulture),int.Parse(parts[1],CultureInfo.InvariantCulture));}).ToArray());editor.RevealCaret();editor.FocusEditor();break;
            case "caret":editor.Session.Select(Selection.At(int.Parse(argument,CultureInfo.InvariantCulture)));editor.RevealCaret();editor.FocusEditor();break;
            case "focus":editor.FocusEditor();break;
            case "focus-secondary":view.SecondaryEditor.FocusEditor();break;
            case "readonly":editor.IsReadOnly=argument=="true";break;
            case "document-readonly":editor.Session.Document.IsReadOnly=argument=="true";break;
            case "wrap":editor.WordWrap=argument=="true";editor.RevealCaret();break;
            case "whitespace":editor.ShowWhitespace=argument=="true";break;
            case "pairs":editor.AutoPairs=argument=="true";break;
            case "zoom":editor.Zoom=double.Parse(argument,CultureInfo.InvariantCulture);break;
            case "undo":editor.UndoCommand.Execute(null);break;
            case "redo":editor.RedoCommand.Execute(null);break;
            case "saved":editor.Session.Document.MarkSaved();break;
            case "find":case "replace-all":
                var q=JsonSerializer.Deserialize(argument,BridgeJson.Default.SearchArguments)??new();
                editor.Search.Pattern=q.Pattern;editor.Search.Replacement=q.Replacement;editor.Search.CaseSensitive=q.Case;editor.Search.UseRegex=q.Regex;editor.Search.WholeWord=q.Word;editor.Search.InSelection=q.Selection;
                editor.IsSearchOpen=true;editor.Search.Refresh();if(command=="replace-all")editor.Search.Replace(true);break;
            case "next-match":editor.Search.Navigate();break;
            case "previous-match":editor.Search.Navigate(true);break;
            case "replace-current":editor.Search.Replace(false);break;
            case "theme":view.ToggleTheme();break;
            case "template":view.ToggleTemplate();break;
            case "split":view.ToggleSplit();break;
            case "fold":await editor.RefreshAnalysisAsync();editor.FoldAll(argument!="false");break;
            case "analysis":await editor.RefreshAnalysisAsync();break;
            case "complete":editor.FocusEditor();await editor.ShowCompletionAsync();break;
            case "accept-completion":editor.AcceptCompletion();break;
            case "snippet":editor.InsertSnippet(argument);break;
            case "snippet-next":editor.Snippet?.Next();editor.RevealCaret();break;
            case "bookmark":editor.Session.ToggleBookmark();break;
            case "next-bookmark":editor.Session.NextBookmark();editor.RevealCaret();break;
            case "comment":editor.Session.ToggleComment();editor.RevealCaret();break;
            case "duplicate":editor.Session.Duplicate();editor.RevealCaret();break;
            case "trim":editor.Session.TrimWhitespace();break;
            case "indent":editor.Session.Indent();break;
            case "unindent":editor.Session.Indent(true);break;
            case "upper":editor.Session.ChangeCase(true);break;
            case "goto":view.GoTo(argument);break;
            case "large":view.LoadLarge(int.Parse(argument,CultureInfo.InvariantCulture));break;
            case "scroll":editor.Viewport.ScrollTo(0,double.Parse(argument,CultureInfo.InvariantCulture));break;
            default:throw new ArgumentException("Unknown sample command: "+command);
        }
        return State();
    }
}
public sealed class SearchArguments
{
    public string Pattern {get;set;}="";
    public string Replacement {get;set;}="";
    public bool Regex {get;set;}
    public bool Case {get;set;}
    public bool Word {get;set;}
    public bool Selection {get;set;}
}
public sealed class EditorState
{
    public string Text {get;set;}="";
    public bool TextTruncated {get;set;}
    public int Length {get;set;}
    public int Lines {get;set;}
    public long Revision {get;set;}
    public bool Dirty {get;set;}
    public bool CanUndo {get;set;}
    public bool CanRedo {get;set;}
    public int Caret {get;set;}
    public int[] Starts {get;set;}=[];
    public int[] Lengths {get;set;}=[];
    public int[] Carets {get;set;}=[];
    public int Line {get;set;}
    public int Column {get;set;}
    public bool ReadOnly {get;set;}
    public bool Wrap {get;set;}
    public bool SearchOpen {get;set;}
    public int SearchMatches {get;set;}
    public string SearchStatus {get;set;}="";
    public int FoldCount {get;set;}
    public int CollapsedCount {get;set;}
    public int CompletionCount {get;set;}
    public int DiagnosticCount {get;set;}
    public int Bookmarks {get;set;}
    public bool SnippetActive {get;set;}
    public bool Split {get;set;}
    public bool Minimal {get;set;}
    public bool Large {get;set;}
    public long LoadMs {get;set;}
    public int VisualLines {get;set;}
    public int CacheCount {get;set;}
    public int VisualRows {get;set;}
    public int FirstVisibleLine {get;set;}
    public long ShapeCount {get;set;}
    public long RenderCount {get;set;}
    public double OffsetY {get;set;}
    public string Theme {get;set;}="";
    public string ViewportType {get;set;}="";
    public string ViewportBase {get;set;}="";
    public int ViewportId {get;set;}
    public string Preedit {get;set;}="";
    public string Status {get;set;}="";
    public string Message {get;set;}="";
    public int SecondaryCaret {get;set;}
    public bool SharedDocument {get;set;}
    public int SecondaryLength {get;set;}
    public string UndoSourceAssembly {get;set;}="";
}
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EditorState))]
[JsonSerializable(typeof(SearchArguments))]
internal partial class BridgeJson : JsonSerializerContext { }
