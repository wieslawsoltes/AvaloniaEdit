using System;
using System.Collections.Generic;
using UnoEdit.Document;
using UnoEdit.Editing;

namespace UnoEdit.Uno.Demo;

internal static class NativeReadOnlyChecks
{
    internal static IEnumerable<string> Run()
    {
        using var editor = new TextEditor();
        editor.Text = "ab[LOCK]cd";
        var provider = new TextSegmentReadOnlySectionProvider<TextSegment>(editor.Document);
        provider.Segments.Add(new TextSegment { StartOffset = 2, Length = 6 });
        editor.TextArea.ReadOnlySectionProvider = provider;
        editor.CaretOffset = 4;
        editor.SelectedText = "forbidden";
        Require(editor.Text == "ab[LOCK]cd" && editor.CaretOffset == 4, "Native typing must respect protected text");
        editor.TextArea.Session.Delete(false);
        Require(editor.SelectionLength == 0 && editor.CaretOffset == 4, "Blocked Delete must not move or select");
        editor.IsReadOnly = true;
        editor.IsReadOnly = false;
        Require(ReferenceEquals(editor.TextArea.ReadOnlySectionProvider, provider), "Read-only property must retain the custom provider");
        editor.SelectAll();
        editor.TextArea.ReplaceSelectionWithText("X");
        Require(editor.Text == "[LOCK]X" && editor.CaretOffset == 7, "Original protected-island replacement contract");
        editor.Undo();
        Require(editor.Text == "ab[LOCK]cd" && !editor.CanUndo, "Protected replacement must be one undo operation");
        editor.SelectAll();
        editor.TextArea.RemoveSelectedText();
        Require(editor.Text == "[LOCK]" && editor.SelectionLength == 0, "RemoveSelectedText must preserve protected content");
        editor.Undo();
        editor.Select(0, 2);
        editor.TextArea.ClearSelection();
        Require(editor.SelectionLength == 0 && editor.CaretOffset == 2, "ClearSelection must preserve the active caret");
        return new[]
        {
            "Native read-only section provider blocks insertion and deletion",
            "Native protected selection replacement preserves islands and grouped undo",
            "Native read-only toggles and selection commands preserve provider state"
        };
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Native protected-edit regression: " + message);
    }
}
