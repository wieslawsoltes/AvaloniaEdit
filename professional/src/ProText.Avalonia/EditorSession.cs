using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AvaloniaEdit.Document;
using ProText.Core;
namespace ProText.Avalonia;

/// <summary>A UI-thread-owned document, independent of any particular view. Dispose after all views detach.</summary>
public sealed class EditorSession : INotifyPropertyChanged, IDisposable
{
    private string _fileName, _language;
    private bool _readOnly, _disposed;
    private IReadOnlyList<EditorDiagnostic> _diagnostics = [];
    public TextDocument Document { get; }
    public long Revision { get; private set; }
    public string Text => Document.Text;
    public int Length => Document.TextLength;
    public int LineCount => Document.LineCount;
    public bool IsDirty => !Document.UndoStack.IsOriginalFile;
    public string FileName { get => _fileName; set { if(_fileName != value) { _fileName=value; Notify(); } } }
    public string Language { get => _language; set { if(_language != value) { _language=value; Notify(); } } }
    public bool IsReadOnly { get => _readOnly; set { if(_readOnly != value) { _readOnly=value; Notify(); } } }
    public string EncodingName { get; set; } = "UTF-8";
    public LineEnding LineEnding
    {
        get
        {
            for(int i=1; i<=Math.Min(Document.LineCount,64); i++)
            {
                var line=Document.GetLineByNumber(i);
                if(line.DelimiterLength==2) return LineEnding.CrLf;
                if(line.DelimiterLength==1) return Document.GetCharAt(line.EndOffset)=='\r' ? LineEnding.Cr : LineEnding.Lf;
            }
            return LineEnding.Lf;
        }
    }
    public ObservableCollection<EditorBookmark> Bookmarks { get; } = [];
    public IReadOnlyList<EditorDiagnostic> Diagnostics => _diagnostics;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Changed;
    public event EventHandler? DiagnosticsChanged;
    public EditorSession(string text="", string fileName="Untitled.txt", string language="Plain Text")
    {
        _fileName=fileName; _language=language;
        Document=new TextDocument(text); Document.UndoStack.MarkAsOriginalFile();
        Document.TextChanged+=OnTextChanged;
        Document.UndoStack.PropertyChanged+=OnUndoChanged;
    }
    public void ApplyEdits(IEnumerable<TextEdit> edits, long? expectedRevision=null)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(IsReadOnly) throw new InvalidOperationException("The document is read-only.");
        if(expectedRevision is not null && expectedRevision != Revision) throw new InvalidOperationException("The document changed before the operation completed.");
        var ordered=EditBatch.Validate(Text,edits);
        using(Document.RunUpdate())
            for(int i=ordered.Count-1;i>=0;i--) Document.Replace(ordered[i].Range.Offset,ordered[i].Range.Length,ordered[i].NewText);
    }
    public void ReplaceAllText(string text) => ApplyEdits([new(new(0,Length),text)]);
    /// <summary>Explicit loading operation. Unlike editing, this is allowed for a read-only document.</summary>
    public void Reset(string text, bool markSaved=true)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        Document.Text=text; Document.UndoStack.ClearAll(); Bookmarks.Clear();
        if(markSaved) MarkSaved();
    }
    public bool MarkSaved(long? expectedRevision=null)
    {
        if(expectedRevision is not null && expectedRevision != Revision) return false;
        Document.UndoStack.MarkAsOriginalFile(); Notify(nameof(IsDirty)); return true;
    }
    public bool SetDiagnostics(long revision,IEnumerable<EditorDiagnostic> diagnostics)
    {
        if(_disposed || revision!=Revision) return false;
        var items=diagnostics.ToArray();
        if(items.Any(d=>d.Range.End>Length)) throw new ArgumentOutOfRangeException(nameof(diagnostics));
        _diagnostics=items.OrderBy(d=>d.Range.Offset).Take(10000).ToArray();
        DiagnosticsChanged?.Invoke(this,EventArgs.Empty); return true;
    }
    public void ToggleBookmark(int line)
    {
        line=Math.Clamp(line,1,LineCount);
        var existing=Bookmarks.FirstOrDefault(b=>b.Line==line);
        if(existing is not null) { Bookmarks.Remove(existing); return; }
        var anchor=Document.CreateAnchor(Document.GetLineByNumber(line).Offset);
        anchor.SurviveDeletion=true; anchor.MovementType=AnchorMovementType.AfterInsertion;
        Bookmarks.Add(new EditorBookmark(Document,anchor));
    }
    public int FindBookmark(int line,bool backwards=false)
    {
        var lines=Bookmarks.Select(b=>b.Line).Distinct().Order().ToArray();
        if(lines.Length==0) return line;
        return backwards ? lines.LastOrDefault(n=>n<line,lines[^1]) : lines.FirstOrDefault(n=>n>line,lines[0]);
    }
    public void ConvertLineEndings(LineEnding ending)=>ReplaceAllText(TextOperations.NormalizeLineEndings(Text,ending));
    private void OnTextChanged(object? sender,EventArgs e)
    {
        Revision++; _diagnostics=[];
        foreach(var bookmark in Bookmarks) bookmark.NotifyLocation();
        Notify(nameof(Revision)); Notify(nameof(LineCount)); Notify(nameof(Length)); Notify(nameof(IsDirty)); Notify(nameof(LineEnding));
        Changed?.Invoke(this,EventArgs.Empty); DiagnosticsChanged?.Invoke(this,EventArgs.Empty);
    }
    private void OnUndoChanged(object? sender,PropertyChangedEventArgs e)=>Notify(nameof(IsDirty));
    private void Notify([CallerMemberName] string? property=null)=>PropertyChanged?.Invoke(this,new(property));
    public void Dispose()
    {
        if(_disposed) return; _disposed=true;
        Document.TextChanged-=OnTextChanged; Document.UndoStack.PropertyChanged-=OnUndoChanged;
        Changed=null; DiagnosticsChanged=null; PropertyChanged=null;
    }
}
public sealed class EditorBookmark(TextDocument document,TextAnchor anchor) : INotifyPropertyChanged
{
    public int Offset=>anchor.Offset;
    public int Line=>document.GetLineByOffset(Offset).LineNumber;
    public string Label=>$"Line {Line}";
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void NotifyLocation() { PropertyChanged?.Invoke(this,new(nameof(Line))); PropertyChanged?.Invoke(this,new(nameof(Label))); }
}
