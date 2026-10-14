using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ProText.Sample;
using ProText.Core;
namespace ProText.Browser;
/// <summary>Gallery-only validation bridge. Not part of either reusable package; gated by ?test=1.</summary>
public static partial class TestBridge
{
    public static bool Enabled { get; set; }
    private static MainView View => Enabled && App.Gallery is not null ? App.Gallery : throw new InvalidOperationException("The test bridge is disabled.");
    [JSExport]
    public static string GetState()
    {
        var view=View; var editor=view.Primary; var session=editor.Session!; var native=editor.Editor;
        var state=new EditorState {
            Text=session.Length<=200000?session.Text:session.Document.GetText(0,200000),TextTruncated=session.Length>200000,
            SecondaryText=view.Secondary.Session!.Length<=200000?view.Secondary.Text:"",Revision=session.Revision,Length=session.Length,Lines=session.LineCount,
            SelectionStart=native.SelectionStart,SelectionLength=native.SelectionLength,Caret=native.CaretOffset,Line=native.TextArea.Caret.Line,Column=native.TextArea.Caret.Column,
            IsDirty=session.IsDirty,ReadOnly=editor.EffectiveReadOnly,WordWrap=editor.WordWrap,SearchOpen=editor.IsSearchOpen,SearchMatches=editor.Search.Hits.Count,SearchMessage=editor.Search.Message,
            Foldings=editor.FoldingCount,Folded=editor.FoldedCount,Completions=editor.CompletionCount,Diagnostics=session.Diagnostics.Count,Bookmarks=session.Bookmarks.Count,
            IsSplit=view.IsSplit,MinimalTemplate=view.IsMinimalTemplate,LargeFile=editor.IsLargeFile,LastLoadMs=view.LastLoadMilliseconds,Theme=Application.Current!.ActualThemeVariant.ToString(),
            VisualLines=native.TextArea.TextView.VisualLinesValid?native.TextArea.TextView.VisualLines.Count:0,Status=editor.StatusText,Message=editor.LastMessage,
            DiffHunks=view.Diff.Result.Hunks.Count,DiffVisible=view.Diff.IsVisible,CanUndo=native.CanUndo,CanRedo=native.CanRedo,
            VisibleFirstLine=native.TextArea.TextView.VisualLinesValid&&native.TextArea.TextView.VisualLines.Count>0?native.TextArea.TextView.VisualLines[0].FirstDocumentLine.LineNumber:0
        };
        return JsonSerializer.Serialize(state,BridgeJsonContext.Default.EditorState);
    }
    [JSExport]
    public static string GetBounds(string name)
    {
        var view=View;
        Control? control=name switch {
            "editor"=>view.Primary.Editor.TextArea,"secondary"=>view.Secondary.Editor.TextArea,
            "find-query"=>view.Primary.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(c=>c.Name=="PART_Query"),
            "replace-query"=>view.Primary.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(c=>AutomationProperties.GetName(c)=="Replacement text"),
            _=>view.FindControl<Control>(name)
        };
        if(control is null) throw new ArgumentException("Unknown control: "+name);
        var point=control.TranslatePoint(new Point(0,0),view)??throw new InvalidOperationException("Control is not attached.");
        return FormattableString.Invariant($"{{\"x\":{point.X},\"y\":{point.Y},\"width\":{control.Bounds.Width},\"height\":{control.Bounds.Height}}}");
    }
    [JSExport]
    public static async Task<string> Execute(string command,string argument)
    {
        var view=View; var editor=view.Primary;
        switch(command)
        {
            case "reset":
                editor.IsReadOnly=false; editor.Session!.IsReadOnly=false; editor.Session.Reset(argument); editor.IsSearchOpen=false; editor.IsGoToOpen=false;
                editor.Search.Pattern=""; editor.Search.InSelection=false; editor.Search.UseRegex=false; editor.Search.MatchCase=false; editor.Search.WholeWord=false;
                editor.Editor.Select(0,0); editor.Editor.CaretOffset=0; editor.FocusEditor(); await editor.RefreshAnalysisAsync(); break;
            case "focus": editor.FocusEditor(); break;
            case "focus-secondary": view.Secondary.FocusEditor(); break;
            case "select":
                var parts=argument.Split(':'); editor.Editor.Select(int.Parse(parts[0],CultureInfo.InvariantCulture),int.Parse(parts[1],CultureInfo.InvariantCulture)); editor.FocusEditor(); break;
            case "caret": editor.Editor.CaretOffset=int.Parse(argument,CultureInfo.InvariantCulture); editor.FocusEditor(); break;
            case "language": editor.Session!.Language=argument; await editor.RefreshAnalysisAsync(); break;
            case "readonly": editor.IsReadOnly=argument=="true"; break;
            case "session-readonly": editor.Session!.IsReadOnly=argument=="true"; break;
            case "wrap": editor.WordWrap=argument=="true"; break;
            case "whitespace": editor.ShowWhitespace=argument=="true"; break;
            case "pairs": editor.EnableAutoPairs=argument=="true"; break;
            case "undo": editor.UndoCommand.Execute(null); break;
            case "redo": editor.RedoCommand.Execute(null); break;
            case "mark-saved": editor.Session!.MarkSaved(); break;
            case "find":
            case "replace-all":
                var search=JsonSerializer.Deserialize(argument,BridgeJsonContext.Default.SearchArguments)??new();
                editor.Search.Pattern=search.Pattern; editor.Search.Replacement=search.Replacement; editor.Search.UseRegex=search.Regex;
                editor.Search.MatchCase=search.Case; editor.Search.WholeWord=search.Word; editor.Search.InSelection=search.Selection;
                editor.IsSearchOpen=true; editor.Search.Refresh(); if(command=="replace-all") editor.Search.ReplaceAll(); break;
            case "next-match": editor.Search.Navigate(); break;
            case "previous-match": editor.Search.Navigate(true); break;
            case "replace-current": editor.Search.ReplaceCurrent(); break;
            case "theme": view.ToggleTheme(); break;
            case "template": view.ToggleTemplate(); break;
            case "split": view.ToggleSplit(); break;
            case "fold": editor.FoldAll(argument!="false"); break;
            case "complete": editor.FocusEditor(); await editor.ShowCompletionAsync(); break;
            case "accept-completion": editor.AcceptCompletion(); break;
            case "bookmark": editor.ToggleBookmark(); break;
            case "next-bookmark": editor.NextBookmark(); break;
            case "comment": editor.ToggleComment(); break;
            case "duplicate": editor.DuplicateSelectionOrLine(); break;
            case "trim": editor.TrimTrailingWhitespace(); break;
            case "indent": editor.IndentSelection(); break;
            case "unindent": editor.IndentSelection(true); break;
            case "upper": editor.ChangeCase(true); break;
            case "lower": editor.ChangeCase(false); break;
            case "sort": editor.SortSelectedLines(); break;
            case "goto": editor.GoToText=argument; editor.ExecuteGoTo(); break;
            case "analysis": await editor.RefreshAnalysisAsync(); break;
            case "large": view.LoadLarge(string.IsNullOrEmpty(argument)?100000:int.Parse(argument,CultureInfo.InvariantCulture)); break;
            case "document": view.SwitchDocument(int.Parse(argument,CultureInfo.InvariantCulture)); break;
            case "diff": view.ShowDiff(); break;
            case "line-ending": editor.Session!.ConvertLineEndings(Enum.Parse<LineEnding>(argument)); break;
            default: throw new ArgumentException("Unknown gallery test command: "+command);
        }
        return GetState();
    }
}
public sealed class SearchArguments
{
    public string Pattern { get; set; }="";
    public string Replacement { get; set; }="";
    public bool Regex { get; set; }
    public bool Case { get; set; }
    public bool Word { get; set; }
    public bool Selection { get; set; }
}
public sealed class EditorState
{
    public string Text { get; set; }="";
    public string SecondaryText { get; set; }="";
    public bool TextTruncated { get; set; }
    public long Revision { get; set; }
    public int Length { get; set; }
    public int Lines { get; set; }
    public int SelectionStart { get; set; }
    public int SelectionLength { get; set; }
    public int Caret { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public bool IsDirty { get; set; }
    public bool ReadOnly { get; set; }
    public bool WordWrap { get; set; }
    public bool SearchOpen { get; set; }
    public int SearchMatches { get; set; }
    public string SearchMessage { get; set; }="";
    public int Foldings { get; set; }
    public int Folded { get; set; }
    public int Completions { get; set; }
    public int Diagnostics { get; set; }
    public int Bookmarks { get; set; }
    public bool IsSplit { get; set; }
    public bool MinimalTemplate { get; set; }
    public bool LargeFile { get; set; }
    public long LastLoadMs { get; set; }
    public string Theme { get; set; }="";
    public int VisualLines { get; set; }
    public int VisibleFirstLine { get; set; }
    public string Status { get; set; }="";
    public string Message { get; set; }="";
    public int DiffHunks { get; set; }
    public bool DiffVisible { get; set; }
    public bool CanUndo { get; set; }
    public bool CanRedo { get; set; }
}
[JsonSerializable(typeof(EditorState))]
[JsonSerializable(typeof(SearchArguments))]
internal partial class BridgeJsonContext : JsonSerializerContext { }
