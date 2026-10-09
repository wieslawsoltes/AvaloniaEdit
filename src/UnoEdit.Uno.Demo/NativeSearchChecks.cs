using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using UnoEdit.Document;
using UnoEdit.Editing;
using UnoEdit.Search;

namespace UnoEdit.Uno.Demo;

internal static class NativeSearchChecks
{
    internal static string[] Run()
    {
        var checks = new List<string>();
        using var editor = new TextEditor { Text = "cat Cat scatter cat" };
        var panel = editor.SearchPanel;
        Require(ReferenceEquals(panel, SearchPanel.Install(editor)), "Install must not duplicate subscriptions");
        Require(panel.IsClosed && panel.Session.SearchExecutionCount == 0, "Search starts closed and inactive");
        panel.SearchPattern = "cat";
        panel.Open();
        Require(panel.Session.Results.Count == 4, "Native SearchPattern DP must reach the original engine");
        panel.Measure(new Windows.Foundation.Size(440, 400));
        panel.Arrange(new Windows.Foundation.Rect(0, 0, 440, 400));
        panel.ApplyTemplate();
        var searchBox = FindPart<TextBox>(panel, "PART_searchTextBox");
        Require(searchBox != null, "The packaged native default template must create its search TextBox");
        searchBox.Text = "Cat";
        Require(panel.SearchPattern == "Cat", "Native search field updates the dependency property");
        panel.SearchPattern = "cat";
        Require(searchBox.Text == "cat", "Programmatic query updates the native search field");
        panel.MatchCase = true;
        panel.WholeWords = true;
        Require(panel.Session.Results.Count == 2, "Native search-option DPs");
        panel.FindNext();
        Require(editor.SelectionStart == 0 && editor.SelectionLength == 3, "Find selects the actual document match");
        panel.FindNext();
        Require(editor.SelectionStart == 16, "Forward search skips partial/case-mismatched words");
        panel.FindPrevious();
        Require(editor.SelectionStart == 0, "Backward search navigates the same results");
        checks.Add("Native search installation, dependency properties and indexed navigation");

        panel.IsReplaceMode = true;
        panel.ReplacePattern = "dog";
        panel.ReplaceAll();
        Require(editor.Text == "dog Cat scatter dog" && panel.LastReplaceCount == 2, "Native Replace All");
        editor.Undo();
        Require(editor.Text == "cat Cat scatter cat" && !editor.CanUndo, "Replace All is one undo action");
        var provider = new TextSegmentReadOnlySectionProvider<TextSegment>(editor.Document);
        provider.Segments.Add(new TextSegment { StartOffset = 1, Length = 1 });
        editor.TextArea.ReadOnlySectionProvider = provider;
        panel.ReplaceAll();
        Require(editor.Text == "cat Cat scatter dog" && panel.LastSkippedCount == 1, "Protected search matches are not partially replaced");
        editor.IsReadOnly = true;
        Require(!panel.IsReplaceMode, "Read-only state hides replace controls");
        editor.IsReadOnly = false;
        checks.Add("Native replace commands preserve protected matches and grouped undo");

        var old = editor.Document;
        editor.Document = new TextDocument("cat cat");
        Require(ReferenceEquals(panel.Session.Document, editor.Document), "Search follows document rebinding");
        Require(ReferenceEquals(editor.TextArea.TextView.Viewport.MarkerSource?.Document, editor.Document), "Marker source follows new document");
        Require(panel.Session.Results.Count == 2, "New document results");
        old.Insert(0, "old document ");
        Require(panel.Session.Results.Count == 2, "Old document is detached");
        editor.Document = null;
        Require(panel.Session.Results.Count == 0 && !panel.Session.IsActive, "Null document disables searching");
        editor.Document = new TextDocument("cat");
        Require(panel.Session.Results.Count == 1, "Search reactivates after null document");
        checks.Add("Native search and marker layers survive document replacement and null documents");

        panel.UseRegex = true;
        panel.SearchPattern = "[";
        panel.FindNext();
        Require(panel.LastError != null && panel.Session.Results.Count == 0, "Invalid regex clears native matches");
        panel.SearchPattern = "(?<word>cat)";
        panel.ReplacePattern = "${word}!";
        panel.IsReplaceMode = true;
        panel.ReplaceAll();
        Require(editor.Text == "cat!" && panel.LastError == null, "Regex capture replacement and recovery");
        checks.Add("Native invalid-regex feedback and capture replacement recovery");

        panel.Close();
        Require(panel.IsClosed && !panel.Session.IsActive && editor.TextArea.TextView.Viewport.MarkerSource == null, "Close releases matches and markers");
        var executions = panel.Session.SearchExecutionCount;
        editor.Document.Insert(0, "no background search ");
        Require(panel.Session.SearchExecutionCount == executions, "Closed search must not scan edits");
        panel.Uninstall();
        Require(!panel.IsInstalled, "Uninstall releases the native search binding");
        Require(!ReferenceEquals(editor.SearchPanel, panel), "Panel can be reinstalled without old subscriptions");
        var reinstalled = editor.SearchPanel;
        editor.Dispose();
        Require(!reinstalled.IsInstalled, "Editor disposal owns search cleanup");
        checks.Add("Native search close, uninstall, reinstall and editor disposal");
        return checks.ToArray();
    }

    internal static T FindPart<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        if (root is T candidate && candidate.Name == name) return candidate;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindPart<T>(VisualTreeHelper.GetChild(root, i), name);
            if (found != null) return found;
        }
        return null;
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Native Uno search regression: " + message);
    }
}
