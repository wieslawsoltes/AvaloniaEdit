using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using UnoEdit.Search;

namespace UnoEdit;

public partial class TextEditor
{
    internal SearchPanel ExistingSearchPanel { get; set; }
    /// <summary>Gets the native search panel installed on this editor.</summary>
    public SearchPanel SearchPanel => !_disposed ? ExistingSearchPanel ?? SearchPanel.Install(this) : throw new ObjectDisposedException(nameof(TextEditor));
    public static readonly DependencyProperty SearchResultsBrushProperty = DependencyProperty.Register(
        nameof(SearchResultsBrush), typeof(Brush), typeof(TextEditor), new PropertyMetadata(null,
            (sender, args) => ((TextEditor)sender).ExistingSearchPanel?.SetSearchResultsBrush((Brush)args.NewValue)));
    public Brush SearchResultsBrush { get => (Brush)GetValue(SearchResultsBrushProperty); set => SetValue(SearchResultsBrushProperty, value); }
    private void InitializeSearchPanel()
    {
        SearchResultsBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(110, 255, 193, 7));
        _ = SearchPanel;
    }
    private void DisposeSearchPanel() => ExistingSearchPanel?.Uninstall();
}
