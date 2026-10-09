using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using UnoEdit.Rendering.Skia;
using Windows.Foundation;
using Windows.UI.Text;

namespace UnoEdit.Rendering;

public readonly record struct Typeface(FontFamily FontFamily, FontStyle Style = FontStyle.Normal, FontWeight Weight = default, FontStretch Stretch = FontStretch.Normal)
{
    public Typeface(string family) : this(new FontFamily(family)) { }
}
public enum BaselineAlignment { Baseline, Top, Center, Bottom, TextTop, TextBottom, Subscript, Superscript }
public enum TextDecorationLocation { Underline, Strikethrough, Overline, Baseline }
public sealed record TextDecoration(TextDecorationLocation Location);
public sealed class TextDecorationCollection : Collection<TextDecoration>
{
    public TextDecorationCollection() { }
    public TextDecorationCollection(IEnumerable<TextDecoration> values) { foreach (var value in values) Add(value); }
}
public static class TextDecorations
{
    public static TextDecorationCollection Underline => new(new[] { new TextDecoration(TextDecorationLocation.Underline) });
    public static TextDecorationCollection Strikethrough => new(new[] { new TextDecoration(TextDecorationLocation.Strikethrough) });
}
public abstract class TextRunProperties
{
    public virtual Typeface Typeface => new(new FontFamily("monospace"));
    public virtual FontFamily FontFamily => Typeface.FontFamily;
    public virtual FontWeight FontWeight => Typeface.Weight.Weight == 0 ? Microsoft.UI.Text.FontWeights.Normal : Typeface.Weight;
    public virtual FontStyle FontStyle => Typeface.Style;
    public virtual double FontRenderingEmSize => 14;
    public virtual Brush ForegroundBrush => new SolidColorBrush(Microsoft.UI.Colors.Black);
    public virtual Brush BackgroundBrush => null;
    public virtual CultureInfo CultureInfo => CultureInfo.CurrentCulture;
    public virtual BaselineAlignment BaselineAlignment => BaselineAlignment.Baseline;
    public virtual TextDecorationCollection TextDecorations => null;
}
internal sealed class GlobalTextRunProperties : TextRunProperties
{
    internal static TextRunProperties Create(TextViewStyle style) => new NativeRunProperties(new Typeface(new FontFamily(style.FontFamily)), style.FontSize,
        NativeRunProperties.Brush(style.Foreground));
}
public sealed class NativeRunProperties : TextRunProperties
{
    private readonly Typeface _typeface; private readonly double _size; private readonly Brush _foreground, _background;
    public NativeRunProperties(Typeface typeface, double size, Brush foreground, Brush background = null)
    { _typeface = typeface; _size = size > 0 && double.IsFinite(size) ? size : throw new ArgumentOutOfRangeException(nameof(size)); _foreground = foreground; _background = background; }
    public override Typeface Typeface => _typeface;
    public override double FontRenderingEmSize => _size;
    public override Brush ForegroundBrush => _foreground;
    public override Brush BackgroundBrush => _background;
    internal static SolidColorBrush Brush(SKColor c) => new(Windows.UI.Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue));
}
public abstract class TextRun
{
    public abstract int Length { get; }
    public virtual TextRunProperties Properties => null;
    public virtual ReadOnlyMemory<char> Text => ReadOnlyMemory<char>.Empty;
}
public sealed class TextCharacters : TextRun
{
    public TextCharacters(ReadOnlyMemory<char> text, TextRunProperties properties) { Text = text; Properties = properties ?? throw new ArgumentNullException(nameof(properties)); }
    public TextCharacters(string text, TextRunProperties properties) : this(text.AsMemory(), properties) { }
    public override int Length => Text.Length;
    public override ReadOnlyMemory<char> Text { get; }
    public override TextRunProperties Properties { get; }
}
public class TextEndOfParagraph : TextRun { public TextEndOfParagraph(int length = 1) { Length = length; } public override int Length { get; } }
public abstract class DrawableTextRun : TextRun
{
    public override int Length => 1;
    public abstract Size Size { get; }
    public abstract double Baseline { get; }
    public abstract void Draw(DrawingContext drawingContext, Point origin);
}
public readonly record struct CharacterHit(int FirstCharacterIndex, int TrailingLength = 0);

/// <summary>A native shaped row. Character indices remain visual-line coordinates.</summary>
public sealed class TextLine : IDisposable
{
    internal readonly NativeTextLayout Owner;
    internal readonly ShapedTextRow Row;
    internal bool OwnsLayout;
    internal TextLine(NativeTextLayout owner, ShapedTextRow row) { Owner = owner; Row = row; }
    public int FirstTextSourceIndex => Owner.ToVisual(Row.Start);
    public int Length => Owner.ToVisual(Row.End) - FirstTextSourceIndex;
    public double Width => Row.Width;
    public double WidthIncludingTrailingWhitespace => Row.Width;
    public double Height => Row.Height;
    public double Baseline => Row.Baseline;
    public int TrailingWhitespaceLength => Owner.CountTrailingWhitespace(this);
    public int NewLineLength => 0;
    public bool HasCollapsed => false;
    private IReadOnlyList<TextRun> _runs;
    public IReadOnlyList<TextRun> TextRuns
    {
        get
        {
            if (_runs != null) return _runs;
            var result = new List<TextRun>(); var at = 0;
            var start = FirstTextSourceIndex; var end = start + Length;
            foreach (var run in Owner.Runs)
            {
                var from = Math.Max(start, at); var to = Math.Min(end, at + run.Length);
                if (from < to) result.Add(run is DrawableTextRun ? run : new TextCharacters(run.Text.Slice(from - at, to - from), run.Properties));
                at += run.Length;
                if (at >= end) break;
            }
            return _runs = result.AsReadOnly();
        }
    }
    public double GetDistanceFromCharacterHit(CharacterHit hit) => Owner.Layout.GetDistanceAtOffset(Owner.ToDisplay(hit.FirstCharacterIndex + hit.TrailingLength), Row);
    public CharacterHit GetCharacterHitFromDistance(double distance)
    {
        var hit = Owner.Layout.HitTest((float)distance, Row.Top + Row.Height / 2);
        var visual = Owner.ToVisual(hit);
        var x = GetDistanceFromCharacterHit(new CharacterHit(visual));
        if (distance < x && visual > FirstTextSourceIndex)
        {
            var previous = Owner.PreviousBoundary(visual);
            return new CharacterHit(previous, visual - previous);
        }
        return new CharacterHit(visual);
    }
    public CharacterHit GetNextCaretCharacterHit(CharacterHit hit) => new(Owner.NextBoundary(hit.FirstCharacterIndex + hit.TrailingLength));
    public CharacterHit GetPreviousCaretCharacterHit(CharacterHit hit) => new(Owner.PreviousBoundary(hit.FirstCharacterIndex + hit.TrailingLength));
    public IReadOnlyList<TextBounds> GetTextBounds(int firstTextSourceIndex, int textLength)
    {
        var result = new List<TextBounds>();
        foreach (var b in Owner.GetRangeRectangles(firstTextSourceIndex, textLength))
            if (Math.Abs(b.Top - Row.Top) < 0.1f) result.Add(new TextBounds(new Rect(b.Left, 0, b.Width, b.Height)));
        return result;
    }
    public void Draw(DrawingContext context, Point origin) => Owner.PaintRow(context, this, origin);
    public void Dispose() { if (OwnsLayout) { OwnsLayout = false; Owner.Dispose(); } }
}
public sealed record TextBounds(Rect Rectangle);
