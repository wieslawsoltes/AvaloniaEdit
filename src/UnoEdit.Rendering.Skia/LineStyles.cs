using System;
using System.Collections.Generic;
using SkiaSharp;
using UnoEdit.Document;

namespace UnoEdit.Rendering.Skia;

/// <summary>A non-overlapping style run using offsets relative to a document line.</summary>
public sealed record TextStyleSpan(int Start, int Length)
{
    public SKColor? Foreground { get; init; }
    public SKColor? Background { get; init; }
    /// <summary>Exact advance for a single object-replacement character, or null for text.</summary>
    public float? ObjectWidth { get; init; }
    public float? ObjectHeight { get; init; }
    public float? ObjectBaseline { get; init; }
    public string FontFamily { get; init; }
    public float? FontSize { get; init; }
    public int? FontWeight { get; init; }
    public bool? Italic { get; init; }
    public bool? Underline { get; init; }
    public bool? Strikethrough { get; init; }
}

/// <summary>
/// Supplies styles to the shaped text pipeline without coupling it to a UI
/// framework. Runs must be sorted, non-overlapping and within the line. The
/// viewport owns neither this provider nor the returned immutable run values.
/// </summary>
public interface ILineStyleSource
{
    TextDocument Document { get; }
    IReadOnlyList<TextStyleSpan> GetStyles(DocumentLine line);
    event EventHandler<LineStylesChangedEventArgs> StylesChanged;
}

/// <summary>Inclusive document line range whose previously shaped styles changed.</summary>
public sealed class LineStylesChangedEventArgs : EventArgs
{
    public LineStylesChangedEventArgs(int firstLine, int lastLine)
    {
        if (firstLine < 1) throw new ArgumentOutOfRangeException(nameof(firstLine));
        if (lastLine < firstLine) throw new ArgumentOutOfRangeException(nameof(lastLine));
        FirstLine = firstLine;
        LastLine = lastLine;
    }
    public int FirstLine { get; }
    public int LastLine { get; }
}
