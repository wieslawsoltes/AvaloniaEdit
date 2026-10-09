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

internal static class LegacyHighlightingChecks
{
    internal static string[] Run()
    {
        const string legacy = """
            <SyntaxDefinition name="Legacy" extensions=".legacy">
              <Digits color="Red" />
              <RuleSets>
                <RuleSet ignorecase="true">
                  <KeyWords color="Blue" bold="true"><Key word="class" /></KeyWords>
                  <Span color="Green" stopateol="false"><Begin>/*</Begin><End>*/</End></Span>
                </RuleSet>
              </RuleSets>
            </SyntaxDefinition>
            """;
        using var reader = XmlReader.Create(new StringReader(legacy), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        var definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        var document = new TextDocument("CLASS 123\n/* comment\ncontinued */");
        using var highlighter = new DocumentHighlighter(document, definition);
        var first = highlighter.HighlightLine(1);
        Require(first.Sections.Any(s => s.Color.FontWeight?.Weight == 700 && s.Color.Foreground?.GetColor(null)?.B == 255), "Legacy case-insensitive bold keywords");
        Require(first.Sections.Any(s => s.Color.Foreground?.GetColor(null)?.R == 255), "Legacy numeric rules");
        Require(highlighter.HighlightLine(3).Sections.Any(s => s.Color.Foreground?.GetColor(null)?.G == 128), "Legacy multiline spans");

        using var tex = new DocumentHighlighter(new TextDocument("% TeX comment\n\\section{Title}"), HighlightingManager.Instance.GetDefinition("TeX"));
        Require(tex.HighlightLine(1).Sections.Any(s => s.Color.FontStyle == Windows.UI.Text.FontStyle.Italic), "Original TeX comment rule");
        Require(tex.HighlightLine(2).Sections.Any(s => s.Color.FontWeight?.Weight == 700), "Original TeX command rule");

        var original = Windows.UI.Color.FromArgb(255, 10, 20, 30);
        var brush = new SimpleHighlightingBrush(original);
        var firstBrush = (SolidColorBrush)brush.GetBrush(null);
        firstBrush.Color = Windows.UI.Color.FromArgb(255, 200, 210, 220);
        Require(brush.GetColor(null).Equals(original), "Mutating a materialized Uno brush must not change the highlighting descriptor");
        Require(((SolidColorBrush)brush.GetBrush(null)).Color.Equals(original), "Later materializations retain the original immutable color");
        Require(brush.Equals(new SimpleHighlightingBrush(original)), "Highlighting brush equality uses the color value");
        return new[]
        {
            "Legacy XSHD v1 keywords, numeric rules, multiline spans and original TeX definitions execute in native Uno",
            "Materialized mutable Uno brushes cannot alter immutable highlighting color descriptors"
        };
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Native legacy highlighting regression: " + message);
    }
}
