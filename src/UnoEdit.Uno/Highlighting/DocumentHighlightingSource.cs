using System;
using System.Collections.Generic;
using SkiaSharp;
using UnoEdit.Document;
using UnoEdit.Rendering;
using UnoEdit.Rendering.Skia;
using Windows.UI.Text;

namespace UnoEdit.Highlighting;

/// <summary>
/// Bridges the original incremental highlighting engine to native shaped text.
/// Nested highlighting sections are flattened by the original RichText model;
/// no second regular-expression highlighter or document copy is maintained.
/// </summary>
internal sealed class DocumentHighlightingSource : ILineStyleSource, IDisposable
{
    private readonly TextView _view;
    private bool _disposed;

    internal DocumentHighlightingSource(TextView view, IHighlightingDefinition definition)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        Document = view.Document;
        Highlighter = new DocumentHighlighter(Document, definition ?? throw new ArgumentNullException(nameof(definition)));
        Highlighter.HighlightingStateChanged += OnHighlightingStateChanged;
        TextDocumentWeakEventManager.Changed.AddHandler(Document, OnDocumentChanged);
    }

    public TextDocument Document { get; }
    internal DocumentHighlighter Highlighter { get; }
    public event EventHandler<LineStylesChangedEventArgs> StylesChanged;

    public IReadOnlyList<TextStyleSpan> GetStyles(DocumentLine line)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DocumentHighlightingSource));
        if (line == null || line.IsDeleted || !Document.Lines.Contains(line))
            throw new ArgumentException("Line belongs to another document or has been deleted.", nameof(line));
        var highlighted = Highlighter.HighlightLine(line.LineNumber);
        if (highlighted.Sections.Count == 0 || line.Length == 0) return Array.Empty<TextStyleSpan>();
        var context = new TextRunConstructionContext(_view, line);
        var richText = highlighted.ToRichText();
        var spans = new List<TextStyleSpan>();
        foreach (var section in richText.GetHighlightedSections(0, richText.Length))
        {
            var color = section.Color;
            if (section.Length == 0 || color == null || color.IsEmptyForMerge) continue;
            spans.Add(new TextStyleSpan(section.Offset, section.Length)
            {
                Foreground = ToSkia(color.Foreground?.GetColor(context)),
                Background = ToSkia(color.Background?.GetColor(context)),
                FontFamily = color.FontFamily?.Source,
                FontSize = color.FontSize,
                FontWeight = color.FontWeight?.Weight,
                Italic = color.FontStyle.HasValue ? color.FontStyle.Value != FontStyle.Normal : null,
                Underline = color.Underline,
                Strikethrough = color.Strikethrough
            });
        }
        return spans;
    }

    private static SKColor? ToSkia(Windows.UI.Color? color) => color is { } c ? new SKColor(c.R, c.G, c.B, c.A) : null;

    private void OnDocumentChanged(object sender, DocumentChangeEventArgs e)
    {
        // An edit can change the lexical state of later lines (e.g. /*).
        // Evict only matching entries of the viewport's bounded cache; the
        // original engine retains compressed states and recomputes lazily.
        var first = Document.GetLineByOffset(Math.Min(e.Offset, Document.TextLength)).LineNumber;
        StylesChanged?.Invoke(this, new LineStylesChangedEventArgs(first, Document.LineCount));
    }

    private void OnHighlightingStateChanged(int first, int last)
    {
        if (!_disposed && last >= first && last >= 1)
            StylesChanged?.Invoke(this, new LineStylesChangedEventArgs(Math.Max(1, first), Math.Max(1, last)));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        TextDocumentWeakEventManager.Changed.RemoveHandler(Document, OnDocumentChanged);
        Highlighter.HighlightingStateChanged -= OnHighlightingStateChanged;
        Highlighter.Dispose();
        StylesChanged = null;
    }
}
