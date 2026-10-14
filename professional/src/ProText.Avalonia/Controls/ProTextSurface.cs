using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
namespace ProText.Avalonia.Controls;

/// <summary>Native editing surface with contiguous typing undo groups. Uses the standard AvaloniaEdit templates.</summary>
public sealed class ProTextSurface : TextEditor
{
    public ProTextSurface() : base(new GroupedTextArea()) { }
    protected override Type StyleKeyOverride=>typeof(TextEditor);
}
internal sealed class GroupedTextArea : TextArea
{
    private readonly object _typingGroup=new();
    private TextDocument? _lastDocument;
    private int _lastCaret=-1;
    private long _lastInput;
    private bool _hasTypingGroup;
    protected override Type StyleKeyOverride=>typeof(TextArea);
    protected override void OnTextInput(TextInputEventArgs e)
    {
        var document=Document;
        if(e.Handled||document is null||IsReadOnly||string.IsNullOrEmpty(e.Text)) { base.OnTextInput(e); return; }
        var stack=document.UndoStack; long now=Environment.TickCount64;
        bool ordinary=!e.Text.Contains('\r')&&!e.Text.Contains('\n');
        bool continueGroup=ordinary&&Selection.IsEmpty&&_hasTypingGroup&&ReferenceEquals(_lastDocument,document)&&
            _lastCaret==Caret.Offset&&now-_lastInput<1500&&ReferenceEquals(stack.LastGroupDescriptor,_typingGroup)&&!stack.IsOriginalFile;
        if(continueGroup) stack.StartContinuedUndoGroup(_typingGroup); else stack.StartUndoGroup(_typingGroup);
        try { base.OnTextInput(e); }
        finally
        {
            stack.EndUndoGroup();
            _lastDocument=Document; _lastCaret=Caret.Offset; _lastInput=now;
            _hasTypingGroup=ordinary&&ReferenceEquals(Document,document);
        }
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if((e.KeyModifiers&(KeyModifiers.Control|KeyModifiers.Meta|KeyModifiers.Alt))!=0||
            e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown or Key.Back or Key.Delete or Key.Enter or Key.Escape)
            _hasTypingGroup=false;
        base.OnKeyDown(e);
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e) { _hasTypingGroup=false; base.OnPointerPressed(e); }
    protected override void OnLostFocus(RoutedEventArgs e) { _hasTypingGroup=false; base.OnLostFocus(e); }
}
