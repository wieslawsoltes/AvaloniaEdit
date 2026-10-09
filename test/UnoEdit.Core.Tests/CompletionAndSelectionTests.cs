using System;
using NUnit.Framework;
using UnoEdit.CodeCompletion;
using UnoEdit.Document;
using UnoEdit.Editing;

namespace UnoEdit.Core.Tests;
[TestFixture]
public sealed class CompletionAndSelectionTests
{
    [TestCase("WriteLine", "WriteLine", 8)]
    [TestCase("WriteLine", "writeline", 7)]
    [TestCase("WriteLine", "Write", 6)]
    [TestCase("WriteLine", "write", 5)]
    [TestCase("WriteLine", "WL", 4)]
    [TestCase("WriteLine", "Line", 3)]
    [TestCase("WriteLine", "line", 2)]
    [TestCase("ArgumentOutOfRangeException", "AOORE", 1)]
    [TestCase("WriteLine", "impossible", -1)]
    public void OriginalCompletionRanking(string candidate, string query, int quality) => Assert.That(CompletionMatcher.GetMatchQuality(candidate, query), Is.EqualTo(quality));
    [Test]
    public void PhysicalCaretDoesNotCollapseExistingSelection()
    {
        using var session = new EditorSession(new TextDocument("abcdef")); session.Select(1, 3);
        session.SetCaretOffset(3); Assert.That(session.SelectedText, Is.EqualTo("bcd"));
        session.SetSelection(5, 2); Assert.That(session.CaretOffset, Is.EqualTo(3)); Assert.That(session.SelectedText, Is.EqualTo("cde"));
        session.ReplaceSelection("X"); Assert.That(session.Document.Text, Is.EqualTo("abXf")); Assert.That(session.CaretOffset, Is.EqualTo(3)); Assert.That(session.SelectionLength, Is.Zero);
    }
    [Test]
    public void IndependentSelectionAndCaretTrackSharedDocumentEdits()
    {
        var document = new TextDocument("abcdef"); using var session = new EditorSession(document);
        session.Select(1, 3); session.SetCaretOffset(6); document.Insert(0, "prefix");
        Assert.That(session.SelectedText, Is.EqualTo("bcd")); Assert.That(session.CaretOffset, Is.EqualTo(12));
        session.MoveHorizontal(-1); Assert.That(session.SelectionLength, Is.Zero); Assert.That(session.CaretOffset, Is.EqualTo(7));
    }
    [Test]
    public void InvalidIndependentEndpointsAreRejectedWithoutChanges()
    {
        using var session = new EditorSession(new TextDocument("abc")); session.Select(0, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SetSelection(0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.SetCaretOffset(-1));
        Assert.That(session.SelectedText, Is.EqualTo("ab"));
    }
}
