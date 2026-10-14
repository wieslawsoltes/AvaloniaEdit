using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
namespace Scribe.Avalonia;
/// <summary>Original sampled overview, separate from the templated editor chrome.</summary>
public class EditorOverview : Control
{
    public static readonly StyledProperty<ScribeEditor?> OwnerProperty=AvaloniaProperty.Register<EditorOverview,ScribeEditor?>(nameof(Owner));
    public ScribeEditor? Owner {get=>GetValue(OwnerProperty);set=>SetValue(OwnerProperty,value);}
    private ScribeEditor? _hook;
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e){base.OnPropertyChanged(e);if(e.Property==OwnerProperty)Hook();}
    private void Hook(){if(_hook is not null)_hook.ViewChanged-=Changed;_hook=Owner;if(_hook is not null)_hook.ViewChanged+=Changed;InvalidateVisual();}
    private void Changed(object? sender,EventArgs e)=>InvalidateVisual();
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e){base.OnAttachedToVisualTree(e);Hook();}
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e){if(_hook is not null)_hook.ViewChanged-=Changed;_hook=null;base.OnDetachedFromVisualTree(e);}
    public override void Render(DrawingContext context)
    {
        base.Render(context);var owner=Owner;if(owner?.Session is null||Bounds.Height<=0)return;
        var snapshot=owner.Session.Document.Snapshot;int samples=Math.Min(512,Math.Min(snapshot.LineCount,(int)Math.Max(1,Bounds.Height/2)));
        for(int i=0;i<samples;i++)
        {int index=(int)((long)i*snapshot.LineCount/samples);var line=snapshot.GetLine(index);double width=Math.Min(Bounds.Width-8,line.Length*.65);if(width>0)context.FillRectangle(owner.GutterBrush??Brushes.Transparent,new Rect(4,(double)i/samples*Bounds.Height,width,1));}
        foreach(var bookmark in owner.Session.Bookmarks.Take(1000))context.FillRectangle(owner.CaretBrush??Brushes.Transparent,new Rect(0,(double)snapshot.LineAt(bookmark.Offset)/snapshot.LineCount*Bounds.Height,4,3));
        foreach(var d in owner.Diagnostics.Take(1000))context.FillRectangle(Brush.Parse(d.IsError?"#EA7B88":"#DAB065"),new Rect(Math.Max(0,Bounds.Width-5),(double)snapshot.LineAt(d.Start)/snapshot.LineCount*Bounds.Height,5,3));
        double top=(double)owner.Viewport.FirstVisibleLine/snapshot.LineCount*Bounds.Height,height=Math.Max(6,(double)Math.Max(1,owner.Viewport.RealizedLineCount)/snapshot.LineCount*Bounds.Height);
        context.DrawRectangle(owner.SelectionBrush,new Pen(owner.CaretBrush,.5),new Rect(0,top,Math.Max(1,Bounds.Width),Math.Min(height,Bounds.Height-top)));
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);if(Owner?.Session is null)return;int line=1+(int)(Math.Clamp(e.GetPosition(this).Y/Math.Max(1,Bounds.Height),0,1)*(Owner.Session.Document.LineCount-1));
        Owner.Session.GoTo(line);Owner.RevealCaret();Owner.FocusEditor();e.Handled=true;
    }
}
