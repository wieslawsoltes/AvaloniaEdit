using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UnoEdit.Document;
using UnoEdit.Rendering;

namespace UnoEdit.Editing;

/// <summary>Native margin base preserving the editor's view/document lifetime contract.</summary>
public abstract class AbstractMargin : Grid, ITextViewConnect
{
    private bool _automatic;
    public static readonly DependencyProperty TextViewProperty = DependencyProperty.Register(nameof(TextView), typeof(TextView), typeof(AbstractMargin), new PropertyMetadata(null, (owner, args) =>
    { var margin = (AbstractMargin)owner; margin._automatic = false; margin.OnTextViewChanged((TextView)args.OldValue, (TextView)args.NewValue); }));
    public TextView TextView { get => (TextView)GetValue(TextViewProperty); set => SetValue(TextViewProperty, value); }
    public TextDocument Document { get; private set; }
    protected TextArea TextArea { get; set; }
    public virtual void InvalidateVisual() => InvalidateArrange();
    protected virtual void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        if (oldTextView != null) { oldTextView.DocumentChanged -= ViewDocumentChanged; oldTextView.VisualLinesChanged -= ViewLinesChanged; oldTextView.ScrollOffsetChanged -= ViewLinesChanged; }
        if (newTextView != null) { newTextView.DocumentChanged += ViewDocumentChanged; newTextView.VisualLinesChanged += ViewLinesChanged; newTextView.ScrollOffsetChanged += ViewLinesChanged; }
        OnDocumentChanged(Document, newTextView?.Document);
        TextArea = newTextView?.GetService(typeof(TextArea)) as TextArea;
        OnTextViewVisualLinesChanged();
    }
    protected virtual void OnDocumentChanged(TextDocument oldDocument, TextDocument newDocument) => Document = newDocument;
    protected virtual void OnTextViewVisualLinesChanged() => InvalidateVisual();
    private void ViewDocumentChanged(object sender, DocumentChangedEventArgs args) => OnDocumentChanged(args.OldDocument, args.NewDocument);
    private void ViewLinesChanged(object sender, EventArgs args) => OnTextViewVisualLinesChanged();
    void ITextViewConnect.AddToTextView(TextView view)
    { if (TextView == null) { TextView = view; _automatic = true; } else if (TextView != view) throw new InvalidOperationException("Margin belongs to another text view."); }
    void ITextViewConnect.RemoveFromTextView(TextView view) { if (_automatic && TextView == view) TextView = null; }
}
