using Scribe.Core;
namespace Scribe.Sample;
public static class SampleContent
{
    public const string Code="""
    using System;
    using System.Threading.Tasks;

    namespace Scribe.Showcase;

    // This text is edited by an original engine, not another editor control.
    public sealed class Welcome
    {
        private readonly string message = "Hello, independent editor!";

        public async Task RunAsync()
        {
            Console.WriteLine(message);
            await Task.Delay(120);

            // TODO: connect a language service for your application.
            foreach (var platform in new[] { "Desktop", "Browser" })
            {
                Console.WriteLine($"Welcome to {platform}.");
            }
        }

        public static double Area(double width, double height)
        {
            if (width < 0 || height < 0)
            {
                throw new ArgumentOutOfRangeException();
            }

            return width * height;
        }
    }

    // Alt+click adds a caret. Ctrl/Cmd+D selects the next occurrence.
    // Linked snippet placeholders update together. Tab moves to the next stop.
    // Replace the ControlTheme without rebuilding the editing surface.
    // Use the overview, mouse wheel, or Go to navigate a large document.
    """;
    public const string Notes="""
    Independent text editing
    ========================

    The engine preserves CR, LF and CRLF and uses immutable snapshots.
    Editing, history, multi-selection and snippet state live outside the renderer.

    Unicode: Zażółć gęślą jaźń. Καλημέρα. 日本語. 🌍
    Combining mark: é. Family emoji: 👩‍👩‍👧‍👦.

    Switch on word wrap and resize this view. TextLayout shapes the visible
    text and supplies hit-testing; it does not provide document editing.
    """;
    public const string Json="""
    {
      "editor": {
        "engine": "Scribe.Core",
        "viewport": "Scribe.Avalonia.EditorViewport",
        "externalEditorDependencies": 0,
        "tabSize": 4,
        "features": ["multi-caret", "snippets", "search", "undo"]
      },
      "platforms": ["desktop", "browser"]
    }
    """;
}
public sealed class SampleCompletion : ICompletionProvider
{
    public ValueTask<IReadOnlyList<Completion>> CompleteAsync(CompletionRequest request,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();int start=request.Offset;while(start>0&&EditSession.IsWord(request.Snapshot[start-1]))start--;
        string prefix=request.Snapshot.GetText(start,request.Offset-start);
        Completion[] items=[new("Console","Console","System console"),new("Console.WriteLine","Console.WriteLine(\"Hello\");","Write a line"),new("foreach","foreach (var ${1:item} in ${2:collection})\n{\n    $0\n}","Linked-placeholder snippet",true),new("public","public","Access modifier"),new("private","private","Access modifier"),new("async","async","Asynchronous modifier"),new("await","await","Await an operation")];
        return ValueTask.FromResult<IReadOnlyList<Completion>>(items.Where(i=>i.Label.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)).ToArray());
    }
}
public sealed class SampleDiagnostics : IDiagnosticProvider
{
    public ValueTask<IReadOnlyList<Diagnostic>> AnalyzeAsync(TextSnapshot snapshot,long revision,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();string text=snapshot.ToString();var results=new List<Diagnostic>();int start=0;
        while((start=text.IndexOf("TODO",start,StringComparison.Ordinal))>=0){results.Add(new(start,4,"Unfinished work item (sample provider)",false));start+=4;}
        return ValueTask.FromResult<IReadOnlyList<Diagnostic>>(results);
    }
}
