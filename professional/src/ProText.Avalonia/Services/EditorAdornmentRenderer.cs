using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using ProText.Core;
using ProText.Avalonia.Controls;
namespace ProText.Avalonia.Services;
internal sealed class EditorAdornmentRenderer(ProTextEditor owner) : IBackgroundRenderer
{
    public KnownLayer Layer=>KnownLayer.Selection;
    public void Draw(TextView view,DrawingContext context)
    {
        if(!view.VisualLinesValid||view.VisualLines.Count==0) return;
        int first=view.VisualLines[0].FirstDocumentLine.Offset;
        int last=view.VisualLines[^1].LastDocumentLine.EndOffset;
        if(owner.IsSearchOpen)
            foreach(var hit in owner.Search.Hits)
            {
                if(hit.Range.Offset>last) break;
                if(hit.Range.End<first) continue;
                foreach(var rect in Rects(view,hit.Range)) context.DrawRectangle(Brush.Parse("#506FA5DD"),new Pen(Brush.Parse("#B08FC7FF"),1),rect,2,2);
            }
        foreach(var diagnostic in owner.Session?.Diagnostics??[])
        {
            if(diagnostic.Range.Offset>last) break;
            if(diagnostic.Range.End<first) continue;
            IBrush brush=Brush.Parse(diagnostic.Severity==DiagnosticSeverity.Error?"#F07983":diagnostic.Severity==DiagnosticSeverity.Warning?"#E1B567":"#75AFDD");
            foreach(var rect in Rects(view,diagnostic.Range))
            {
                var pen=new Pen(brush,1.2); double y=rect.Bottom-1;
                for(double x=rect.Left;x<rect.Right;x+=4) { context.DrawLine(pen,new Point(x,y),new Point(Math.Min(x+2,rect.Right),y-2)); context.DrawLine(pen,new Point(Math.Min(x+2,rect.Right),y-2),new Point(Math.Min(x+4,rect.Right),y)); }
            }
        }
    }
    private static IEnumerable<Rect> Rects(TextView view,TextRange range)
    {
        var segment=new TextSegment { StartOffset=range.Offset,Length=range.Length };
        return BackgroundGeometryBuilder.GetRectsForSegment(view,segment);
    }
}
