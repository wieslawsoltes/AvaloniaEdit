using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Scribe.Core;
namespace Scribe.Avalonia;
public partial class ScribeEditor
{
    private readonly List<(TextAnchor Anchor,char Closing)> _pairs=[];
    public SnippetSession? Snippet {get;private set;}
    public void InsertSnippet(string template)
    {
        if(!CanEdit) return; Snippet?.Cancel(); Snippet=new SnippetSession(Session,template); RevealCaret(); FocusEditor();
    }
    internal void TypeText(string text)
    {
        if(!CanEdit||string.IsNullOrEmpty(text)) return;
        if(text.Length==1&&CompletionCount>0&&!EditSession.IsWord(text[0])) AcceptCompletion(); else CloseCompletion();
        var selections=Session.Selections.ToArray();
        if(AutoPairs&&text.Length==1)
        {
            char typed=text[0];
            if(selections.All(s=>s.Length==0&&_pairs.Any(p=>p.Anchor.Offset==s.Caret&&p.Closing==typed)&&s.Caret<Document!.Length&&Document.Snapshot[s.Caret]==typed))
            {
                foreach(var s in selections) _pairs.RemoveAll(p=>p.Anchor.Offset==s.Caret&&p.Closing==typed);
                Session.Select(selections.Select(s=>Selection.At(s.Caret+1)).ToArray()); RevealCaret(); return;
            }
            char close=typed switch {'('=>')','['=>']','{'=>'}','"'=>'"','\''=>'\'',_=>'\0'};
            bool canPair=close!='\0'&&selections.All(s=>s.Length>0||s.Caret==Document!.Length||char.IsWhiteSpace(Document.Snapshot[s.Caret])||Document.Snapshot[s.Caret] is ')' or ']' or '}' or ';' or ',');
            if(canPair)
            {
                var edits=new List<TextEdit>(); var after=new List<Selection>(); var positions=new List<int>(); int delta=0;
                foreach(var s in selections)
                {
                    string selected=Document!.Snapshot.GetText(s.Start,s.Length); edits.Add(new(s.Start,s.Length,typed+selected+close));
                    int start=s.Start+delta+1; after.Add(new(start,start+s.Length)); positions.Add(start+s.Length); delta+=2;
                }
                Document!.Apply(edits,Session.Id,selections,after);
                foreach(int p in positions) _pairs.Add((Document.CreateAnchor(p,Affinity.After),close));
                RevealCaret(); return;
            }
        }
        Session.Insert(text,true); RevealCaret();
    }
    public async Task CopyAsync(bool cut=false)
    {
        try
        {
            if(cut&&!CanEdit) return; var clipboard=TopLevel.GetTopLevel(this)?.Clipboard;
            if(clipboard is null) {Message="Clipboard is not available in this host."; return;}
            var session=Session; long revision=session.Document.Revision;
            var originalSelection=session.Selections.ToArray();var cutSelection=originalSelection;string text=session.SelectedText();
            if(originalSelection.All(s=>s.Length==0))
            {
                var line=session.Document.Snapshot.GetLine(session.Document.Snapshot.LineAt(session.Primary.Caret));
                text=session.Document.Snapshot.GetText(line.Start,line.TotalLength);
                if(cut) cutSelection=[new(line.Start,line.Start+line.TotalLength)];
            }
            await clipboard.SetValueAsync(DataFormat.Text,text);
            if(cut)
            {
                if(session!=Session||revision!=session.Document.Revision||!originalSelection.SequenceEqual(session.Selections)||!CanEdit)
                {Message="Text copied, but cut cancelled because the document or selection changed.";return;}
                session.Select(cutSelection);session.Insert("");RevealCaret();
            }
        }
        catch(Exception e) {Message="Clipboard operation failed: "+e.Message;}
    }
    public async Task PasteAsync()
    {
        try
        {
            if(!CanEdit) return; var clipboard=TopLevel.GetTopLevel(this)?.Clipboard;
            if(clipboard is null) {Message="Clipboard is not available in this host."; return;}
            var session=Session; long revision=session.Document.Revision; var selections=session.Selections.ToArray();
            string? text=await clipboard.TryGetTextAsync();
            if(text is null) return;
            if(session!=Session||revision!=session.Document.Revision||!selections.SequenceEqual(session.Selections)||!CanEdit)
            {Message="Paste cancelled because the document or selection changed."; return;}
            session.Insert(text); RevealCaret();
        }
        catch(Exception e) {Message="Paste failed: "+e.Message;}
    }
    internal async void HandleKey(KeyEventArgs e)
    {
        try
        {
            bool primary=(e.KeyModifiers&(KeyModifiers.Control|KeyModifiers.Meta))!=0&&(e.KeyModifiers&KeyModifiers.Alt)==0;
            bool shift=e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            if(HandleCompletionKey(e)) return;
            if(primary)
            {
                switch(e.Key)
                {
                    case Key.A: Session.SelectAll(); break;
                    case Key.C: e.Handled=true; await CopyAsync(); return;
                    case Key.X: e.Handled=true; await CopyAsync(true); return;
                    case Key.V: e.Handled=true; await PasteAsync(); return;
                    case Key.Z: if(shift) RedoCommand.Execute(null); else UndoCommand.Execute(null); break;
                    case Key.Y: RedoCommand.Execute(null); break;
                    case Key.F: case Key.H: OpenSearch(); e.Handled=true; return;
                    case Key.D: if(shift) Session.Duplicate(); else Session.SelectNextOccurrence(); break;
                    case Key.Space: e.Handled=true; await ShowCompletionAsync(); return;
                    case Key.OemQuestion: Session.ToggleComment(Language=="Python"?"#":"//"); break;
                    case Key.OemPlus: case Key.Add: Zoom=Math.Min(3,Zoom+.1); break;
                    case Key.OemMinus: case Key.Subtract: Zoom=Math.Max(.5,Zoom-.1); break;
                    case Key.D0: Zoom=1; break;
                    case Key.F2: Session.ToggleBookmark(); break;
                    case Key.Home: Session.MoveHomeEnd(false,true,shift); break;
                    case Key.End: Session.MoveHomeEnd(true,true,shift); break;
                    case Key.Left: Session.MoveHorizontal(-1,shift,true); break;
                    case Key.Right: Session.MoveHorizontal(1,shift,true); break;
                    case Key.Back: Session.Delete(true,true); break;
                    case Key.Delete: Session.Delete(false,true); break;
                    default: return;
                }
                e.Handled=true; RevealCaret(); return;
            }
            switch(e.Key)
            {
                case Key.Left: Session.MoveHorizontal(-1,shift); Snippet?.Cancel(); break;
                case Key.Right: Session.MoveHorizontal(1,shift); Snippet?.Cancel(); break;
                case Key.Up: Viewport.MoveVertical(-1,shift); break;
                case Key.Down: Viewport.MoveVertical(1,shift); break;
                case Key.PageUp: Viewport.MoveVertical(-Math.Max(1,(int)(Viewport.Bounds.Height/Viewport.LineHeight)-1),shift); break;
                case Key.PageDown: Viewport.MoveVertical(Math.Max(1,(int)(Viewport.Bounds.Height/Viewport.LineHeight)-1),shift); break;
                case Key.Home: Session.MoveHomeEnd(false,false,shift); break;
                case Key.End: Session.MoveHomeEnd(true,false,shift); break;
                case Key.Back: Session.Delete(true); break;
                case Key.Delete: Session.Delete(); break;
                case Key.Enter: Session.Enter(); break;
                case Key.Tab:
                    if(Snippet?.IsActive==true) Snippet.Next(shift);
                    else if(shift||Session.Selections.Any(s=>s.Length>0)) Session.Indent(shift);
                    else Session.Insert(UseTabs?"\t":new string(' ',TabSize));
                    break;
                case Key.F3: Search.Navigate(shift); break;
                case Key.F2: Session.NextBookmark(shift); break;
                case Key.Escape: Snippet?.Cancel(); IsSearchOpen=false; Session.Select(Selection.At(Session.Primary.Caret)); break;
                default:return;
            }
            e.Handled=true; RevealCaret();
        }
        catch(Exception error) {Message=error.Message; e.Handled=true;}
    }
}
