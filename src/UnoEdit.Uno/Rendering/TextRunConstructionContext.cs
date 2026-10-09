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

/// <summary>
/// Native construction-time visual-line data. Layout and custom generator APIs
/// are a separate migration boundary; this represents the real document line
/// being shaped, not an Avalonia control or a replacement text document.
/// </summary>
public sealed class VisualLine
{
    internal VisualLine(TextView view, DocumentLine line)
    {
        TextView = view;
        FirstDocumentLine = LastDocumentLine = line;
    }
    public TextView TextView { get; }
    public DocumentLine FirstDocumentLine { get; }
    public DocumentLine LastDocumentLine { get; }
    public int StartOffset => FirstDocumentLine.Offset;
    public int DocumentLength => LastDocumentLine.EndOffset - FirstDocumentLine.Offset;
}

/// <summary>Native font/brush values replacing framework-specific text-run properties.</summary>
public sealed record TextRunProperties
{
    public FontFamily FontFamily { get; init; }
    public double FontRenderingEmSize { get; init; }
    public FontWeight FontWeight { get; init; } = Microsoft.UI.Text.FontWeights.Normal;
    public FontStyle FontStyle { get; init; } = FontStyle.Normal;
    public Brush ForegroundBrush { get; init; }
    public Brush BackgroundBrush { get; init; }
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
        GlobalTextRunProperties = new TextRunProperties
        {
            FontFamily = new FontFamily(style.FontFamily),
            FontRenderingEmSize = style.FontSize,
            ForegroundBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(style.Foreground.Alpha, style.Foreground.Red, style.Foreground.Green, style.Foreground.Blue)),
            BackgroundBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(style.Background.Alpha, style.Background.Red, style.Background.Green, style.Background.Blue))
        };
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
        return new StringSegment(Document.GetText(offset, length));
    }
}
