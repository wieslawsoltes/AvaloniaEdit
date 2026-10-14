using Avalonia;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Threading;
using Scribe.Core;
namespace Scribe.Avalonia;
public sealed partial class EditorViewport
{
    private bool _dragging,_box;
    private int _dragAnchor,_boxFirstLine;
    private double _boxX;
    private Point _lastPointer;
    private Selection[] _dragBase=[];
    private double? _desiredX;
    private readonly DispatcherTimer _dragTimer;
    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);if(e.Handled||string.IsNullOrEmpty(e.Text))return;
        _desiredX=null;_preedit=null;Owner.TypeText(e.Text);e.Handled=true;
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);if(e.Handled)return;
        if(e.Key is not (Key.Up or Key.Down or Key.PageUp or Key.PageDown))_desiredX=null;
        Owner.HandleKey(e);
    }
    protected override void OnGotFocus(FocusChangedEventArgs e){base.OnGotFocus(e);RefreshInputState();}
    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);_preedit=null;
        // Completion items are part of the current editing interaction, not unrelated focus targets.
        if(!Owner.HasCompletionFocus(e.NewFocusedElement))Owner.CloseCompletion();
        InvalidateVisual();
    }
    public void MoveVertical(int rows,bool extend=false)
    {
        if(!CheckDocument())return;
        _desiredX??=CaretRectangle.X+OffsetX;
        var selections=Owner.Session.Selections.Select(s=>
        {
            var rect=GetCaretRectangle(s.Caret);int target=HitTestOffset(new Point(_desiredX.Value-OffsetX,rect.Y+LineHeight*.5+rows*LineHeight));
            return new Selection(extend?s.Anchor:target,target);
        }).ToArray();
        Owner.Session.Select(selections);
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);if(!CheckDocument())return;
        var p=e.GetCurrentPoint(this);if(p.Properties.IsRightButtonPressed)return;
        if(!p.Properties.IsLeftButtonPressed&&e.Pointer.Type!=PointerType.Touch)return;
        Focus();Owner.CloseCompletion();Owner.Snippet?.Cancel();_desiredX=null;var position=e.GetPosition(this);int offset=HitTestOffset(position);
        int line=Owner.Session.Document.Snapshot.LineAt(offset);
        if(position.X<GutterWidth&&position.X>GutterWidth-18&&Owner.Folds.Any(f=>f.FirstLine==line)){Owner.ToggleFold(line);e.Handled=true;return;}
        if(e.ClickCount>=3||position.X<GutterWidth-18)
        {
            var info=Owner.Session.Document.Snapshot.GetLine(line);Owner.Session.Select(new Selection(info.Start,info.Start+info.TotalLength));
        }
        else
        {
            bool alt=e.KeyModifiers.HasFlag(KeyModifiers.Alt),shift=e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            _box=alt&&shift;_boxFirstLine=line;_boxX=position.X+OffsetX;
            _dragBase=alt&&!shift?Owner.Session.Selections.ToArray():[];
            _dragAnchor=shift&&!alt?Owner.Session.Primary.Anchor:offset;
            Owner.Session.Select(_dragBase.Append(new Selection(_dragAnchor,offset)).ToArray());
            _dragging=true;_lastPointer=position;e.Pointer.Capture(this);_dragTimer.Start();
        }
        Owner.RevealCaret();e.Handled=true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);if(!_dragging)return;_lastPointer=e.GetPosition(this);ExtendDrag();e.Handled=true;
    }
    private void ExtendDrag()
    {
        int target=HitTestOffset(_lastPointer);
        if(_box)
        {
            var b=Owner.Session.Document.Snapshot;int last=b.LineAt(target),first=Math.Min(_boxFirstLine,last),end=Math.Max(_boxFirstLine,last);var ranges=new List<Selection>();
            for(int i=first;i<=end&&ranges.Count<4096;i++)
            {
                var line=b.GetLine(i);var shaped=Shape(i);
                int a=shaped.ToRaw(shaped.Layout.HitTestPoint(new Point(Math.Max(0,_boxX-GutterWidth),0)).TextPosition);
                int z=shaped.ToRaw(shaped.Layout.HitTestPoint(new Point(Math.Max(0,_lastPointer.X-GutterWidth+OffsetX),0)).TextPosition);
                ranges.Add(new(line.Start+a,line.Start+z));
            }
            Owner.Session.Select(ranges.ToArray());
        }
        else Owner.Session.Select(_dragBase.Append(new Selection(_dragAnchor,target)).ToArray());
    }
    private void AutoScrollSelection()
    {
        if(!_dragging)return;
        double delta=_lastPointer.Y<0?-LineHeight:_lastPointer.Y>Bounds.Height?LineHeight:0;
        if(delta!=0){ScrollTo(OffsetX,OffsetY+delta);ExtendDrag();}
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);if(!_dragging)return;_dragging=false;_dragTimer.Stop();e.Pointer.Capture(null);e.Handled=true;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e){base.OnPointerCaptureLost(e);_dragging=false;_dragTimer.Stop();}
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if((e.KeyModifiers&(KeyModifiers.Control|KeyModifiers.Meta))!=0)Owner.Zoom=Math.Clamp(Owner.Zoom+Math.Sign(e.Delta.Y)*.1,.5,3);
        else if(e.KeyModifiers.HasFlag(KeyModifiers.Shift))ScrollTo(OffsetX-e.Delta.Y*40,OffsetY);
        else ScrollTo(OffsetX-e.Delta.X*40,OffsetY-e.Delta.Y*LineHeight*3);
        e.Handled=true;
    }
}
internal sealed class EditorInputClient : TextInputMethodClient,IDisposable
{
    private readonly EditorViewport _view;
    private int _start;
    private string _surrounding="";
    private bool _refreshing;
    public EditorInputClient(EditorViewport view)=>_view=view;
    public override Visual TextViewVisual=>_view;
    public override Rect CursorRectangle=>_view.CaretRectangle;
    public override bool SupportsPreedit=>true;
    public override bool SupportsSurroundingText=>true;
    public override string SurroundingText=>_surrounding;
    public override TextSelection Selection
    {
        get
        {
            var selection=_view.Owner.Session?.Primary??Core.Selection.At(0);
            return new(Math.Clamp(selection.Start-_start,0,_surrounding.Length),Math.Clamp(selection.End-_start,0,_surrounding.Length));
        }
        set
        {
            var session=_view.Owner.Session;if(session is null)return;
            int a=EditSession.Snap(session.Document.Snapshot,_start+Math.Clamp(value.Start,0,_surrounding.Length));
            int b=EditSession.Snap(session.Document.Snapshot,_start+Math.Clamp(value.End,0,_surrounding.Length));
            if(session.Primary.Start==a&&session.Primary.End==b)return;
            session.Select(new Core.Selection(a,b));_view.Owner.RevealCaret();
        }
    }
    public override void SetPreeditText(string? text)=>_view.SetPreedit(text);
    public void Refresh()
    {
        if(_refreshing||_view.Owner.Session is null)return;_refreshing=true;
        try
        {
            var session=_view.Owner.Session;var b=session.Document.Snapshot;var s=session.Primary;
            int start=Math.Max(0,s.Start-512),end=Math.Min(b.Length,Math.Max(s.End,s.Caret)+512);
            if(end-start>4096){start=Math.Max(0,s.Caret-2048);end=Math.Min(b.Length,start+4096);}
            _start=EditSession.Snap(b,start);end=EditSession.Snap(b,end);_surrounding=b.GetText(_start,Math.Max(0,end-_start));
            RaiseSurroundingTextChanged();RaiseSelectionChanged();RaiseCursorRectangleChanged();
        }
        finally{_refreshing=false;}
    }
    public void Dispose(){_surrounding="";}
}
