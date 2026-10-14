using Avalonia.Controls;
using Avalonia.Input;
using AvaloniaEdit.Document;
using ProText.Core;
namespace ProText.Avalonia.Controls;
public partial class ProTextEditor
{
    private readonly List<(TextAnchor Anchor,char Closing)> _autoPairs=[];
    public void OpenSearch()
    {
        if(Editor.SelectionLength>0 && Editor.SelectionLength<512 && !Editor.SelectedText.Contains('\n')) Search.Pattern=Editor.SelectedText;
        IsSearchOpen=true; _searchBar?.FocusQuery();
    }
    public void OpenGoTo() { GoToText=Editor.TextArea.Caret.Line.ToString(); IsGoToOpen=true; }
    private void OnGoToKey(object? sender,KeyEventArgs e)
    {
        if(e.Key==Key.Enter) { ExecuteGoTo(); e.Handled=true; }
        else if(e.Key==Key.Escape) { IsGoToOpen=false; FocusEditor(); e.Handled=true; }
    }
    public bool ExecuteGoTo()
    {
        var parts=GoToText.Split(':'); int column=1;
        if(parts.Length>2 || !int.TryParse(parts[0],out int line) || line<1 || (parts.Length==2 && (!int.TryParse(parts[1],out column)||column<1))) { LastMessage="Enter a positive line number, optionally followed by :column."; return false; }
        GoTo(line,column); IsGoToOpen=false; FocusEditor(); return true;
    }
    public void GoTo(int line,int column=1)
    {
        var document=Editor.Document; line=Math.Clamp(line,1,document.LineCount);
        var target=document.GetLineByNumber(line); column=Math.Clamp(column,1,target.Length+1);
        Editor.Select(target.Offset+column-1,0); Editor.CaretOffset=target.Offset+column-1;
        Editor.ScrollTo(line,column); Editor.TextArea.Caret.BringCaretToView();
    }
    public void Reveal(TextRange range)
    {
        if(range.End>Editor.Document.TextLength) throw new ArgumentOutOfRangeException(nameof(range));
        Editor.Select(range.Offset,range.Length); Editor.TextArea.Caret.BringCaretToView();
    }
    public void FoldAll(bool folded=true)
    {
        if(_folding is null) return; foreach(var fold in _folding.AllFoldings) fold.IsFolded=folded; Redraw();
    }
    public void ToggleCurrentFold()
    {
        var fold=_folding?.AllFoldings.LastOrDefault(f=>f.StartOffset<=Editor.CaretOffset&&f.EndOffset>Editor.CaretOffset);
        if(fold is not null) { fold.IsFolded=!fold.IsFolded; Redraw(); }
    }
    public void ToggleBookmark()=>Session?.ToggleBookmark(Editor.TextArea.Caret.Line);
    public void NextBookmark(bool backwards=false) { if(Session is not null) GoTo(Session.FindBookmark(Editor.TextArea.Caret.Line,backwards)); }
    private TextRange SelectionRange=>new(Editor.SelectionStart,Editor.SelectionLength);
    public void ToggleComment()
    {
        if(EffectiveReadOnly) return;
        string prefix=Session!.Language is "Python" or "Shell" ? "#" : "//";
        Session.ApplyEdits(TextOperations.ToggleLineComment(Session.Text,SelectionRange,prefix));
    }
    public void TrimTrailingWhitespace() { if(!EffectiveReadOnly) Session!.ApplyEdits(TextOperations.TrimTrailingWhitespace(Session.Text)); }
    public void IndentSelection(bool unindent=false) { if(!EffectiveReadOnly) Session!.ApplyEdits(TextOperations.Indent(Session.Text,SelectionRange,TabSize,UseTabs,unindent)); }
    public void ChangeCase(bool upper)
    {
        if(EffectiveReadOnly||Editor.SelectionLength==0) return;
        var range=SelectionRange; var replacement=upper?Editor.SelectedText.ToUpperInvariant():Editor.SelectedText.ToLowerInvariant();
        Session!.ApplyEdits([new(range,replacement)]); Editor.Select(range.Offset,replacement.Length);
    }
    public void SortSelectedLines()
    {
        if(EffectiveReadOnly) return;
        var text=Session!.Text; var range=new LineMap(text).ExpandToLines(SelectionRange);
        var lines=TextOperations.NormalizeLineEndings(text.Substring(range.Offset,range.Length),LineEnding.Lf).Split('\n');
        Array.Sort(lines,StringComparer.Ordinal);
        var replacement=string.Join(TextOperations.Delimiter(Session.LineEnding),lines);
        Session.ApplyEdits([new(range,replacement)]); Editor.Select(range.Offset,replacement.Length);
    }
    public void DuplicateSelectionOrLine()
    {
        if(EffectiveReadOnly) return;
        if(Editor.SelectionLength>0)
        {
            int end=Editor.SelectionStart+Editor.SelectionLength; string copy=Editor.SelectedText;
            Session!.ApplyEdits([new(new(end,0),copy)]); Editor.Select(end,copy.Length);
        }
        else
        {
            int caret=Editor.CaretOffset; var line=Editor.Document.GetLineByOffset(caret);
            string copy=Editor.Document.GetText(line.Offset,line.TotalLength);
            if(line.DelimiterLength==0)
            {
                string insertion=TextOperations.Delimiter(Session!.LineEnding)+copy;
                int offset=line.EndOffset; Session.ApplyEdits([new(new(offset,0),insertion)]);
                Editor.CaretOffset=Math.Min(Session.Length,caret+insertion.Length);
            }
            else
            {
                int offset=line.Offset; Session!.ApplyEdits([new(new(offset,0),copy)]);
                Editor.CaretOffset=Math.Min(Session.Length,caret+copy.Length);
            }
        }
    }
    public void DeleteSelectedLines()
    {
        if(EffectiveReadOnly) return;
        var document=Editor.Document; var first=document.GetLineByOffset(Editor.SelectionStart);
        var last=document.GetLineByOffset(Editor.SelectionLength>0?Editor.SelectionStart+Editor.SelectionLength-1:Editor.SelectionStart);
        int start=first.Offset,end=last.Offset+last.TotalLength;
        if(last.DelimiterLength==0&&start>0) start-=document.GetLineByNumber(first.LineNumber-1).DelimiterLength;
        Session!.ApplyEdits([new(new(start,end-start),"")]); Editor.CaretOffset=Math.Min(start,document.TextLength);
    }
    private async void OnEditorKeyDown(object? sender,KeyEventArgs e)
    {
        bool primary=(e.KeyModifiers&(KeyModifiers.Control|KeyModifiers.Meta))!=0&&(e.KeyModifiers&KeyModifiers.Alt)==0;
        bool shift=(e.KeyModifiers&KeyModifiers.Shift)!=0;
        if(primary)
        {
            switch(e.Key)
            {
                case Key.F: case Key.H: OpenSearch(); e.Handled=true; return;
                case Key.G: OpenGoTo(); e.Handled=true; return;
                case Key.Space: e.Handled=true; await ShowCompletionAsync(); return;
                case Key.OemPlus: case Key.Add: Zoom=Math.Min(3,Zoom+.1); e.Handled=true; return;
                case Key.OemMinus: case Key.Subtract: Zoom=Math.Max(.5,Zoom-.1); e.Handled=true; return;
                case Key.D0: Zoom=1; e.Handled=true; return;
                case Key.OemQuestion: ToggleComment(); e.Handled=true; return;
                case Key.D when shift: DuplicateSelectionOrLine(); e.Handled=true; return;
                case Key.K when shift: DeleteSelectedLines(); e.Handled=true; return;
                case Key.F2: ToggleBookmark(); e.Handled=true; return;
            }
        }
        if(e.Key==Key.F3) { Search.Navigate(shift); e.Handled=true; }
        else if(e.Key==Key.F2) { NextBookmark(shift); e.Handled=true; }
        else if(e.Key==Key.Escape)
        {
            if(CompletionCount>0) CancelCompletion(); else { IsSearchOpen=false; IsGoToOpen=false; }
            e.Handled=true;
        }
    }
    private void OnEditorWheel(object? sender,PointerWheelEventArgs e)
    {
        if((e.KeyModifiers&(KeyModifiers.Control|KeyModifiers.Meta))==0) return;
        Zoom=Math.Clamp(Zoom+Math.Sign(e.Delta.Y)*.1,.5,3); e.Handled=true;
    }
    private void OnTextEntering(object? sender,TextInputEventArgs e)
    {
        if(EffectiveReadOnly||e.Text is null||e.Text.Length!=1) return;
        char typed=e.Text[0]; int offset=Editor.CaretOffset;
        if(CompletionCount>0&&!CompletionRequest.IsIdentifier(typed)) AcceptCompletion();
        if(!EnableAutoPairs) return;
        var pair=_autoPairs.FirstOrDefault(p=>!p.Anchor.IsDeleted&&p.Anchor.Offset==offset&&p.Closing==typed);
        if(pair.Anchor is not null && offset<Editor.Document.TextLength && Editor.Document.GetCharAt(offset)==typed)
        {
            _autoPairs.Remove(pair); Editor.CaretOffset=offset+1; e.Handled=true; return;
        }
        char closing=typed switch { '('=>')','['=>']','{'=>'}','"'=>'"','\''=>'\'',_=>'\0' };
        if(closing=='\0') return;
        if(offset>0 && (Editor.Document.GetCharAt(offset-1)=='\\'||((typed=='"'||typed=='\'')&&CompletionRequest.IsIdentifier(Editor.Document.GetCharAt(offset-1))))) return;
        if(Editor.SelectionLength==0&&offset<Editor.Document.TextLength)
        {
            char next=Editor.Document.GetCharAt(offset);
            if(!char.IsWhiteSpace(next)&&next is not (')' or ']' or '}' or ';' or ',')) return;
        }
        var range=SelectionRange; string selected=Editor.SelectedText;
        Session!.ApplyEdits([new(range,typed+selected+closing)]);
        var anchor=Editor.Document.CreateAnchor(range.Offset+selected.Length+1); anchor.MovementType=AnchorMovementType.AfterInsertion;
        _autoPairs.Add((anchor,closing)); Editor.Select(range.Offset+1,selected.Length); Editor.CaretOffset=range.Offset+1; e.Handled=true;
    }
    private async void OnTextEntered(object? sender,TextInputEventArgs e) { if(e.Text==".") await ShowCompletionAsync(); }
    private ContextMenu CreateContextMenu()
    {
        MenuItem Item(string label,EditorCommand command)=>new() { Header=label,Command=command };
        return new ContextMenu { ItemsSource=new object[] {
            Item("Undo",UndoCommand),Item("Redo",RedoCommand),new Separator(),
            Item("Cut",new(()=>Editor.Cut(),()=>!EffectiveReadOnly)),Item("Copy",new(()=>Editor.Copy())),Item("Paste",new(()=>Editor.Paste(),()=>!EffectiveReadOnly)),
            Item("Select all",new(()=>Editor.SelectAll())),new Separator(),Item("Find / replace",FindCommand),Item("Go to line",GoToCommand),
            Item("Toggle line comment",CommentCommand),Item("Duplicate line / selection",DuplicateCommand),Item("Trim trailing whitespace",TrimCommand),
            Item("Toggle bookmark",BookmarkCommand),Item("Fold all",new(()=>FoldAll())),Item("Unfold all",new(()=>FoldAll(false))) } };
    }
}
