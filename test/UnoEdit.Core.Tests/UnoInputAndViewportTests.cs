using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnoEdit.Document;
using UnoEdit.Editing;
using UnoEdit.Rendering;

namespace UnoEdit.Core.Tests;

[TestFixture]
public sealed class UnoInputAndViewportTests
{
    [TestCase("🙂", 2)]
    [TestCase("e\u0301", 2)]
    [TestCase("👨‍👩‍👧‍👦", 11)]
    [TestCase("🇵🇱", 4)]
    public void DeleteTreatsAUnicodeTextElementAsOneCharacter(string element, int utf16Length)
    {
        Assert.That(element.Length, Is.EqualTo(utf16Length));
        var document = new TextDocument("a" + element + "b");
        using var session = new EditorSession(document);
        session.MoveTo(1);
        session.MoveHorizontal(1);
        Assert.That(session.CaretOffset, Is.EqualTo(1 + utf16Length));
        session.Delete(true);
        Assert.That(document.Text, Is.EqualTo("ab"));
        Assert.That(session.CaretOffset, Is.EqualTo(1));
        session.Undo();
        Assert.That(document.Text, Is.EqualTo("a" + element + "b"));
        session.Select(1, 0);
        session.Delete(false);
        Assert.That(document.Text, Is.EqualTo("ab"));
    }

    [TestCase("\r\n")]
    [TestCase("\n")]
    [TestCase("\r")]
    public void DelimitersAreAtomicForNavigationAndDeletion(string delimiter)
    {
        var document = new TextDocument("ab" + delimiter + "cd");
        using var session = new EditorSession(document);
        session.MoveTo(2);
        session.MoveHorizontal(1);
        Assert.That(session.CaretOffset, Is.EqualTo(2 + delimiter.Length));
        session.Delete(true);
        Assert.That(document.Text, Is.EqualTo("abcd"));
        session.Undo();
        session.MoveTo(2);
        session.Delete(false);
        Assert.That(document.Text, Is.EqualTo("abcd"));
    }

    [Test]
    public void EnterRetainsExistingDelimiterAndIndentation()
    {
        var document = new TextDocument("  first\r\n  second");
        using var session = new EditorSession(document);
        session.MoveTo(document.TextLength);
        session.Enter();
        Assert.That(document.Text, Is.EqualTo("  first\r\n  second\r\n  "));
        session.Undo();
        Assert.That(document.Text, Is.EqualTo("  first\r\n  second"));
        session.Redo();
        Assert.That(document.LineCount, Is.EqualTo(3));
    }

    [Test]
    public void MultipleSessionsTrackChangesWithoutExpandingACollapsedCaret()
    {
        var document = new TextDocument("abc");
        using var first = new EditorSession(document);
        using var second = new EditorSession(document);
        first.MoveTo(1);
        second.MoveTo(1);
        first.ReplaceSelection("XX");
        Assert.That(second.CaretOffset, Is.EqualTo(3));
        Assert.That(second.SelectionLength, Is.Zero);
        second.Select(1, 2);
        first.Select(0, 1);
        first.ReplaceSelection("header");
        Assert.That(second.SelectedText, Is.EqualTo("XX"));
    }

    [Test]
    public void ReplacingASelectionRaisesOnlyOneSessionNotification()
    {
        using var session = new EditorSession(new TextDocument("original"));
        session.SelectAll();
        var notifications = 0;
        session.Changed += (_, _) => notifications++;
        session.ReplaceSelection("replacement");
        Assert.That(notifications, Is.EqualTo(1));
        Assert.That(session.SelectionLength, Is.Zero);
        Assert.That(session.CaretOffset, Is.EqualTo(11));
    }

