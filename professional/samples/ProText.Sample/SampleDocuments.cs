using ProText.Core;
namespace ProText.Sample;
public static class SampleDocuments
{
    public const string Code="""
    using System;
    using System.Threading.Tasks;

    namespace ProText.Showcase;

    // A familiar editor. A completely replaceable visual template.
    public sealed class Welcome
    {
        private readonly string _message = "Hello, Avalonia!";

        public async Task RunAsync()
        {
            Console.WriteLine(_message);
            await Task.Delay(120);

            // TODO: connect your own completion and diagnostic providers.
            foreach (var name in new[] { "Desktop", "Browser", "Your application" })
            {
                Console.WriteLine($"Welcome to {name}.");
            }
        }

        public static double CalculateArea(double width, double height)
        {
            if (width < 0 || height < 0)
            {
                throw new ArgumentOutOfRangeException("Dimensions must be positive.");
            }

            return width * height;
        }
    }

    // Explore the gallery:
    // • Find / replace with regex, case, word and selection scope.
    // • Split views share one document and one undo history.
    // • Swap the ControlTheme without replacing the text surface.
    // • Fold code, toggle bookmarks, or load 100,000 lines.
    """;
    public const string Json="""
    {
      "editor": {
        "fontSize": 14,
        "tabSize": 4,
        "wordWrap": false,
        "showLineNumbers": true,
        "theme": "dark",
        "features": ["search", "folding", "completion", "diagnostics"]
      },
      "platforms": ["desktop", "browser"],
      "sharedDocument": true
    }
    """;
    public const string Notes="""
    A space for your words
    ======================

    This is a plain-text document, not a rich-text word processor.
    Editing, undo, selection, search, wrapping and shared views use
    the same document model as the code editor.

    Unicode matters: Zażółć gęślą jaźń. Καλημέρα. 日本語. 🌍

    Try selecting a paragraph and restricting search to that selection.
    Switch to another document, then return: your edits are still here.
    """;
}
/// <summary>Illustrative TODO provider, not a compiler or language server.</summary>
public sealed class SampleDiagnostics : IDiagnosticProvider
{
    public ValueTask<IReadOnlyList<EditorDiagnostic>> GetDiagnosticsAsync(string text,long revision,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); var diagnostics=new List<EditorDiagnostic>(); int start=0;
        while((start=text.IndexOf("TODO",start,StringComparison.Ordinal))>=0)
        {
            diagnostics.Add(new("DEMO001",new(start,4),DiagnosticSeverity.Warning,"Unfinished work item","Gallery TODO provider")); start+=4;
        }
        return ValueTask.FromResult<IReadOnlyList<EditorDiagnostic>>(diagnostics);
    }
}
