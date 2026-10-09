using System;
using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using UnoEdit.CodeCompletion;
using UnoEdit.Document;
using UnoEdit.Editing;
using UnoEdit.Snippets;
using Windows.System;

namespace UnoEdit.Uno.Demo;

public sealed partial class DemoPage
{
    private CompletionWindow _completion;
    private OverloadInsightWindow _insight;
    private bool _snippetActive;
    private void InitializeEditingFeatures(Panel toolbar)
    {
        AddButton(toolbar, "Complete", ShowCompletion);
        AddButton(toolbar, "Overloads", ShowOverloads);
        _editor.TextArea.CommandBindings.Add(new RoutedCommandBinding(new RoutedCommand("DemoCompletion", new KeyGesture(VirtualKey.Space, VirtualKeyModifiers.Control)), (_, _) => ShowCompletion()));
        _editor.TextArea.CommandBindings.Add(new RoutedCommandBinding(new RoutedCommand("DemoOverloads", new KeyGesture(VirtualKey.Space, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift)), (_, _) => ShowOverloads()));
    }
    private void ShowCompletion()
    {
        _completion?.Dispose();
        var area = _editor.TextArea; var end = area.Caret.Offset; var start = end;
        while (start > 0 && (char.IsLetterOrDigit(area.Document.GetCharAt(start - 1)) || area.Document.GetCharAt(start - 1) == '_')) start--;
        var window = new CompletionWindow(area) { StartOffset = start, EndOffset = end };
        _completion = window;
        using (window.CompletionList.DeferRefresh())
        {
            foreach (var word in new[] { "Console", "Control", "class", "for", "WriteLine", "Write", "Window" })
                window.CompletionList.CompletionData.Add(new SampleCompletion(word, word == "for" ? InsertLoopSnippet : null));
        }
        window.Closed += (_, _) => { if (ReferenceEquals(_completion, window)) _completion = null; window.Dispose(); QueueDiagnostics(); };
        window.CompletionList.SelectionChanged += (_, _) => QueueDiagnostics();
        window.Show(); QueueDiagnostics();
    }
    private void InsertLoopSnippet(TextArea area, ISegment completion)
    {
        area.Selection = Selection.Create(area, completion);
        var snippet = new Snippet(); var variable = new SnippetReplaceableTextElement { Text = "i" };
        snippet.Elements.Add(new SnippetTextElement { Text = "for (int " });
        snippet.Elements.Add(variable);
        snippet.Elements.Add(new SnippetTextElement { Text = " = 0; " });
        snippet.Elements.Add(new SnippetBoundElement { TargetElement = variable });
        snippet.Elements.Add(new SnippetTextElement { Text = " < " });
        snippet.Elements.Add(new SnippetReplaceableTextElement { Text = "count" });
        snippet.Elements.Add(new SnippetTextElement { Text = "; " });
        snippet.Elements.Add(new SnippetBoundElement { TargetElement = variable });
        snippet.Elements.Add(new SnippetTextElement { Text = "++)\n{\n\t" });
        snippet.Elements.Add(new SnippetCaretElement());
        snippet.Elements.Add(new SnippetTextElement { Text = "\n}" });
        var context = snippet.Insert(area); _snippetActive = true;
        context.Deactivated += (_, _) => { _snippetActive = false; QueueDiagnostics(); };
        QueueDiagnostics();
    }
    private void ShowOverloads()
    {
        _insight?.Dispose();
        var window = new OverloadInsightWindow(_editor.TextArea) { Provider = new SampleOverloads() };
        _insight = window;
        window.Provider.PropertyChanged += (_, _) => QueueDiagnostics();
        window.Closed += (_, _) => { if (ReferenceEquals(_insight, window)) _insight = null; window.Dispose(); QueueDiagnostics(); };
        window.Show(); QueueDiagnostics();
    }
    private sealed class SampleCompletion : ICompletionData
    {
        private readonly Action<TextArea, ISegment> _insert;
        public SampleCompletion(string text, Action<TextArea, ISegment> insert) { Text = text; _insert = insert; }
        public ImageSource Image => null;
        public string Text { get; }
        public object Content => Text;
        public object Description => Text == "for" ? "Insert a linked for-loop snippet. Tab moves between editable placeholders; Enter finishes." : "Native Uno completion item: " + Text;
        public double Priority => 0;
        public void Complete(TextArea area, ISegment segment, EventArgs args)
        {
            if (_insert != null) { _insert(area, segment); return; }
            using (area.Document.RunUpdate()) area.Document.Replace(segment.Offset, segment.Length, Text);
            area.Caret.Offset = segment.Offset + Text.Length;
            area.ClearSelection();
        }
    }
    private sealed class SampleOverloads : IOverloadProvider
    {
        private int _index;
        public event PropertyChangedEventHandler PropertyChanged;
        public int SelectedIndex { get => _index; set { _index = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); } }
        public int Count => 3;
        public string CurrentIndexText => $"{_index + 1} of {Count}";
        public object CurrentHeader => _index switch { 0 => "WriteLine()", 1 => "WriteLine(string value)", _ => "WriteLine(string format, params object[] args)" };
        public object CurrentContent => "Use Up and Down to inspect overloads. Escape closes this native popup.";
    }
}
