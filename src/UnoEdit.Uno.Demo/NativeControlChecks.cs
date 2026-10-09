using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnoEdit.Document;

namespace UnoEdit.Uno.Demo;

// Invoked only by the opt-in browser smoke host, on the real Uno UI thread.
// These are not substitutes for the original editor regression suite.
internal static class NativeControlChecks
{
    internal static string[] Run()
    {
        var checks = new List<string>();
        using var editor = new TextEditor();
        editor.Text = "first\r\nsecond";
        Require(editor.CaretOffset == 0 && !editor.CanUndo, "Text replacement must reset caret and undo history");
        checks.Add("Text property resets caret and undo history");
        editor.Select(7, 6);
        editor.SelectedText = "replacement";
        Require(editor.Text == "first\r\nreplacement", "SelectedText replacement");
        editor.Undo();
        Require(editor.Text == "first\r\nsecond", "Undo selected text replacement");
        checks.Add("SelectedText uses the shared document undo stack");
        var original = editor.Document;
        var replacement = new TextDocument("new document");
        editor.Document = replacement;
        editor.CaretOffset = 3;
        original.Insert(0, "old edit");
        Require(editor.Text == "new document" && editor.CaretOffset == 3, "Old document must be detached");
        checks.Add("Document rebinding detaches the previous document");
        editor.Document = null;
        editor.IsReadOnly = true;
        editor.IsReadOnly = false;
        Require(editor.TextArea.IsReadOnly, "A null document must remain noneditable");
        editor.Document = replacement;
        Require(!editor.TextArea.IsReadOnly, "Restoring a document must restore editable state");
        checks.Add("Null document stays read-only across property toggles");
        var options = new TextEditorOptions { IndentationSize = 8 };
        editor.Options = options;
        Require(editor.TextArea.TextView.Viewport.Style.TabSize == 8, "Options assignment must update layout");
        options.IndentationSize = 2;
        Require(editor.TextArea.TextView.Viewport.Style.TabSize == 2, "Options mutations must update layout");
        checks.Add("Options replacement and mutation update native text layout");
        editor.Text = "Unicode 🙂 e\u0301";
        editor.Encoding = new UTF8Encoding(false);
        using var stream = new MemoryStream();
        editor.Save(stream);
        Require(stream.CanWrite, "Save must leave the caller's stream open");
        stream.Position = 0;
        editor.Clear();
        editor.Load(stream);
        Require(stream.CanRead && editor.Text == "Unicode 🙂 e\u0301", "Load must preserve text and leave the stream open");
        checks.Add("Stream load/save preserves Unicode and stream ownership");
        var retainedDocument = editor.Document;
        var notified = false;
        editor.TextChanged += (_, _) => notified = true;
        ((IDisposable)editor).Dispose();
        editor.Dispose();
        retainedDocument.Insert(0, "after disposal");
        options.IndentationSize = 4;
        Require(!notified, "Disposed editor must detach document events");
        var threw = false;
        try { editor.TextArea.TextView.Viewport.ScrollTo(0, 0); }
        catch (ObjectDisposedException) { threw = true; }
        Require(threw, "IDisposable dispatch must dispose owned viewport resources");
        checks.Add("Concrete and IDisposable cleanup are idempotent and release owned resources");
        return checks.ToArray();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Native Uno control regression: " + message);
    }
}
