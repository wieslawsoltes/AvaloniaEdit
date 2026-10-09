using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.UI.Xaml.Markup;
using Windows.UI;
using Windows.UI.Text;

namespace UnoEdit.Highlighting;

internal static class NativeHighlightingConversions
{
    internal static Color ParseColor(string text) => (Color)XamlBindingHelper.ConvertValue(typeof(Color), text);

    internal static FontWeight ParseFontWeight(string text)
    {
        if (ushort.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var numeric) && numeric is >= 1 and <= 1000)
            return new FontWeight { Weight = numeric };
        var weight = text.ToLowerInvariant() switch
        {
            "thin" => 100,
            "extralight" or "ultralight" => 200,
            "light" => 300,
            "semilight" => 350,
            "normal" or "regular" => 400,
            "medium" => 500,
            "demibold" or "semibold" => 600,
            "bold" => 700,
            "extrabold" or "ultrabold" => 800,
            "black" or "heavy" => 900,
            "extrablack" or "ultrablack" => 950,
            _ => throw new FormatException($"Invalid font weight '{text}'.")
        };
        return new FontWeight { Weight = (ushort)weight };
    }
}

internal static class HighlightingExtensions
{
    internal static T PeekOrDefault<T>(this ImmutableStack<T> stack) => stack.IsEmpty ? default : stack.Peek();
    internal static void AddRange<T>(this ICollection<T> target, IEnumerable<T> items)
    {
        foreach (var item in items) target.Add(item);
    }
}
