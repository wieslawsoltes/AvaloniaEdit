using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using NUnit.Framework;
using UnoEdit.CodeCompletion;
using UnoEdit.Document;
using UnoEdit.Editing;
using UnoEdit.Rendering;
using UnoEdit.Snippets;

namespace UnoEdit.Uno.Tests;

[TestFixture]
public sealed class CompletionSnippetTests
{
    private TextEditor _editor;
    [SetUp]
    public void Setup()
    {
        Assert.That(TestApp.Root.DispatcherQueue.HasThreadAccess, Is.True, "Tests must execute on the actual Uno UI thread.");
        _editor = new TextEditor { Width = 600, Height = 300 };
        TestApp.Root.Children.Add(_editor);
        _editor.Measure(new Windows.Foundation.Size(600, 300));
        _editor.Arrange(new Windows.Foundation.Rect(0, 0, 600, 300));
    }
    [TearDown]
    public void Cleanup()
    {
        TestApp.Root.Children.Remove(_editor);
        _editor.Dispose();
    }
    [Test]
    public void CompletionRanksExactPrefixSubstringAndCamelCase()
    {
        var list = new CompletionList();
        using (list.DeferRefresh())
            foreach (var word in new[] { "WriteLine", "While", "WideLength", "wl", "AWL" }) list.CompletionData.Add(new Data(word));
        list.SelectItem("wl");
        Assert.That(list.SelectedItem.Text, Is.EqualTo("wl"));
        Assert.That(list.CurrentList.Select(x => x.Text), Does.Contain("WriteLine"));
        list.SelectItem("Write");
        Assert.That(list.SelectedItem.Text, Is.EqualTo("WriteLine"));
        list.CompletionData.Add(new Data("Writer")); list.SelectItem("Write");
        Assert.That(list.CurrentList.Count, Is.EqualTo(2));
        list.CompletionData.Clear(); list.SelectItem("Write");
        Assert.That(list.CurrentList, Is.Empty);
    }
    [Test]
    public void NonFilteringCompletionKeepsAllItemsAndSelectsBest()
    {
        var list = new CompletionList { IsFiltering = false };
        list.CompletionData.Add(new Data("Alpha")); list.CompletionData.Add(new Data("Beta"));
        list.SelectItem("B");
        Assert.That(list.CurrentList.Count, Is.EqualTo(2));
        Assert.That(list.SelectedItem.Text, Is.EqualTo("Beta"));
    }
    [Test]
    public void CompletionClosesBeforeClientStartsSnippet()
    {
        _editor.Text = "pre"; _editor.CaretOffset = 3;
        using var popup = new CompletionWindow(_editor.TextArea) { StartOffset = 0, EndOffset = 3 };
        var called = false;
        popup.CompletionList.CompletionData.Add(new Data("prefix", (area, segment) =>
        {
            Assert.That(popup.IsOpen, Is.False);
            area.Selection = Selection.Create(area, segment);
            var snippet = new Snippet(); snippet.Elements.Add(new SnippetReplaceableTextElement { Text = "value" });
            snippet.Insert(area); called = true;
        }));
        popup.Show();
        Assert.That(popup.IsOpen, Is.True);
        popup.CompletionList.RequestInsertion(EventArgs.Empty);
        Assert.That(called, Is.True);
        Assert.That(_editor.Text, Is.EqualTo("value"));
        Assert.That(_editor.TextArea.StackedInputHandlers.Count(), Is.EqualTo(1), "New snippet handler survives completion teardown.");
        Assert.That(_editor.SelectedText, Is.EqualTo("value"));
    }
    [Test]
    public void CompletionTracksEditsAndDetachesOnDocumentReplacement()
    {
        _editor.Text = "ab"; _editor.CaretOffset = 2;
        using var popup = new CompletionWindow(_editor.TextArea) { StartOffset = 0, EndOffset = 2 };
        popup.CompletionList.CompletionData.Add(new Data("abcd")); popup.Show();
        _editor.TextArea.ReplaceSelectionWithText("c");
        Assert.That(popup.EndOffset, Is.EqualTo(3));
        Assert.That(popup.IsOpen, Is.True);
        _editor.Document = new TextDocument("replacement");
        Assert.That(popup.IsOpen, Is.False);
        Assert.That(_editor.TextArea.StackedInputHandlers, Is.Empty);
    }
    [Test]
    public void CompletionCannotReplaceAProtectedRange()
    {
        _editor.Text = "ab"; _editor.CaretOffset = 2;
        var provider = new TextSegmentReadOnlySectionProvider<TextSegment>(_editor.Document);
        provider.Segments.Add(new TextSegment { StartOffset = 0, Length = 2 });
        _editor.TextArea.ReadOnlySectionProvider = provider;
        using var popup = new CompletionWindow(_editor.TextArea) { StartOffset = 0, EndOffset = 2 };
        var called = false; popup.CompletionList.CompletionData.Add(new Data("abc", (_, _) => called = true));
        popup.Show(); popup.CompletionList.RequestInsertion(EventArgs.Empty);
        Assert.That(called, Is.False); Assert.That(_editor.Text, Is.EqualTo("ab"));
    }
    [Test]
    public void PopupDisposeAndReopenReleaseStackedHandlers()
    {
        using var popup = new CompletionWindow(_editor.TextArea);
        popup.CompletionList.CompletionData.Add(new Data("word"));
        popup.Show(); popup.Hide(); popup.Show();
        Assert.That(_editor.TextArea.StackedInputHandlers.Count(), Is.EqualTo(1));
        popup.Dispose(); popup.Dispose();
        Assert.That(_editor.TextArea.StackedInputHandlers, Is.Empty);
        Assert.Throws<ObjectDisposedException>(popup.Show);
    }
    [Test]
    public void SnippetLinksEditInOneUndoGroupAndCaretPreservesSelection()
    {
        var snippet = new Snippet(); var primary = new SnippetReplaceableTextElement { Text = "name" };
        snippet.Elements.Add(primary); snippet.Elements.Add(new SnippetTextElement { Text = " = " });
        snippet.Elements.Add(new SnippetBoundElement { TargetElement = primary }); snippet.Elements.Add(new SnippetCaretElement());
        var context = snippet.Insert(_editor.TextArea);
        Assert.That(_editor.Text, Is.EqualTo("name = name"));
        Assert.That(_editor.SelectedText, Is.EqualTo("name"));
        _editor.TextArea.Caret.Offset = 4;
        Assert.That(_editor.SelectedText, Is.EqualTo("name"));
        _editor.TextArea.ReplaceSelectionWithText("item");
        Assert.That(_editor.Text, Is.EqualTo("item = item"));
        _editor.Undo(); Assert.That(_editor.Text, Is.EqualTo("name = name"));
        context.Deactivate(new SnippetEventArgs(DeactivateReason.ReturnPressed));
        Assert.That(_editor.TextArea.StackedInputHandlers, Is.Empty);
        Assert.That(_editor.TextArea.TextView.BackgroundRenderers, Is.Empty);
    }
    [Test]
    public void UndoingSnippetInsertionDetachesInteractiveState()
    {
        var snippet = new Snippet(); snippet.Elements.Add(new SnippetReplaceableTextElement { Text = "value" });
        snippet.Insert(_editor.TextArea);
        _editor.Undo(); Assert.That(_editor.Text, Is.Empty);
        Assert.That(_editor.TextArea.StackedInputHandlers, Is.Empty);
        Assert.That(_editor.TextArea.TextView.BackgroundRenderers, Is.Empty);
    }
    [Test]
    public void SnippetCannotOverwriteProtectedSelection()
    {
        _editor.Text = "protected"; _editor.SelectAll();
        _editor.TextArea.ReadOnlySectionProvider = new TextSegmentReadOnlySectionProvider<TextSegment>(_editor.Document);
        ((TextSegmentReadOnlySectionProvider<TextSegment>)_editor.TextArea.ReadOnlySectionProvider).Segments.Add(new TextSegment { StartOffset = 0, Length = 9 });
        var snippet = new Snippet(); snippet.Elements.Add(new SnippetTextElement { Text = "bad" });
        Assert.Throws<InvalidOperationException>(() => snippet.Insert(_editor.TextArea));
        Assert.That(_editor.Text, Is.EqualTo("protected")); Assert.That(_editor.CanUndo, Is.False);
    }
    [Test]
    public void BoundSnippetStopsRatherThanMutatingNewlyProtectedMirror()
    {
        var snippet = new Snippet(); var primary = new SnippetReplaceableTextElement { Text = "one" };
        snippet.Elements.Add(primary); snippet.Elements.Add(new SnippetTextElement { Text = " / " });
        snippet.Elements.Add(new SnippetBoundElement { TargetElement = primary });
        snippet.Insert(_editor.TextArea);
        var provider = new TextSegmentReadOnlySectionProvider<TextSegment>(_editor.Document);
        provider.Segments.Add(new TextSegment { StartOffset = 6, Length = 3 }); _editor.TextArea.ReadOnlySectionProvider = provider;
        _editor.SelectedText = "two";
        Assert.That(_editor.Text, Is.EqualTo("two / one"));
        Assert.That(_editor.TextArea.StackedInputHandlers, Is.Empty);
    }
    [Test]
    public void StackedHandlersDetachInReverseOrder()
    {
        var log = new List<int>(); var first = new Handler(_editor.TextArea, 1, log); var second = new Handler(_editor.TextArea, 2, log);
        _editor.TextArea.PushStackedInputHandler(first); _editor.TextArea.PushStackedInputHandler(second);
        _editor.TextArea.PopStackedInputHandler(first);
        Assert.That(log, Is.EqualTo(new[] { 1, 2, -2, -1 }));
    }
    [Test]
    public void NestedInputHandlerCyclesAreRejected()
    {
        var first = new TextAreaInputHandler(_editor.TextArea); var second = new TextAreaInputHandler(_editor.TextArea);
        first.NestedInputHandlers.Add(second);
        Assert.Throws<ArgumentException>(() => second.NestedInputHandlers.Add(first));
    }
    [Test]
    public void CommandsHonorCanExecuteAndCustomHandlerLifetime()
    {
        var command = new RoutedCommand("Test"); var count = 0; var allowed = true;
        var handler = new TextAreaInputHandler(_editor.TextArea);
        handler.CommandBindings.Add(new RoutedCommandBinding(command, (_, _) => count++, (_, args) => args.CanExecute = allowed));
        _editor.TextArea.ActiveInputHandler = handler;
        command.Execute(null, _editor.TextArea); allowed = false; command.Execute(null, _editor.TextArea);
        Assert.That(count, Is.EqualTo(1)); Assert.That(command.CanExecute(null, _editor.TextArea), Is.False);
        _editor.TextArea.ActiveInputHandler = _editor.TextArea.DefaultInputHandler;
        Assert.That(_editor.TextArea.CommandBindings.Any(x => x.Command == command), Is.False);
    }
    [Test]
    public void OverloadsWrapAndObserveProviderReplacement()
    {
        using var viewer = new OverloadViewer(); var provider = new Overloads();
        viewer.Provider = provider; viewer.ChangeIndex(-1); Assert.That(provider.SelectedIndex, Is.EqualTo(2));
        viewer.ChangeIndex(1); Assert.That(provider.SelectedIndex, Is.Zero);
        viewer.Provider = new Overloads(); Assert.That(provider.Subscribers, Is.Zero);
        viewer.Dispose(); Assert.That(((Overloads)viewer.Provider).Subscribers, Is.Zero);
    }
    private sealed class Data : ICompletionData
    {
        private readonly Action<TextArea, ISegment> _complete;
        public Data(string text, Action<TextArea, ISegment> complete = null) { Text = text; _complete = complete; }
        public ImageSource Image => null;
        public string Text { get; }
        public object Content => Text;
        public object Description => "Description " + Text;
        public double Priority => 0;
        public void Complete(TextArea area, ISegment segment, EventArgs e) => _complete?.Invoke(area, segment);
    }
    private sealed class Handler : TextAreaStackedInputHandler
    {
        private readonly int _id; private readonly List<int> _log;
        public Handler(TextArea area, int id, List<int> log) : base(area) { _id = id; _log = log; }
        public override void Attach() => _log.Add(_id);
        public override void Detach() => _log.Add(-_id);
    }
    private sealed class Overloads : IOverloadProvider
    {
        private PropertyChangedEventHandler _changed; private int _index;
        public event PropertyChangedEventHandler PropertyChanged { add => _changed += value; remove => _changed -= value; }
        public int Subscribers => _changed?.GetInvocationList().Length ?? 0;
        public int SelectedIndex { get => _index; set { _index = value; _changed?.Invoke(this, new PropertyChangedEventArgs(null)); } }
        public int Count => 3;
        public string CurrentIndexText => $"{_index + 1} of 3";
        public object CurrentHeader => "Header";
        public object CurrentContent => "Content";
    }
}
