using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using UnoEdit.Rendering;
using UnoEdit.Utils;

namespace UnoEdit.Editing;

public partial class TextArea
{
    private ObserveAddRemoveCollection<UIElement> _leftMargins;
    private readonly StackPanel _marginPanel = new() { Orientation = Orientation.Horizontal };
    public IList<UIElement> LeftMargins => _leftMargins;
    private UIElement InitializeMarginLayout()
    {
        TextView.Services.AddService(typeof(TextArea), this);
        _leftMargins = new ObserveAddRemoveCollection<UIElement>(
            element => { if (element == null) throw new ArgumentNullException(nameof(element)); if (VisualTreeHelper.GetParent(element) != null) throw new InvalidOperationException("Margin already has a visual parent."); if (element is ITextViewConnect c) c.AddToTextView(TextView); },
            element => { _marginPanel.Children.Remove(element); if (element is ITextViewConnect c) c.RemoveFromTextView(TextView); });
        _leftMargins.CollectionChanged += (_, _) =>
        {
            _marginPanel.Children.Clear();
            foreach (var element in _leftMargins) _marginPanel.Children.Add(element);
        };
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(_marginPanel); Grid.SetColumn(TextView, 1); grid.Children.Add(TextView);
        return grid;
    }
}
