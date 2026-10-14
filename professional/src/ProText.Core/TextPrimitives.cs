using System.Text;
namespace ProText.Core;

/// <summary>A half-open range of UTF-16 code units.</summary>
public readonly record struct TextRange
{
    public int Offset { get; }
    public int Length { get; }
    public int End => checked(Offset + Length);
    public TextRange(int offset, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        _ = checked(offset + length); Offset = offset; Length = length;
    }
    public bool Contains(int offset) => offset >= Offset && offset < End;
    public void ValidateFor(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (End > text.Length) throw new ArgumentOutOfRangeException(nameof(text), "Range exceeds document length.");
    }
}
public sealed record TextEdit(TextRange Range, string NewText);

/// <summary>Preflights every edit before mutation. Offsets refer to a single original snapshot.</summary>
public static class EditBatch
{
    public static IReadOnlyList<TextEdit> Validate(string snapshot, IEnumerable<TextEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(edits);
        var ordered = edits.OrderBy(x => x.Range.Offset).ThenBy(x => x.Range.Length).ToArray();
        long resultingLength = snapshot.Length;
        for (int i = 0; i < ordered.Length; i++)
        {
            var edit = ordered[i]; ArgumentNullException.ThrowIfNull(edit.NewText); edit.Range.ValidateFor(snapshot);
            if (i > 0 && (ordered[i-1].Range.End > edit.Range.Offset || ordered[i-1].Range.Offset == edit.Range.Offset))
                throw new ArgumentException("Edits must not overlap or share an insertion boundary.", nameof(edits));
            resultingLength += (long)edit.NewText.Length - edit.Range.Length;
        }
        if (resultingLength > int.MaxValue) throw new ArgumentException("Resulting document is too large.", nameof(edits));
        return ordered;
    }
    public static string Apply(string snapshot, IEnumerable<TextEdit> edits)
    {
        var ordered = Validate(snapshot, edits); var builder = new StringBuilder(snapshot.Length); int cursor = 0;
        foreach (var edit in ordered)
        {
            builder.Append(snapshot, cursor, edit.Range.Offset - cursor); builder.Append(edit.NewText); cursor = edit.Range.End;
        }
        return builder.Append(snapshot, cursor, snapshot.Length - cursor).ToString();
    }
}
public readonly record struct TextLine(int Number, int Offset, int Length, int DelimiterLength)
{
    public int End => Offset + Length;
    public int TotalLength => Length + DelimiterLength;
}
/// <summary>CRLF, LF and CR-aware immutable line map. Line and column numbers are one-based.</summary>
public sealed class LineMap
{
    private readonly TextLine[] _lines;
    public IReadOnlyList<TextLine> Lines => _lines;
    public int Count => _lines.Length;
    public int TextLength { get; }
    public LineMap(string text)
    {
        ArgumentNullException.ThrowIfNull(text); TextLength = text.Length;
        var lines = new List<TextLine>(); int start = 0, i = 0;
        while (i < text.Length)
        {
            if (text[i] is not ('\r' or '\n')) { i++; continue; }
            int delimiter = text[i] == '\r' && i + 1 < text.Length && text[i+1] == '\n' ? 2 : 1;
            lines.Add(new(lines.Count+1, start, i-start, delimiter)); i += delimiter; start = i;
        }
        lines.Add(new(lines.Count+1, start, text.Length-start, 0)); _lines = lines.ToArray();
    }
    public TextLine this[int number] => _lines[Math.Clamp(number, 1, Count)-1];
    public TextLine AtOffset(int offset)
    {
        offset = Math.Clamp(offset, 0, TextLength); int low = 0, high = Count-1;
        while (low < high) { int mid = (low+high+1)/2; if (_lines[mid].Offset <= offset) low = mid; else high = mid-1; }
        return _lines[low];
    }
    public TextRange ExpandToLines(TextRange selection)
    {
        if (selection.End > TextLength) throw new ArgumentOutOfRangeException(nameof(selection));
        var start = AtOffset(selection.Offset); var end = AtOffset(selection.Length > 0 ? selection.End-1 : selection.End);
        return new(start.Offset, end.End-start.Offset);
    }
    public int GetOffset(int line, int column = 1) { var l = this[line]; return l.Offset + Math.Clamp(column-1, 0, l.Length); }
}
public enum LineEnding { Lf, CrLf, Cr }
public static class TextOperations
{
    public static string Delimiter(LineEnding ending) => ending switch { LineEnding.CrLf => "\r\n", LineEnding.Cr => "\r", _ => "\n" };
    public static LineEnding DetectLineEnding(string text)
    {
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') return LineEnding.Lf;
            else if (text[i] == '\r') return i+1 < text.Length && text[i+1] == '\n' ? LineEnding.CrLf : LineEnding.Cr;
        return LineEnding.Lf;
    }
    public static string NormalizeLineEndings(string text, LineEnding ending) => text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", Delimiter(ending));
    public static IReadOnlyList<TextEdit> TrimTrailingWhitespace(string text)
    {
        var edits = new List<TextEdit>();
        foreach (var line in new LineMap(text).Lines)
        {
            int end = line.End; while (end > line.Offset && text[end-1] is ' ' or '\t') end--;
            if (end < line.End) edits.Add(new(new(end, line.End-end), ""));
        }
        return edits;
    }
    public static IReadOnlyList<TextEdit> Indent(string text, TextRange range, int tabSize, bool useTabs, bool unindent)
    {
        if (tabSize is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(tabSize));
        range.ValidateFor(text); var map = new LineMap(text); var lines = map.ExpandToLines(range); var edits = new List<TextEdit>();
        foreach (var line in map.Lines.Where(l => l.Offset >= lines.Offset && l.Offset <= lines.End))
        {
            if (!unindent) edits.Add(new(new(line.Offset,0), useTabs ? "\t" : new string(' ',tabSize)));
            else
            {
                int count = 0;
                if (line.Length > 0 && text[line.Offset] == '\t') count = 1;
                else while (count < Math.Min(tabSize,line.Length) && text[line.Offset+count] == ' ') count++;
                if (count > 0) edits.Add(new(new(line.Offset,count),""));
            }
        }
        return edits;
    }
    public static IReadOnlyList<TextEdit> ToggleLineComment(string text, TextRange range, string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix) || prefix.Contains('\n') || prefix.Contains('\r')) throw new ArgumentException("A single-line comment prefix is required.",nameof(prefix));
        range.ValidateFor(text); var map = new LineMap(text); var expanded = map.ExpandToLines(range);
        int Start(TextLine line) { int i=line.Offset; while(i<line.End && text[i] is ' ' or '\t') i++; return i; }
        var nonempty = map.Lines.Where(l => l.Offset >= expanded.Offset && l.Offset <= expanded.End && Start(l)<l.End).ToArray();
        bool remove = nonempty.Length > 0 && nonempty.All(l => text.AsSpan(Start(l),l.End-Start(l)).StartsWith(prefix,StringComparison.Ordinal));
        return nonempty.Select(l => { int offset=Start(l), length=0; if(remove) { length=prefix.Length; if(offset+length<l.End && text[offset+length]==' ') length++; } return new TextEdit(new(offset,length),remove ? "" : prefix+" "); }).ToArray();
    }
}
