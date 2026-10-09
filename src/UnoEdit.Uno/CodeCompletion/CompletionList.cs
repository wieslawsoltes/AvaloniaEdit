using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace UnoEdit.CodeCompletion;

public sealed class CompletionRequestEventArgs : EventArgs
{
    public CompletionRequestEventArgs(object originalEventArgs) => OriginalEventArgs = originalEventArgs;
    public object OriginalEventArgs { get; }
}

/// <summary>Templated, virtualized native completion list using the original matcher.</summary>
public class CompletionList : Control
{
    private readonly ObservableCollection<ICompletionData> _data = new();
    private List<ICompletionData> _currentList = new();
    private CompletionListBox _listBox;
    private string _query = string.Empty;
    private bool _isFiltering = true;
    private int _deferRefresh;
    private bool _pointerPressed;
    private long _version, _filteredVersion = -1;
    private ICompletionData _selected;
    public CompletionList()
    {
        DefaultStyleKey = typeof(CompletionList);
        IsTabStop = false;
        _data.CollectionChanged += OnDataChanged;
    }
    public bool IsFiltering
    {
        get => _isFiltering;
        set { if (_isFiltering == value) return; _isFiltering = value; _filteredVersion = -1; SelectItem(_query); }
    }
    public static readonly DependencyProperty EmptyTemplateProperty = DependencyProperty.Register(nameof(EmptyTemplate), typeof(ControlTemplate), typeof(CompletionList), new PropertyMetadata(null));
    public ControlTemplate EmptyTemplate { get => (ControlTemplate)GetValue(EmptyTemplateProperty); set => SetValue(EmptyTemplateProperty, value); }
    public CompletionAcceptAction CompletionAcceptAction { get; set; } = CompletionAcceptAction.DoubleTapped;
    public VirtualKey[] CompletionAcceptKeys { get; set; } = new[] { VirtualKey.Enter, VirtualKey.Tab };
    public IList<ICompletionData> CompletionData => _data;
    public List<ICompletionData> CurrentList => new(_currentList);
    public CompletionListBox ListBox { get { if (_listBox == null) ApplyTemplate(); return _listBox; } }
    public ScrollViewer ScrollViewer => _listBox?.ScrollViewer;
    public ICompletionData SelectedItem
    {
        get => _listBox?.SelectedItem as ICompletionData ?? _selected;
        set { _selected = value; if (_listBox != null) _listBox.SelectedItem = value; }
    }
    public event EventHandler InsertionRequested;
    public event EventHandler<SelectionChangedEventArgs> SelectionChanged;
    public void RequestInsertion(EventArgs e) { if (SelectedItem != null) InsertionRequested?.Invoke(this, e ?? EventArgs.Empty); }
    public void ScrollIntoView(ICompletionData item) => ListBox?.ScrollIntoView(item);
    protected override void OnApplyTemplate()
    {
        if (_listBox != null)
        {
            _listBox.SelectionChanged -= OnSelectionChanged;
            _listBox.RemoveHandler(PointerPressedEvent, (PointerEventHandler)OnPointerPressed);
            _listBox.RemoveHandler(PointerReleasedEvent, (PointerEventHandler)OnPointerReleased);
            _listBox.RemoveHandler(DoubleTappedEvent, (DoubleTappedEventHandler)OnDoubleTapped);
        }
        base.OnApplyTemplate();
        _listBox = GetTemplateChild("PART_ListBox") as CompletionListBox;
        if (_listBox == null) return;
        _listBox.ItemsSource = _currentList;
        _listBox.SelectedItem = _selected;
        _listBox.SelectionChanged += OnSelectionChanged;
        _listBox.AddHandler(PointerPressedEvent, (PointerEventHandler)OnPointerPressed, true);
        _listBox.AddHandler(PointerReleasedEvent, (PointerEventHandler)OnPointerReleased, true);
        _listBox.AddHandler(DoubleTappedEvent, (DoubleTappedEventHandler)OnDoubleTapped, true);
        _filteredVersion = -1;
        SelectItem(_query);
    }
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    { _selected = _listBox?.SelectedItem as ICompletionData; SelectionChanged?.Invoke(this, e); }
    private bool SelectPointerItem(object source)
    {
        for (var node = source as DependencyObject; node != null && !ReferenceEquals(node, _listBox); node = VisualTreeHelper.GetParent(node))
            if (node is ListViewItem item && _listBox.ItemFromContainer(item) is ICompletionData entry)
            { SelectedItem = entry; return true; }
        return false;
    }
    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _pointerPressed = e.GetCurrentPoint(_listBox).Properties.IsLeftButtonPressed;
        if (CompletionAcceptAction != CompletionAcceptAction.PointerPressed || !_pointerPressed || !SelectPointerItem(e.OriginalSource)) return;
        e.Handled = true; RequestInsertion(new CompletionRequestEventArgs(e));
    }
    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var wasPressed = _pointerPressed; _pointerPressed = false;
        if (CompletionAcceptAction != CompletionAcceptAction.PointerReleased || !wasPressed || !SelectPointerItem(e.OriginalSource)) return;
        e.Handled = true; RequestInsertion(new CompletionRequestEventArgs(e));
    }
    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (CompletionAcceptAction != CompletionAcceptAction.DoubleTapped || !SelectPointerItem(e.OriginalSource)) return;
        e.Handled = true; RequestInsertion(new CompletionRequestEventArgs(e));
    }
    /// <summary>Coalesces bulk list additions into one filter/layout update.</summary>
    public IDisposable DeferRefresh()
    {
        _deferRefresh++;
        return new UnoEdit.Utils.CallbackOnDispose(() => { if (--_deferRefresh == 0) SelectItem(_query); });
    }
    private void OnDataChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        _version++;
        if (_deferRefresh == 0 && _listBox != null) SelectItem(_query);
    }
    public void SelectItem(string text)
    {
        if (text == null) throw new ArgumentNullException(nameof(text));
        if (_filteredVersion == _version && _query == text) return;
        var suggested = SelectedItem;
        IEnumerable<ICompletionData> candidates = _isFiltering && _filteredVersion == _version && _query.Length > 0 && text.StartsWith(_query, StringComparison.Ordinal) ? _currentList : _data;
        var matching = new List<ICompletionData>();
        ICompletionData best = null; var bestQuality = -1; var bestPriority = double.NegativeInfinity;
        foreach (var item in candidates)
        {
            if (item == null) throw new InvalidOperationException("CompletionData must not contain null entries.");
            var quality = CompletionMatcher.GetMatchQuality(item.Text, text, _isFiltering);
            if (!_isFiltering || quality > 0) matching.Add(item);
            if (quality < 0) continue;
            var priority = ReferenceEquals(item, suggested) ? double.PositiveInfinity : double.IsNaN(item.Priority) ? 0 : item.Priority;
            if (quality > bestQuality || quality == bestQuality && priority > bestPriority) { best = item; bestQuality = quality; bestPriority = priority; }
        }
        _currentList = matching; _selected = best; _query = text; _filteredVersion = _version;
        if (_listBox != null) { _listBox.ItemsSource = matching; _listBox.SelectedItem = best; if (best != null) _listBox.ScrollIntoView(best); }
        if (GetTemplateChild("PART_Empty") is Control empty)
        { empty.Template = EmptyTemplate; empty.Visibility = matching.Count == 0 && EmptyTemplate != null ? Visibility.Visible : Visibility.Collapsed; }
    }
    public void HandleKey(KeyRoutedEventArgs e)
    {
        if (e == null) throw new ArgumentNullException(nameof(e));
        if (_currentList.Count == 0) return;
        var index = _currentList.IndexOf(SelectedItem);
        var step = Math.Max(1, _listBox?.VisibleItemCount ?? 8);
        switch (e.Key)
        {
            case VirtualKey.Down: index = (index + 1) % _currentList.Count; break;
            case VirtualKey.Up: index = index <= 0 ? _currentList.Count - 1 : index - 1; break;
            case VirtualKey.PageDown: index = Math.Min(_currentList.Count - 1, index + step); break;
            case VirtualKey.PageUp: index = Math.Max(0, index - step); break;
            case VirtualKey.Home: index = 0; break;
            case VirtualKey.End: index = _currentList.Count - 1; break;
            default:
                if (CompletionAcceptKeys?.Contains(e.Key) == true) { e.Handled = true; RequestInsertion(new CompletionRequestEventArgs(e)); }
                return;
        }
        e.Handled = true; SelectedItem = _currentList[index]; ScrollIntoView(SelectedItem);
    }
    protected override void OnKeyDown(KeyRoutedEventArgs e) { base.OnKeyDown(e); if (!e.Handled) HandleKey(e); }
}

