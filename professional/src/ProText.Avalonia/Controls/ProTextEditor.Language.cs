using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Folding;
using ProText.Core;
namespace ProText.Avalonia.Controls;
public partial class ProTextEditor
{
    private CancellationTokenSource? _completionCancellation,_diagnosticCancellation;
    private CompletionWindow? _completion;
    public int CompletionCount=>_completion?.CompletionList.CompletionData.Count??0;
    public async Task RefreshAnalysisAsync()
    {
        if(_disposed||Session is null) return;
        ApplyOptions();
        if(IsLargeFile) { LastMessage="Large-file mode: lexical folding and diagnostic analysis are paused."; return; }
        var session=Session; long revision=session.Revision; string text=session.Text;
        if(_folding is not null && FoldingProvider is not null)
        {
            try { _folding.UpdateFoldings(FoldingProvider.GetFoldings(text).Select(f=>new NewFolding(f.StartOffset,f.EndOffset) { Name=f.Name }),-1); }
            catch(Exception ex) { ReportServiceError(ex); }
        }
        if(DiagnosticProvider is not null)
        {
            CancelDiagnostics(); var cancellation=new CancellationTokenSource(); _diagnosticCancellation=cancellation;
            try
            {
                var diagnostics=await DiagnosticProvider.GetDiagnosticsAsync(text,revision,cancellation.Token);
                if(!cancellation.IsCancellationRequested && Session==session) session.SetDiagnostics(revision,diagnostics);
            }
            catch(OperationCanceledException) { }
            catch(Exception ex) { ReportServiceError(ex); }
        }
        Redraw();
    }
    public async Task ShowCompletionAsync()
    {
        if(_disposed||EffectiveReadOnly||CompletionProvider is null||Session is null||IsLargeFile) return;
        CancelCompletion(); var cancellation=new CancellationTokenSource(); _completionCancellation=cancellation;
        var session=Session; var request=new CompletionRequest(session.Text,Editor.CaretOffset,session.Revision);
        try
        {
            var items=await CompletionProvider.GetCompletionsAsync(request,cancellation.Token);
            if(cancellation.IsCancellationRequested||Session!=session||session.Revision!=request.Revision||Editor.CaretOffset!=request.Offset||items.Count==0) return;
            var window=new CompletionWindow(Editor.TextArea) { StartOffset=request.WordRange.Offset,EndOffset=request.Offset };
            foreach(var item in items.Take(200)) window.CompletionList.CompletionData.Add(new CompletionData(item));
            _completion=window; window.Closed+=(_,_)=>{ if(_completion==window) _completion=null; };
            window.Show(); window.CompletionList.SelectItem(request.Prefix);
            if(window.CompletionList.SelectedItem is null) window.CompletionList.SelectedItem=window.CompletionList.CompletionData[0];
        }
        catch(OperationCanceledException) { }
        catch(Exception ex) { ReportServiceError(ex); }
    }
    public void AcceptCompletion()=>_completion?.CompletionList.RequestInsertion(EventArgs.Empty);
    private void CancelCompletion()
    {
        _completionCancellation?.Cancel(); _completionCancellation?.Dispose(); _completionCancellation=null;
        var window=_completion; _completion=null; window?.Close();
    }
    private void CancelDiagnostics() { _diagnosticCancellation?.Cancel(); _diagnosticCancellation?.Dispose(); _diagnosticCancellation=null; }
    private sealed class CompletionData(CompletionItem item) : ICompletionData
    {
        public IImage? Image=>null;
        public string Text=>item.Label;
        public object Content=>item.Label;
        public object Description=>item.Description??item.Label;
        public double Priority=>item.Priority;
        public void Complete(TextArea textArea,ISegment completionSegment,EventArgs insertionRequestEventArgs)=>textArea.Document.Replace(completionSegment,item.InsertText);
    }
}
