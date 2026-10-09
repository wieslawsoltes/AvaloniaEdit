using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.UI.Xaml.Media;
using UnoEdit.Document;
using UnoEdit.Highlighting;
using UnoEdit.Highlighting.Xshd;

namespace UnoEdit.Uno.Demo;

// Runs in the actual Uno app as part of the browser smoke suite. No Avalonia
// headless assembly or replacement framework types are involved in these tests.
internal static class NativeHighlightingChecks
{
    private const string Definition = """
        <SyntaxDefinition name="NativeTest" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="Comment" foreground="#008000" />
          <Color name="Keyword" foreground="#204080" background="#10203040" fontWeight="Bold" fontStyle="Italic" underline="true" fontSize="24" />
          <RuleSet>
            <Span color="Comment" begin="/\*" end="\*/" multiline="true" />
            <Keywords color="Keyword"><Word>class</Word></Keywords>
          </RuleSet>
        </SyntaxDefinition>
        """;

    internal static string[] Run()
    {
        var checks = new List<string>();
        var definitions = HighlightingManager.Instance.HighlightingDefinitions;
        Require(definitions.Count >= 21, "All original built-in definitions must be embedded");
        foreach (var definition in definitions)
        {
            Require(definition.MainRuleSet != null, "Missing main rule set for " + definition.Name);
            using var highlighter = new DocumentHighlighter(new TextDocument("class Example { }\n<!-- html -->\n# sample"), definition);
            for (var line = 1; line <= highlighter.Document.LineCount; line++)
                Require(highlighter.HighlightLine(line) != null, "Cannot highlight " + definition.Name);
        }
        Require(HighlightingManager.Instance.GetDefinitionByExtension(".cs").Name == "C#", "C# extension mapping");
        Require(HighlightingManager.Instance.GetDefinitionByExtension(".json").MainRuleSet != null, "JSON extension mapping");
        checks.Add($"All {definitions.Count} original highlighting definitions load and execute in native Uno");

        using var reader = XmlReader.Create(new StringReader(Definition), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        var custom = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        var keyword = custom.GetNamedColor("Keyword");
        Require(keyword.FontWeight?.Weight == 700 && keyword.FontSize == 24 && keyword.Underline == true, "XSHD native font properties");
        var foreground = keyword.Foreground.GetColor(null);
        Require(foreground?.R == 0x20 && foreground?.G == 0x40 && foreground?.B == 0x80, "XSHD RGB color");
        Require(keyword.Background.GetColor(null)?.A == 0x10, "XSHD ARGB color");
        Require(keyword.ToCss().Contains("700", StringComparison.Ordinal), "CSS must write numeric native font weights");
        Require(keyword.Foreground.GetBrush(null) is SolidColorBrush, "XSHD produces real native brushes");
        checks.Add("Custom XSHD uses native RGB/ARGB colors, font weights, styles, sizes and decorations");

        var document = new TextDocument("/* comment\nstill comment\n*/ class Tail");
        using (var highlighter = new DocumentHighlighter(document, custom))
        {
            Require(HasColor(highlighter.HighlightLine(2), "Comment"), "Multiline state must flow from preceding line");
            Require(HasColor(highlighter.HighlightLine(3), "Keyword"), "Comment closure must resume keyword highlighting");
            document.Remove(0, 2);
            Require(!HasColor(highlighter.HighlightLine(2), "Comment"), "Removing opener must invalidate later lexical state");
            document.UndoStack.Undo();
            Require(HasColor(highlighter.HighlightLine(2), "Comment"), "Undo must restore multiline state");
            document.Insert(document.GetLineByNumber(2).Offset, "new line\n");
            Require(HasColor(highlighter.HighlightLine(3), "Comment"), "Line insertion must update compressed state indexing");
        }
        checks.Add("Multiline highlighting survives opener edits, undo and line insertions");

        var color = new HighlightingColor { Foreground = new SimpleHighlightingBrush(Windows.UI.Color.FromArgb(255, 20, 30, 40)) };
        color.Freeze();
        var rejected = false;
        try { color.Underline = true; } catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "Frozen highlighting color must not mutate");
        var clone = color.Clone();
        clone.Underline = true;
        Require(clone.Underline == true && color.Underline == null, "Clone must remain independently mutable");
        checks.Add("Highlighting color freeze and clone contracts are preserved");

        var htmlDocument = new TextDocument("class <T> & \"value\"\t tail");
        using (var highlighter = new DocumentHighlighter(htmlDocument, custom))
        {
            var highlighted = highlighter.HighlightLine(1);
            var rich = highlighted.ToRichText();
            Require(rich.Text == htmlDocument.Text, "Rich text must retain the exact source");
            Require(rich.GetHighlightedSections(0, rich.Length).Any(s => s.Color.FontWeight?.Weight == 700), "Flattening retains syntax styles");
            var html = highlighted.ToHtml();
            Require(html.Contains("&lt;", StringComparison.Ordinal) && html.Contains("&amp;", StringComparison.Ordinal), "HTML export must escape source text");
            Require(html.Contains("tail", StringComparison.Ordinal), "HTML writer must preserve text after whitespace");
        }
        checks.Add("Rich text flattening and HTML output retain syntax and escape source text");

        using var editor = new TextEditor { Text = "class Item", SyntaxHighlighting = custom };
        var first = editor.Document;
        var firstHighlighter = editor.Highlighter;
        var viewport = editor.TextArea.TextView.Viewport;
        Require(firstHighlighter != null && ReferenceEquals(firstHighlighter.Document, first), "Native syntax DP creates a document highlighter");
        var spans = viewport.LineStyleSource.GetStyles(first.GetLineByNumber(1));
        Require(spans.Any(s => s.FontSize == 24 && s.FontWeight == 700 && s.Foreground.HasValue), "Native viewport receives actual styled runs");
        viewport.SetViewport(640, 480);
        var caret = viewport.GetCaretRectangle(5);
        Require(caret.Height >= 20, "Highlighted font size must affect shaped caret metrics");
        editor.Document = new TextDocument("class Replacement");
        Require(!ReferenceEquals(firstHighlighter, editor.Highlighter) && ReferenceEquals(editor.Highlighter.Document, editor.Document), "Rebinding replaces per-document lexical state");
        first.Insert(0, "detached\n");
        Require(editor.Highlighter.HighlightLine(1).Sections.Count != 0, "Detached source edits must not affect replacement");
        editor.SyntaxHighlighting = null;
        Require(editor.Highlighter == null && viewport.LineStyleSource == null, "Plain text detaches highlighting");
        editor.SyntaxHighlighting = custom;
        editor.Dispose();
        Require(editor.Highlighter == null, "Editor disposal releases highlighter and its weak line tracker");
        checks.Add("Native SyntaxHighlighting binding, styled geometry, document replacement and disposal work end-to-end");
        return checks.ToArray();
    }

    private static bool HasColor(HighlightedLine line, string name) => line.Sections.Any(s => s.Color.Name == name);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Native Uno highlighting regression: " + message);
    }
}
