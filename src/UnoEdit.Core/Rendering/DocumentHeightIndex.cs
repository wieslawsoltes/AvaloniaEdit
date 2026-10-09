using System;
using UnoEdit.Document;

namespace UnoEdit.Rendering;

/// <summary>
/// UI-independent facade over the original editor's augmented red-black height
/// tree. Document edits update the index through the existing line tracker.
/// Lookup and individual height changes do not scan preceding document lines.
/// All access follows the owning TextDocument's thread ownership rules.
/// </summary>
public sealed class DocumentHeightIndex : IDisposable
{
    private readonly TextDocument _document;
    private readonly HeightTree _tree;
    private bool _disposed;

    public DocumentHeightIndex(TextDocument document, double defaultLineHeight)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        ValidateHeight(defaultLineHeight);
        _document.VerifyAccess();
        _tree = new HeightTree(document, defaultLineHeight);
    }

    public double TotalHeight { get { VerifyAccess(); return _tree.TotalHeight; } }
    public int LineCount { get { VerifyAccess(); return _tree.LineCount; } }
    public double DefaultLineHeight
    {
        get { VerifyAccess(); return _tree.DefaultLineHeight; }
        set { VerifyAccess(); ValidateHeight(value); _tree.DefaultLineHeight = value; }
    }

    public DocumentLine GetLineByVisualPosition(double position)
    {
        VerifyAccess();
        if (!double.IsFinite(position)) throw new ArgumentOutOfRangeException(nameof(position));
        return _tree.GetLineByVisualPosition(Math.Clamp(position, 0, Math.Max(0, _tree.TotalHeight)));
    }

    public double GetVisualPosition(DocumentLine line) { ValidateLine(line); return _tree.GetVisualPosition(line); }
    public double GetHeight(DocumentLine line) { ValidateLine(line); return _tree.GetHeight(line); }
    public void SetHeight(DocumentLine line, double height) { ValidateLine(line); ValidateHeight(height); _tree.SetHeight(line, height); }

    public bool GetIsCollapsed(int lineNumber)
    {
        VerifyAccess();
        if (lineNumber < 1 || lineNumber > _document.LineCount) throw new ArgumentOutOfRangeException(nameof(lineNumber));
        return _tree.GetIsCollapsed(lineNumber);
    }

    public CollapsedLineSection CollapseText(DocumentLine start, DocumentLine end)
    {
        ValidateLine(start);
        ValidateLine(end);
        if (start.LineNumber > end.LineNumber) throw new ArgumentException("Start must precede end.", nameof(start));
        return _tree.CollapseText(start, end);
    }

    private void ValidateLine(DocumentLine line)
    {
        VerifyAccess();
        if (line == null) throw new ArgumentNullException(nameof(line));
        if (line.IsDeleted || !_document.Lines.Contains(line)) throw new ArgumentException("Line does not belong to this document.", nameof(line));
    }

    private static void ValidateHeight(double height)
    {
        if (!double.IsFinite(height) || height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
    }

    private void VerifyAccess()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DocumentHeightIndex));
        _document.VerifyAccess();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _document.VerifyAccess();
        _tree.Dispose();
        _disposed = true;
    }
}
