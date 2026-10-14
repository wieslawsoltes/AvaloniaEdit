using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.Utilities;
using Scribe.Core;
namespace Scribe.Avalonia;

/// <summary>Original shaped-text viewport. Owns no document storage and derives directly from Control.
/// TextLayout is used only as Avalonia's glyph shaping/hit-testing primitive.</summary>
public sealed partial class EditorViewport : Control,IDisposable
{
    private sealed class ShapedLine(string raw,string display,int[] rawToDisplay,int[] displayToRaw,TextLayout layout,TextLayout number) : IDisposable
    {
        public string Raw=raw,Display=display;
        public int[] RawToDisplay=rawToDisplay,DisplayToRaw=displayToRaw;
        public TextLayout Layout=layout,Number=number;
        public int Cost=>Raw.Length+Display.Length;
        public int ToVisual(int offset)=>RawToDisplay[Math.Clamp(offset,0,Raw.Length)];
        public int ToRaw(int offset)=>DisplayToRaw[Math.Clamp(offset,0,DisplayToRaw.Length-1)];
        public void Dispose(){Layout.Dispose();Number.Dispose();}
    }
    public ScribeEditor Owner {get;}
    private VisualRows _rows=new(1);
    private readonly Dictionary<int,ShapedLine> _layouts=[];
    private readonly Queue<int> _cacheOrder=[];
    private int _cacheCost;
    private long _revision=-1;
    private Document? _document;
    private double _extentWidth,_lastWidth;
    private readonly DispatcherTimer _blinkTimer;
    private bool _blink=true,_disposed;
    internal readonly EditorInputClient InputClient;
    private string? _preedit;
    public double LineHeight=>Math.Max(12,Owner.FontSize*Owner.Zoom*1.5);
    public double GutterWidth=>Owner.ShowLineNumbers?Math.Max(54,24+Math.Max(2,(Owner.Document?.LineCount??1).ToString(CultureInfo.InvariantCulture).Length)*Owner.FontSize*Owner.Zoom*.66):20;
    public double TextWidth=>Math.Max(20,Bounds.Width-GutterWidth-8);
    public double ExtentHeight=>Math.Max(LineHeight,_rows.Total*LineHeight+LineHeight);
    public double ExtentWidth=>_extentWidth;
    public double OffsetX {get;private set;}
    public double OffsetY {get;private set;}
    public int RealizedLineCount {get;private set;}
    public int CachedLayoutCount=>_layouts.Count;
    public int FirstVisibleLine=>_rows.Find((int)(OffsetY/LineHeight));
    public long ShapedLineCount {get;private set;}
    public Rect CaretRectangle=>GetCaretRectangle(Owner.Session?.Primary.Caret??0);
    static EditorViewport()
    {
        TextInputMethodClientRequestedEvent.AddClassHandler<EditorViewport>((view,args)=>{args.Client=view.Owner.CanEdit?view.InputClient:null;});
    }
    internal EditorViewport(ScribeEditor owner)
    {
        Owner=owner; Focusable=true; ClipToBounds=true; Cursor=new Cursor(StandardCursorType.Ibeam);
        InputClient=new(this);
        _blinkTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(530)};
        _blinkTimer.Tick+=(_,_)=>{if(IsFocused){_blink=!_blink;InvalidateVisual();}};
        _dragTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(35)};
        _dragTimer.Tick+=(_,_)=>AutoScrollSelection();
    }
    public void ResetLayout()
    {
        ClearCache(); _rows=new(Math.Max(1,Owner.Session?.Document.LineCount??1)); _extentWidth=0; ApplyFoldWeights();
        _revision=Owner.Session?.Document.Revision??-1; _document=Owner.Session?.Document;
        InvalidateVisual();
    }
    internal void DocumentChanged(DocumentChange change)
    {
        ClearCache();
        if(_rows.Count!=change.After.LineCount||Owner.CollapsedLines.Count>0) {_rows=new(change.After.LineCount);ApplyFoldWeights();}
        else if(change.Edits.Count>0)
        {
            int first=change.Before.LineAt(change.Edits[0].Start),last=change.Before.LineAt(change.Edits[^1].End);
            for(int i=first;i<=Math.Min(last,_rows.Count-1);i++) _rows.Set(i,1);
        }
        _revision=Owner.Session.Document.Revision; _document=Owner.Session.Document; _blink=true; InvalidateVisual();
    }
    internal void ApplyFolds(){ClearCache();_rows=new(Math.Max(1,Owner.Session?.Document.LineCount??1));ApplyFoldWeights();InvalidateVisual();}
    private void ApplyFoldWeights()
    {
        foreach(var f in Owner.Folds) if(Owner.CollapsedLines.Contains(f.FirstLine)) for(int i=f.FirstLine+1;i<=Math.Min(f.LastLine,_rows.Count-1);i++) _rows.Set(i,0);
    }
    private bool CheckDocument()
    {
        if(Owner.Session is null||_disposed) return false;
        var document=Owner.Session.Document;
        if(_document!=document||_rows.Count!=document.LineCount) ResetLayout();
        else if(_revision!=document.Revision){ClearCache();_revision=document.Revision;}
        return true;
    }
    private void ClearCache(){foreach(var line in _layouts.Values)line.Dispose();_layouts.Clear();_cacheOrder.Clear();_cacheCost=0;}
    private ShapedLine Shape(int index)
    {
        if(_layouts.TryGetValue(index,out var cached)) return cached;
        var buffer=Owner.Session.Document.Snapshot; var line=buffer.GetLine(index);
        int count=Math.Min(65536,line.Length);
        if(count<line.Length&&count>0&&char.IsHighSurrogate(buffer[line.Start+count-1])) count--;
        string raw=buffer.GetText(line.Start,count); var expanded=new StringBuilder(raw.Length); var map=new int[raw.Length+1]; var reverse=new List<int>();
        int column=0;
        for(int i=0;i<raw.Length;i++)
        {
            map[i]=expanded.Length;
            if(raw[i]=='\t') {int spaces=Owner.TabSize-column%Owner.TabSize;expanded.Append(' ',spaces);for(int k=0;k<spaces;k++)reverse.Add(i);column+=spaces;}
            else {expanded.Append(raw[i]);reverse.Add(i);column++;}
        }
        map[raw.Length]=expanded.Length;reverse.Add(raw.Length);
        string display=expanded.ToString(); var face=new Typeface(Owner.FontFamily); double size=Math.Max(8,Owner.FontSize*Owner.Zoom);
        var overrides=new List<ValueSpan<TextRunProperties>>();
        if(Owner.Language!="Plain Text") foreach(var token in LexicalLanguage.Tokenize(raw))
        {
            IBrush brush=TokenBrush(token.Kind); int start=map[token.Start],length=map[token.Start+token.Length]-start;
            overrides.Add(new(start,length,new GenericTextRunProperties(face,size,foregroundBrush:brush)));
        }
        var layout=new TextLayout(display,face,size,Owner.Foreground??Brushes.Black,textWrapping:Owner.WordWrap?TextWrapping.Wrap:TextWrapping.NoWrap,
            maxWidth:Owner.WordWrap?TextWidth:double.PositiveInfinity,lineHeight:LineHeight,textStyleOverrides:overrides);
        var number=new TextLayout((index+1).ToString(CultureInfo.InvariantCulture),face,size*.85,Owner.GutterBrush,lineHeight:LineHeight);
        var shaped=new ShapedLine(raw,display,map,reverse.ToArray(),layout,number);
        while(_cacheOrder.Count>0&&(_layouts.Count>=128||_cacheCost+shaped.Cost>262144))
        {
            int old=_cacheOrder.Dequeue(); if(_layouts.Remove(old,out var item)){_cacheCost-=item.Cost;item.Dispose();}
        }
        _layouts[index]=shaped;_cacheOrder.Enqueue(index);_cacheCost+=shaped.Cost;ShapedLineCount++;
        if(_rows[index]>0) _rows.Set(index,Math.Max(1,(int)Math.Ceiling(layout.Height/LineHeight-.001)));
        _extentWidth=Math.Max(_extentWidth,layout.WidthIncludingTrailingWhitespace+16);
        return shaped;
    }
    private IBrush TokenBrush(TokenKind kind)=>Brush.Parse((kind,Owner.IsDark) switch
    {
        (TokenKind.Keyword,true)=>"#CFABFF",(TokenKind.Keyword,false)=>"#733BA8",
        (TokenKind.Number,true)=>"#F4BF85",(TokenKind.Number,false)=>"#955023",
        (TokenKind.String,true)=>"#B4D896",(TokenKind.String,false)=>"#46782B",
        (TokenKind.Comment,true)=>"#829A83",(TokenKind.Comment,false)=>"#5A7752",
        (TokenKind.Type,true)=>"#7ACBD8",(TokenKind.Type,false)=>"#167389",
        _=>Owner.IsDark?"#DFE6F0":"#24334A"
    });
    protected override Size MeasureOverride(Size availableSize)=>new(double.IsFinite(availableSize.Width)?availableSize.Width:600,double.IsFinite(availableSize.Height)?availableSize.Height:400);
    protected override Size ArrangeOverride(Size finalSize)
    {
        if(Math.Abs(_lastWidth-finalSize.Width)>.5){_lastWidth=finalSize.Width;ResetLayout();}
        var result=base.ArrangeOverride(finalSize);Dispatcher.UIThread.Post(Owner.UpdateView);return result;
    }
    public void ScrollTo(double x,double y)
    {
        if(!double.IsFinite(x)||!double.IsFinite(y))return;
        OffsetX=Owner.WordWrap?0:Math.Clamp(x,0,Math.Max(0,ExtentWidth-TextWidth));
        OffsetY=Math.Clamp(y,0,Math.Max(0,ExtentHeight-Bounds.Height));
        InvalidateVisual();Owner.UpdateView();InputClient.Refresh();
    }
    public Rect GetCaretRectangle(int offset)
    {
        if(!CheckDocument())return new(0,0,1,LineHeight);
        var buffer=Owner.Session.Document.Snapshot;offset=Math.Clamp(offset,0,buffer.Length);
        var line=buffer.GetLine(buffer.LineAt(offset));var shaped=Shape(line.Index);
        var local=shaped.Layout.HitTestTextPosition(shaped.ToVisual(Math.Min(line.Length,offset-line.Start)));
        return new(GutterWidth+local.X-OffsetX,_rows.Prefix(line.Index)*LineHeight+local.Y-OffsetY,1.5,LineHeight);
    }
    public int HitTestOffset(Point point)
    {
        if(!CheckDocument())return 0;
        double absolute=Math.Max(0,point.Y+OffsetY);int index=_rows.Find((int)(absolute/LineHeight));
        var buffer=Owner.Session.Document.Snapshot;var line=buffer.GetLine(index);var shape=Shape(index);
        var hit=shape.Layout.HitTestPoint(new Point(Math.Max(0,point.X-GutterWidth+OffsetX),Math.Max(0,absolute-_rows.Prefix(index)*LineHeight)));
        return EditSession.Snap(buffer,line.Start+shape.ToRaw(hit.TextPosition));
    }
    public void EnsureCaretVisible()
    {
        if(!CheckDocument()||Bounds.Height<=0)return;
        int index=Owner.Session.Document.Snapshot.LineAt(Owner.Session.Primary.Caret);Owner.UnfoldAt(index);
        var rect=GetCaretRectangle(Owner.Session.Primary.Caret);double y=OffsetY,x=OffsetX;
        if(rect.Top<0)y+=rect.Top;else if(rect.Bottom>Bounds.Height)y+=rect.Bottom-Bounds.Height;
        if(rect.Left<GutterWidth)x-=GutterWidth-rect.Left;else if(rect.Right>Bounds.Width-8)x+=rect.Right-(Bounds.Width-8);
        ScrollTo(x,y);_blink=true;
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);if(!CheckDocument()||Bounds.Width<=0||Bounds.Height<=0)return;
        context.FillRectangle(Owner.Background??Brushes.Transparent,new Rect(Bounds.Size));
        var snapshot=Owner.Session.Document.Snapshot;int index=_rows.Find((int)(OffsetY/LineHeight));int realized=0;
        while(index<snapshot.LineCount&&realized<1024)
        {
            var line=snapshot.GetLine(index);var shaped=Shape(index);double y=_rows.Prefix(index)*LineHeight-OffsetY;
            if(y>=Bounds.Height)break;
            if(_rows[index]>0)
            {
                realized++;double height=Math.Max(LineHeight,shaped.Layout.Height);
                if(snapshot.LineAt(Owner.Session.Primary.Caret)==index)context.FillRectangle(Owner.CurrentLineBrush,new Rect(GutterWidth,y,TextWidth,height));
                if(Owner.ShowLineNumbers)shaped.Number.Draw(context,new Point(Math.Max(0,GutterWidth-shaped.Number.Width-18),y));
                if(Owner.Folds.Any(f=>f.FirstLine==index))
                {
                    var pen=new Pen(Owner.GutterBrush,1);double x=GutterWidth-11,cy=y+LineHeight/2;
                    context.DrawLine(pen,new(x-3,cy),new(x+3,cy));if(Owner.CollapsedLines.Contains(index))context.DrawLine(pen,new(x,cy-3),new(x,cy+3));
                }
                if(Owner.Session.Bookmarks.Any(b=>snapshot.LineAt(b.Offset)==index))context.DrawEllipse(Owner.CaretBrush,null,new Point(7,y+LineHeight/2),3,3);
                using(context.PushClip(new Rect(GutterWidth,0,TextWidth,Bounds.Height)))
                {
                    foreach(var selection in Owner.Session.Selections) PaintRange(context,shaped,line.Start,selection.Start,selection.End,y,Owner.SelectionBrush);
                    if(Owner.IsSearchOpen)
                    {
                        var hits=Owner.Search.Results.Matches;int low=0,high=hits.Count;
                        while(low<high){int mid=(low+high)/2;if(hits[mid].End<line.Start)low=mid+1;else high=mid;}
                        for(int h=low;h<hits.Count&&hits[h].Start<=line.End;h++)PaintRange(context,shaped,line.Start,hits[h].Start,hits[h].End,y,Brush.Parse("#5090B5DD"));
                    }
                    shaped.Layout.Draw(context,new Point(GutterWidth-OffsetX,y));
                    if(shaped.Raw.Length<line.Length)
                    {
                        using var warning=new TextLayout("… [line display limit]",new Typeface(Owner.FontFamily),Owner.FontSize,Owner.GutterBrush);
                        warning.Draw(context,new Point(GutterWidth+shaped.Layout.Width-OffsetX,y));
                    }
                    foreach(var d in Owner.Diagnostics)
                    {
                        if(d.Start>line.End)break;if(d.Start+d.Length<line.Start)continue;
                        int first=Math.Clamp(d.Start-line.Start,0,shaped.Raw.Length),last=Math.Clamp(d.Start+d.Length-line.Start,first,shaped.Raw.Length);
                        var pen=new Pen(Brush.Parse(d.IsError?"#F07E85":"#DAB065"),1.2);
                        foreach(var r in shaped.Layout.HitTestTextRange(shaped.ToVisual(first),Math.Max(1,shaped.ToVisual(last)-shaped.ToVisual(first))))
                            context.DrawLine(pen,new(GutterWidth+r.Left-OffsetX,y+r.Bottom-1),new(GutterWidth+r.Right-OffsetX,y+r.Bottom-1));
                    }
                    if(Owner.ShowWhitespace)
                    {
                        int drawn=0;for(int p=0;p<shaped.Raw.Length&&drawn<512;p++)if(shaped.Raw[p] is ' ' or '\t')
                        {var r=shaped.Layout.HitTestTextPosition(shaped.ToVisual(p));context.DrawEllipse(Owner.GutterBrush,null,new Point(GutterWidth+r.X-OffsetX+2,y+r.Y+LineHeight*.65),.7,.7);drawn++;}
                    }
                    if(IsFocused&&_blink)foreach(var selection in Owner.Session.Selections)if(snapshot.LineAt(selection.Caret)==index)
                    {var caret=GetCaretRectangle(selection.Caret);context.FillRectangle(Owner.CaretBrush,caret);}
                }
            }
            int next=_rows.Prefix(index+1);if(next>=_rows.Total)break;int nextIndex=_rows.Find(next);if(nextIndex<=index)break;index=nextIndex;
        }
        RealizedLineCount=realized;
        if(IsFocused&&!string.IsNullOrEmpty(_preedit))
        {
            var caret=CaretRectangle;using var preedit=new TextLayout(_preedit,new Typeface(Owner.FontFamily),Owner.FontSize*Owner.Zoom,Owner.Foreground,lineHeight:LineHeight);
            var rect=new Rect(caret.X,caret.Y,Math.Max(8,preedit.Width),LineHeight);context.FillRectangle(Owner.Background,rect);preedit.Draw(context,rect.Position);
            context.DrawLine(new Pen(Owner.CaretBrush,1),rect.BottomLeft,rect.BottomRight);
        }
        Dispatcher.UIThread.Post(Owner.UpdateView,DispatcherPriority.Background);
    }
    private void PaintRange(DrawingContext context,ShapedLine line,int start,int selectionStart,int selectionEnd,double y,IBrush? brush)
    {
        if(selectionEnd<=start||selectionStart>start+line.Raw.Length)return;
        int first=Math.Clamp(selectionStart-start,0,line.Raw.Length),last=Math.Clamp(selectionEnd-start,first,line.Raw.Length);
        foreach(var r in line.Layout.HitTestTextRange(line.ToVisual(first),line.ToVisual(last)-line.ToVisual(first)))
            context.FillRectangle(brush,new Rect(GutterWidth+r.X-OffsetX,y+r.Y,Math.Max(2,r.Width),Math.Max(LineHeight,r.Height)));
        if(selectionEnd>start+line.Raw.Length)
        {var r=line.Layout.HitTestTextPosition(line.Display.Length);context.FillRectangle(brush,new Rect(GutterWidth+r.X-OffsetX,y+r.Y,5,LineHeight));}
    }
    internal void RefreshInputState(){_blink=true;InputClient.Refresh();InvalidateVisual();}
    internal void SetPreedit(string? text){_preedit=text;InvalidateVisual();}
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e){base.OnAttachedToVisualTree(e);_blinkTimer.Start();}
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e){_blinkTimer.Stop();_dragTimer.Stop();_dragging=false;base.OnDetachedFromVisualTree(e);}
    public void Dispose(){if(_disposed)return;_disposed=true;_blinkTimer.Stop();_dragTimer.Stop();ClearCache();InputClient.Dispose();}
}
