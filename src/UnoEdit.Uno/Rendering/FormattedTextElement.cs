using System;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using UnoEdit.Rendering.Skia;
using Windows.Foundation;

namespace UnoEdit.Rendering;

/// <summary>Atomic shaped text replacing a document range (for example a folding label).</summary>
public class FormattedTextElement : VisualLineElement
{
    internal readonly FormattedText FormattedText;
    internal readonly string Text;
    internal TextLine TextLine;
    internal bool OwnsTextLine;
    public FormattedTextElement(string text, int documentLength) : base(1, documentLength) { Text = text ?? throw new ArgumentNullException(nameof(text)); }
    public FormattedTextElement(TextLine text, int documentLength) : base(1, documentLength) { TextLine = text ?? throw new ArgumentNullException(nameof(text)); }
    public FormattedTextElement(FormattedText text, int documentLength) : base(1, documentLength) { FormattedText = text ?? throw new ArgumentNullException(nameof(text)); }
    public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
    {
        if (TextLine == null && FormattedText == null) { TextLine = PrepareText(TextFormatter.Current, Text, TextRunProperties); OwnsTextLine = true; }
        return new FormattedTextRun(this, TextRunProperties);
    }
    public static TextLine PrepareText(TextFormatter formatter, string text, TextRunProperties properties)
    {
        if (formatter == null) throw new ArgumentNullException(nameof(formatter));
        return formatter.FormatText(text, properties);
    }
}
public class FormattedTextRun : DrawableTextRun
{
    public FormattedTextRun(FormattedTextElement element, TextRunProperties properties) { Element = element ?? throw new ArgumentNullException(nameof(element)); Properties = properties ?? throw new ArgumentNullException(nameof(properties)); }
    public FormattedTextElement Element { get; }
    public override TextRunProperties Properties { get; }
    public override double Baseline => Element.FormattedText?.Baseline ?? Element.TextLine.Baseline;
    public override Size Size => Element.FormattedText != null ? new(Element.FormattedText.WidthIncludingTrailingWhitespace, Element.FormattedText.Height) : new(Element.TextLine.WidthIncludingTrailingWhitespace, Element.TextLine.Height);
    public override void Draw(DrawingContext context, Point origin)
    { if (Element.FormattedText != null) Element.FormattedText.Draw(context, origin); else Element.TextLine.Draw(context, origin); }
}
public sealed class TextFormatter : IDisposable
{
    public static TextFormatter Current { get; } = new();
    public static TextFormatter Create() => new();
    public TextLine FormatText(string text, TextRunProperties properties)
    {
        if (text == null) throw new ArgumentNullException(nameof(text));
        if (properties == null) throw new ArgumentNullException(nameof(properties));
        var style = new TextViewStyle { FontFamily = properties.FontFamily?.Source ?? "monospace", FontSize = (float)properties.FontRenderingEmSize };
        var runs = text.Length == 0 ? Array.Empty<TextRun>() : new TextRun[] { new TextCharacters(text, properties) };
        var owner = new NativeTextLayout(runs, style, null, 0);
        var line = owner.Lines[0]; line.OwnsLayout = true; return line;
    }
    public void Dispose() { /* Stateless formatter; each returned text line owns its shaped buffers. */ }
}
public sealed class FormattedText : IDisposable
{
    private readonly TextLine _line;
    public FormattedText(string text, CultureInfo culture, FlowDirection direction, Typeface typeface, double emSize, Brush foreground)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        _line = TextFormatter.Current.FormatText(text, new NativeRunProperties(typeface, emSize, foreground));
    }
    public string Text { get; }
    public double Width => _line.Width;
    public double WidthIncludingTrailingWhitespace => _line.WidthIncludingTrailingWhitespace;
    public double Height => _line.Height;
    public double Baseline => _line.Baseline;
    public void Draw(DrawingContext context, Point origin) => _line.Draw(context, origin);
    public void Dispose() => _line.Dispose();
}
public class InlineObjectElement : VisualLineElement
{
    public InlineObjectElement(int documentLength, FrameworkElement element) : base(1, documentLength) { Element = element ?? throw new ArgumentNullException(nameof(element)); }
    public FrameworkElement Element { get; }
    public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context) => new InlineObjectRun(1, TextRunProperties, Element);
}
public class InlineObjectRun : DrawableTextRun
{
    internal Size DesiredSize;
    public InlineObjectRun(int length, TextRunProperties properties, FrameworkElement element)
    { if (length < 1) throw new ArgumentOutOfRangeException(nameof(length)); Length = length; Properties = properties ?? throw new ArgumentNullException(nameof(properties)); Element = element ?? throw new ArgumentNullException(nameof(element)); }
    public FrameworkElement Element { get; }
    public VisualLine VisualLine { get; internal set; }
    public override int Length { get; }
    public override TextRunProperties Properties { get; }
    public override Size Size => DesiredSize;
    public override double Baseline => DesiredSize.Height;
    public override void Draw(DrawingContext context, Point origin)
    { if (VisualLine == null) throw new InvalidOperationException("Inline controls must be attached to a native TextView before drawing."); }
}