    [Test]
    public void BlockIndentIsOneUndoOperationAndExcludesTrailingUnselectedLine()
    {
        var document = new TextDocument("a\nb\nc");
        using var session = new EditorSession(document);
        session.Options.ConvertTabsToSpaces = true;
        session.Options.IndentationSize = 2;
        session.Select(0, 4);
        session.Indent();
        Assert.That(document.Text, Is.EqualTo("  a\n  b\nc"));
        session.Undo();
        Assert.That(document.Text, Is.EqualTo("a\nb\nc"));
        session.Select(0, 4);
        session.Indent();
        session.Indent(true);
        Assert.That(document.Text, Is.EqualTo("a\nb\nc"));
    }

    [Test]
    public void ReadOnlySessionDoesNotMutateSharedDocumentOrUndoHistory()
    {
        var document = new TextDocument("original");
        document.Insert(0, "prefix");
        using var session = new EditorSession(document) { IsReadOnly = true };
        session.SelectAll();
        session.ReplaceSelection("bad");
        session.Delete(true);
        session.Enter();
        session.Indent();
        session.Undo();
        session.Redo();
        Assert.That(document.Text, Is.EqualTo("prefixoriginal"));
    }

    [Test]
    public void DocumentReplacementDetachesTheOldEventSource()
    {
        var original = new TextDocument("old");
        var replacement = new TextDocument("new");
        using var session = new EditorSession(original);
        session.Document = replacement;
        session.MoveTo(1);
        original.Insert(0, "ignored");
        Assert.That(session.CaretOffset, Is.EqualTo(1));
        replacement.Insert(0, "active");
        Assert.That(session.CaretOffset, Is.EqualTo(7));
    }

    [Test]
    public void HeightIndexRetainsMeasurementsAcrossIncrementalDocumentEdits()
    {
        var document = new TextDocument("a\nb\nc\nd");
        using var index = new DocumentHeightIndex(document, 20);
        var measured = document.GetLineByNumber(3);
        index.SetHeight(measured, 60);
        Assert.That(index.TotalHeight, Is.EqualTo(120));
        Assert.That(index.GetLineByVisualPosition(75), Is.SameAs(measured));
        document.Insert(0, "heading\n");
        Assert.That(index.LineCount, Is.EqualTo(5));
        Assert.That(index.GetVisualPosition(measured), Is.EqualTo(60));
        Assert.That(index.GetHeight(measured), Is.EqualTo(60));
        Assert.That(index.TotalHeight, Is.EqualTo(140));
    }

    [Test]
    public void HeightIndexSupportsCollapsedRangesAndRestoration()
    {
        var document = new TextDocument("a\nb\nc\nd");
        using var index = new DocumentHeightIndex(document, 20);
        var collapsed = index.CollapseText(document.GetLineByNumber(2), document.GetLineByNumber(3));
        Assert.That(index.TotalHeight, Is.EqualTo(40));
        Assert.That(index.GetLineByVisualPosition(25).LineNumber, Is.EqualTo(4));
        collapsed.Uncollapse();
        Assert.That(index.TotalHeight, Is.EqualTo(80));
    }

    [Test]
    public void HeightIndexRejectsInvalidMeasurementsAndForeignLines()
    {
        var document = new TextDocument("a\nb");
        using var index = new DocumentHeightIndex(document, 20);
        Assert.Throws<ArgumentOutOfRangeException>(() => index.DefaultLineHeight = double.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => index.SetHeight(document.GetLineByNumber(1), 0));
        Assert.Throws<ArgumentException>(() => index.GetHeight(new TextDocument("foreign").GetLineByNumber(1)));
        Task.Run(() => Assert.Throws<InvalidOperationException>(() => _ = index.TotalHeight)).GetAwaiter().GetResult();
    }

    [Test]
    public void LargeDocumentLookupReturnsTheIndexedViewportWithoutEnumeration()
    {
        const int count = 100000;
        var document = new TextDocument(string.Join("\n", Enumerable.Repeat("line", count)));
        using var index = new DocumentHeightIndex(document, 18);
        for (var line = 1; line < count; line += 997)
            Assert.That(index.GetLineByVisualPosition((line - 1) * 18 + 0.5).LineNumber, Is.EqualTo(line));
        Assert.That(index.TotalHeight, Is.EqualTo(count * 18));
    }
}
