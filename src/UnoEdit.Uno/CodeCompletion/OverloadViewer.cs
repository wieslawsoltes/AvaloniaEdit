using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using UnoEdit.Editing;
using Windows.System;

namespace UnoEdit.CodeCompletion;

public class OverloadViewer : Control, IDisposable
{
    private IOverloadProvider _subscribed;
    private Button _previous, _next;
    private bool _disposed;
    public OverloadViewer() { DefaultStyleKey = typeof(OverloadViewer); IsTabStop = false; }
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(OverloadViewer), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ProviderProperty = DependencyProperty.Register(nameof(Provider), typeof(IOverloadProvider), typeof(OverloadViewer), new PropertyMetadata(null, (o, _) => ((OverloadViewer)o).Subscribe()));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public IOverloadProvider Provider { get => (IOverloadProvider)GetValue(ProviderProperty); set => SetValue(ProviderProperty, value); }
    private void Subscribe()
    {
        if (_subscribed != null) _subscribed.PropertyChanged -= OnProviderChanged;
        _subscribed = _disposed ? null : Provider;
        if (_subscribed != null) _subscribed.PropertyChanged += OnProviderChanged;
        Refresh();
    }
    private void OnProviderChanged(object sender, PropertyChangedEventArgs e)
    { if (DispatcherQueue.HasThreadAccess) Refresh(); else DispatcherQueue.TryEnqueue(() => { if (!_disposed) Refresh(); }); }
    protected override void OnApplyTemplate()
    {
        DetachButtons(); base.OnApplyTemplate();
        _previous = GetTemplateChild("PART_previous") as Button;
        _next = GetTemplateChild("PART_next") as Button;
        if (_previous != null) _previous.Click += Previous;
        if (_next != null) _next.Click += Next;
        Refresh();
    }
    private void Previous(object sender, RoutedEventArgs e) => ChangeIndex(-1);
    private void Next(object sender, RoutedEventArgs e) => ChangeIndex(1);
    public void ChangeIndex(int relativeIndexChange)
    {
        var provider = Provider; if (provider == null || provider.Count == 0) return;
        var count = provider.Count;
        provider.SelectedIndex = (int)(((long)provider.SelectedIndex + relativeIndexChange) % count + count) % count;
        Refresh();
    }
    private void Refresh()
    {
        var valid = !_disposed && Provider != null && Provider.Count > 0;
        Text = valid ? Provider.CurrentIndexText : string.Empty;
        if (GetTemplateChild("PART_header") is ContentPresenter header) header.Content = valid ? Provider.CurrentHeader : null;
        if (GetTemplateChild("PART_content") is ContentPresenter content) content.Content = valid ? Provider.CurrentContent : null;
        if (_previous != null) _previous.IsEnabled = valid && Provider.Count > 1;
        if (_next != null) _next.IsEnabled = valid && Provider.Count > 1;
    }
    private void DetachButtons() { if (_previous != null) _previous.Click -= Previous; if (_next != null) _next.Click -= Next; }
    public new void Dispose() { if (_disposed) return; _disposed = true; DetachButtons(); Subscribe(); base.Dispose(); }
}
public class InsightWindow : CompletionWindowBase
{
    public InsightWindow(TextArea textArea) : base(textArea) { MinWidth = 200; MaxWidth = 600; MaxHeight = 300; }
    public bool CloseAutomatically { get; set; } = true;
    protected override bool CloseOnFocusLost => CloseAutomatically;
    protected override void OnCaretChanged() { if (CloseAutomatically && (TextArea.Caret.Offset < StartOffset || TextArea.Caret.Offset > EndOffset)) Hide(); }
}
public class OverloadInsightWindow : InsightWindow
{
    private readonly OverloadViewer _viewer = new();
    public OverloadInsightWindow(TextArea textArea) : base(textArea) => Child = _viewer;
    public IOverloadProvider Provider { get => _viewer.Provider; set => _viewer.Provider = value; }
    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Provider?.Count <= 1) return;
        if (e.Key == VirtualKey.Up || e.Key == VirtualKey.Down) { _viewer.ChangeIndex(e.Key == VirtualKey.Up ? -1 : 1); e.Handled = true; UpdatePosition(); }
    }
}
public sealed class CollapseIfSingleOverloadConverter : IValueConverter
{
    public static CollapseIfSingleOverloadConverter Instance { get; } = new();
    public object Convert(object value, Type targetType, object parameter, string language) => value is int count && count > 1 ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException("Overload visibility is one-way.");
}
