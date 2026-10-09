using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using UnoEdit.Document;
using UnoEdit.Editing;

namespace UnoEdit.CodeCompletion;

public class CompletionWindow : CompletionWindowBase
{
    private readonly ContentPresenter _description = new() { Margin = new Thickness(8), MaxWidth = 340 };
    public CompletionWindow(TextArea textArea) : base(textArea)
    {
        CompletionList = new CompletionList { CompletionAcceptAction = textArea.Options.CompletionAcceptAction, MinWidth = 190, MaxHeight = 240 };
        CompletionList.InsertionRequested += OnInsertionRequested;
        CompletionList.SelectionChanged += OnSelectionChanged;
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.Children.Add(CompletionList); Grid.SetColumn(_description, 1); layout.Children.Add(_description);
        Child = layout; MinWidth = 190; MinHeight = 20; MaxWidth = 600; MaxHeight = 260;
    }
    public CompletionList CompletionList { get; }
    public bool CloseAutomatically { get; set; } = true;
    public bool CloseWhenCaretAtBeginning { get; set; }
    protected override bool CloseOnFocusLost => CloseAutomatically;
    protected override void OnOpened() { base.OnOpened(); CompletionList.SelectItem(TextArea.Document.GetText(StartOffset, Math.Max(0, TextArea.Caret.Offset - StartOffset))); }
    protected override void OnCaretChanged()
    {
        var offset = TextArea.Caret.Offset;
        if (offset < StartOffset || offset > EndOffset || CloseWhenCaretAtBeginning && offset == StartOffset)
        { if (CloseAutomatically) Hide(); return; }
        CompletionList.SelectItem(TextArea.Document.GetText(StartOffset, offset - StartOffset));
        if (CompletionList.CurrentList.Count == 0 && CloseAutomatically) Hide();
    }
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _description.Content = CompletionList.SelectedItem?.Description;
        _description.Visibility = _description.Content == null ? Visibility.Collapsed : Visibility.Visible;
        if (IsOpen) UpdatePosition();
    }
    private void OnInsertionRequested(object sender, EventArgs e)
    {
        var item = CompletionList.SelectedItem;
        var document = TextArea.Document; var version = document.Version;
        var segment = new SimpleSegment(StartOffset, EndOffset - StartOffset);
        var allowed = !TextArea.IsReadOnly && (segment.Length == 0 ? TextArea.ReadOnlySectionProvider.CanInsert(segment.Offset) : IsWholeEditable(segment));
        if (!ReferenceEquals(document, TextArea.Document) || document.Version.CompareAge(version) != 0) throw new InvalidOperationException("The document changed while completion protection was being checked.");
        // Close before invoking client code: completion can start a snippet,
        // whose newly pushed input handler must not be popped with this popup.
        Hide();
        if (allowed && item != null) item.Complete(TextArea, new AnchorSegment(document, segment.Offset, segment.Length), e);
    }
    private bool IsWholeEditable(ISegment segment)
    { var ranges = TextArea.GetDeletableSegments(segment); return ranges.Length == 1 && ranges[0].Offset == segment.Offset && ranges[0].Length == segment.Length; }
    protected override void OnKeyDown(KeyRoutedEventArgs e) { base.OnKeyDown(e); if (!e.Handled) CompletionList.HandleKey(e); }
}