public class CompletionListBox : ListView
{
    public CompletionListBox() { IsTabStop = false; SelectionMode = ListViewSelectionMode.Single; }
    public ScrollViewer ScrollViewer { get; private set; }
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate(); ScrollViewer = FindScrollViewer(this);
    }
    private static ScrollViewer FindScrollViewer(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); if (child is ScrollViewer viewer) return viewer; var descendant = FindScrollViewer(child); if (descendant != null) return descendant; }
        return null;
    }
    public int ItemCount => Items.Count;
    public int FirstVisibleItem => (int)((ScrollViewer?.VerticalOffset ?? 0) / Math.Max(1, ItemHeight));
    private double ItemHeight => ContainerFromIndex(Math.Max(0, SelectedIndex)) is FrameworkElement item && item.ActualHeight > 0 ? item.ActualHeight : 26;
    public int VisibleItemCount => Math.Max(1, (int)(ActualHeight / Math.Max(1, ItemHeight)));
    public void ClearSelection() => SelectedIndex = -1;
    public void SelectIndex(int index) { if (Items.Count == 0) return; SelectedIndex = Math.Clamp(index, 0, Items.Count - 1); ScrollIntoView(SelectedItem); }
    public void CenterViewOn(int index)
    { if (index < 0 || index >= Items.Count) return; ScrollIntoView(Items[index]); ScrollViewer?.ChangeView(null, Math.Max(0, index * ItemHeight - ActualHeight / 2), null, true); }
}
public class CompletionTipContentControl : ContentControl { }
