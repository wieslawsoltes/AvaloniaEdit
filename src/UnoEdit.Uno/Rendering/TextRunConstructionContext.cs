using System;
using Microsoft.UI.Xaml.Media;
using UnoEdit.Document;
using UnoEdit.Utils;
using Windows.UI.Text;

namespace UnoEdit.Rendering;

/// <summary>Data available while constructing a styled native visual line.</summary>
public interface ITextRunConstructionContext
{
    TextDocument Document { get; }
    TextView TextView { get; }
    VisualLine VisualLine { get; }
    TextRunProperties GlobalTextRunProperties { get; }
    StringSegment GetText(int offset, int length);
}

internal sealed class TextRunConstructionContext : ITextRunConstructionContext
{
    private readonly string _text;
    private readonly int _offset;
    internal TextRunConstructionContext(TextView view, DocumentLine line)
    {
        Document = view.Document;
        TextView = view;
        VisualLine = new VisualLine(view, line);
        _offset = line.Offset;
        _text = Document.GetText(line.Offset, line.Length);
        var style = view.Viewport.Style;
        GlobalTextRunProperties = global::UnoEdit.Rendering.GlobalTextRunProperties.Create(style);
    }
    public TextDocument Document { get; }
    public TextView TextView { get; }
    public VisualLine VisualLine { get; }
    public TextRunProperties GlobalTextRunProperties { get; }
    public StringSegment GetText(int offset, int length)
    {
        if (offset < 0 || offset > Document.TextLength || length < 0 || length > Document.TextLength - offset)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (offset >= _offset && offset - _offset <= _text.Length && length <= _text.Length - (offset - _offset))
            return new StringSegment(_text, offset - _offset, length);
        return new StringSegment(Document.GetText(offset, length), 0, length);
    }
}
