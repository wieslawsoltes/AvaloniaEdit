using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ProText.Core;
namespace ProText.Avalonia.Controls;
/// <summary>A bounded document overview. Draws at most 512 line samples rather than rasterizing the entire document.</summary>
public class EditorOverview : Control
{
    public static readonly StyledProperty<ProTextEditor?> OwnerProperty=AvaloniaProperty.Register<EditorOverview,ProTextEditor?>(nameof(Owner));
    public static readonly StyledProperty<IBrush?> LineBrushProperty=AvaloniaProperty.Register<EditorOverview,IBrush?>(nameof(LineBrush),Brush.Parse("#586C849E"));
    public ProTextEditor? Owner { get=>GetValue(OwnerProperty); set=>SetValue(OwnerProperty,value); }
    public IBrush? LineBrush { get=>GetValue(LineBrushProperty); set=>SetValue(LineBrushProperty,value); }
    private ProTextEditor? _subscribed;
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e) { base.OnPropertyChanged(e); if(e.Property==OwnerProperty) Hook(); InvalidateVisual(); }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Hook(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { Unhook(); base.OnDetachedFromVisualTree(e); }
    private void Hook() { Unhook(); _subscribed=Owner; if(_subscribed is not null) _subscribed.ViewChanged+=OnChanged; }
    private void Unhook() { if(_subscribed is not null) _subscribed.ViewChanged-=OnChanged; _subscribed=null; }
    private void OnChanged(object? sender,EventArgs e)=>InvalidateVisual();
    public override void Render(DrawingContext context)
    {
        base.Render(context); var owner=Owner; if(owner?.Session is null||Bounds.Height<=0) return;
        var document=owner.Session.Document; int count=document.LineCount, samples=Math.Min(512,Math.Min(count,(int)Math.Max(1,Bounds.Height/2)));
        for(int i=0;i<samples;i++)
        {
            int line=1+(int)((long)i*count/samples); var info=document.GetLineByNumber(line);
            double y=(double)i/samples*Bounds.Height,width=Math.Min(Bounds.Width-8,info.Length*.65);
            if(width>0) context.DrawRectangle(LineBrush,null,new Rect(4,y,width,1));
        }
        foreach(var bookmark in owner.Session.Bookmarks.Take(1000)) context.DrawRectangle(Brush.Parse("#91AAFB"),null,new Rect(0,(double)(bookmark.Line-1)/count*Bounds.Height,4,3));
        foreach(var diagnostic in owner.Session.Diagnostics.Take(1000))
        {
            int line=document.GetLineByOffset(diagnostic.Range.Offset).LineNumber;
            context.DrawRectangle(Brush.Parse(diagnostic.Severity==DiagnosticSeverity.Error?"#EF7A85":"#E5B974"),null,new Rect(Math.Max(0,Bounds.Width-5),(double)(line-1)/count*Bounds.Height,5,3));
        }
        var view=owner.Editor.TextArea.TextView;
        if(view.VisualLinesValid&&view.VisualLines.Count>0)
        {
            int first=view.VisualLines[0].FirstDocumentLine.LineNumber,last=view.VisualLines[^1].LastDocumentLine.LineNumber;
            double top=(double)(first-1)/count*Bounds.Height,height=Math.Max(5,(double)(last-first+1)/count*Bounds.Height);
            context.DrawRectangle(Brush.Parse("#245E9ED5"),new Pen(Brush.Parse("#706B9DCD"),1),new Rect(0,top,Math.Max(1,Bounds.Width),Math.Min(height,Bounds.Height-top)));
        }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); if(Owner?.Session is null) return;
        Owner.GoTo(1+(int)(Math.Clamp(e.GetPosition(this).Y/Math.Max(1,Bounds.Height),0,1)*(Owner.Session.LineCount-1))); Owner.FocusEditor(); e.Handled=true;
    }
}
