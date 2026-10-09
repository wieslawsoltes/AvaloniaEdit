// Copyright (c) 2014 AlphaSierraPapa for the SharpDevelop Team.
// Original completion-match ranking, extracted for native Uno; MIT license.
using System;
namespace UnoEdit.CodeCompletion;

/// <summary>Original exact/prefix/substring/camel-case completion ranking.</summary>
public static class CompletionMatcher
{
    public static int GetMatchQuality(string itemText, string query, bool isFiltering = true)
    {
        if (itemText == null) throw new ArgumentNullException(nameof(itemText), "ICompletionData.Text returned null");
        if (query == null) throw new ArgumentNullException(nameof(query));
        if (query == itemText) return 8;
        if (string.Equals(itemText, query, StringComparison.CurrentCultureIgnoreCase)) return 7;
        if (itemText.StartsWith(query, StringComparison.CurrentCulture)) return 6;
        if (itemText.StartsWith(query, StringComparison.CurrentCultureIgnoreCase)) return 5;
        bool? camelCase = null;
        if (query.Length <= 2) { camelCase = CamelCaseMatch(itemText, query); if (camelCase == true) return 4; }
        if (isFiltering)
        {
            if (itemText.IndexOf(query, StringComparison.CurrentCulture) >= 0) return 3;
            if (itemText.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0) return 2;
        }
        return (camelCase ?? CamelCaseMatch(itemText, query)) ? 1 : -1;
    }
    private static bool CamelCaseMatch(string text, string query)
    {
        var index = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (i != 0 && !char.IsUpper(text[i])) continue;
            if (index == query.Length) return true;
            if (char.ToUpperInvariant(query[index]) != char.ToUpperInvariant(text[i])) return false;
            index++;
        }
        return index >= query.Length;
    }
}
