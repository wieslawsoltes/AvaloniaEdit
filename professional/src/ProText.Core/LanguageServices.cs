namespace ProText.Core;
public enum DiagnosticSeverity { Hint,Information,Warning,Error }
public sealed record EditorDiagnostic(string Id,TextRange Range,DiagnosticSeverity Severity,string Message,string? Source=null);
public sealed record CompletionItem(string Label,string InsertText,string? Description=null,string? Kind=null,int Priority=0);
public sealed record CompletionRequest(string Text,int Offset,long Revision,string? Trigger=null)
{
    public TextRange WordRange { get { int end=Math.Clamp(Offset,0,Text.Length),start=end; while(start>0 && IsIdentifier(Text[start-1])) start--; return new(start,end-start); } }
    public string Prefix => Text.Substring(WordRange.Offset,WordRange.Length);
    public static bool IsIdentifier(char c) => char.IsLetterOrDigit(c)||c=='_'||char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.NonSpacingMark;
}
public interface ICompletionProvider { ValueTask<IReadOnlyList<CompletionItem>> GetCompletionsAsync(CompletionRequest request,CancellationToken cancellationToken); }
public interface IDiagnosticProvider { ValueTask<IReadOnlyList<EditorDiagnostic>> GetDiagnosticsAsync(string text,long revision,CancellationToken cancellationToken); }
public sealed class KeywordCompletionProvider : ICompletionProvider
{
    private readonly CompletionItem[] _items;
    public KeywordCompletionProvider(IEnumerable<CompletionItem> items)=>_items=items.ToArray();
    public ValueTask<IReadOnlyList<CompletionItem>> GetCompletionsAsync(CompletionRequest request,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<CompletionItem>>(_items.Where(i=>i.Label.StartsWith(request.Prefix,StringComparison.OrdinalIgnoreCase)).OrderByDescending(i=>i.Priority).ThenBy(i=>i.Label,StringComparer.OrdinalIgnoreCase).Take(200).ToArray());
    }
}
