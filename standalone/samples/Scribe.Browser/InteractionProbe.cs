using System.Runtime.InteropServices.JavaScript;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Scribe.Core;
namespace Scribe.Browser;
public static partial class TestBridge
{
    private static DeferredCompletion? _deferredCompletion;
    private static DeferredDiagnostics? _deferredDiagnostics;
    private static ICompletionProvider? _originalCompletion;
    private static IDiagnosticProvider? _originalDiagnostics;
    private static Task? _pendingCompletion,_pendingDiagnostics;
    [JSExport]
    public static string CompletionBounds()
    {
        var root=TopLevel.GetTopLevel(View)??throw new InvalidOperationException("Gallery has no TopLevel.");
        // Inspect the rendered tree, not a template's logical child which might belong to a detached popup.
        var list=root.GetVisualDescendants().OfType<ListBox>().FirstOrDefault(c=>c.Name=="PART_CompletionList"&&c.Bounds.Width>0&&c.Bounds.Height>0);
        if(list is null)throw new InvalidOperationException("No rendered completion list. Visual list boxes: "+string.Join(";",root.GetVisualDescendants().OfType<ListBox>().Select(c=>$"{c.Name}:{c.Bounds}")));
        var p=list.TranslatePoint(new global::Avalonia.Point(0,0),View)??throw new InvalidOperationException("Rendered completion list has no common visual root with gallery.");
        return FormattableString.Invariant($"{{\"x\":{p.X},\"y\":{p.Y},\"width\":{list.Bounds.Width},\"height\":{list.Bounds.Height}}}");
    }
    [JSExport]
    public static async Task<string> Probe(string command)
    {
        var editor=View.Editor;
        switch(command)
        {
            case "start-completion":
                _originalCompletion=editor.CompletionProvider;_deferredCompletion=new();editor.CompletionProvider=_deferredCompletion;_pendingCompletion=editor.ShowCompletionAsync();break;
            case "finish-completion":
                _deferredCompletion!.Source.TrySetResult([new Completion("stale-result","WRONG")]);
                await (_pendingCompletion??Task.CompletedTask);editor.CompletionProvider=_originalCompletion;break;
            case "start-diagnostics":
                _originalDiagnostics=editor.DiagnosticProvider;_deferredDiagnostics=new();editor.DiagnosticProvider=_deferredDiagnostics;_pendingDiagnostics=editor.RefreshAnalysisAsync();
                editor.DiagnosticProvider=null;break;
            case "finish-diagnostics":
                _deferredDiagnostics!.Source.TrySetResult([new Diagnostic(0,1,"stale")]);
                await (_pendingDiagnostics??Task.CompletedTask);editor.DiagnosticProvider=_originalDiagnostics;break;
            default:throw new ArgumentException("Unknown probe command.");
        }
        return State();
    }
    // Providers deliberately ignore cancellation so the consumer's revision/caret guards are exercised.
    private sealed class DeferredCompletion:ICompletionProvider
    {
        public TaskCompletionSource<IReadOnlyList<Completion>> Source {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<IReadOnlyList<Completion>> CompleteAsync(CompletionRequest request,CancellationToken token)=>new(Source.Task);
    }
    private sealed class DeferredDiagnostics:IDiagnosticProvider
    {
        public TaskCompletionSource<IReadOnlyList<Diagnostic>> Source {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<IReadOnlyList<Diagnostic>> AnalyzeAsync(TextSnapshot snapshot,long revision,CancellationToken token)=>new(Source.Task);
    }
}
