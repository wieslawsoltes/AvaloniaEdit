using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnoEdit.Document;
using UnoEdit.Editing;

namespace UnoEdit.Core.Tests;

[TestFixture]
public sealed class ReadOnlySectionsTests
{
    private sealed class Provider : IReadOnlySectionProvider
    {
        internal Func<int, bool> Insert = _ => true;
        internal Func<ISegment, IEnumerable<ISegment>> Delete = range => new[] { range };
        public bool CanInsert(int offset) => Insert(offset);
        public IEnumerable<ISegment> GetDeletableSegments(ISegment segment) => Delete(segment);
    }

    private static TextSegmentReadOnlySectionProvider<TextSegment> Protect(EditorSession session, int start, int length)
    {
        var provider = new TextSegmentReadOnlySectionProvider<TextSegment>(session.Document);
        provider.Segments.Add(new TextSegment { StartOffset = start, Length = length });
        session.ReadOnlySectionProvider = provider;
        return provider;
    }

    [Test]
    public void InsertionIsBlockedInsideAProtectedRangeButAllowedAtItsBoundary()
    {
        using var session = new EditorSession(new TextDocument("ab[LOCK]cd"));
        var provider = Protect(session, 2, 6);
        session.MoveTo(4);
        session.ReplaceSelection("forbidden");
        Assert.That(session.Document.Text, Is.EqualTo("ab[LOCK]cd"));
        Assert.That(session.CaretOffset, Is.EqualTo(4));
        Assert.That(session.Document.UndoStack.CanUndo, Is.False);
        session.MoveTo(2);
        session.ReplaceSelection("!");
        Assert.That(session.Document.Text, Is.EqualTo("ab![LOCK]cd"));
        var locked = provider.Segments.Single();
        Assert.That(session.Document.GetText(locked), Is.EqualTo("[LOCK]"));
        session.MoveTo(locked.EndOffset);
        session.ReplaceSelection("?");
        Assert.That(session.Document.Text, Is.EqualTo("ab![LOCK]?cd"));
    }

    [TestCase("X", "[LOCK]X", 7)]
    [TestCase("", "[LOCK]", 0)]
    public void ReplacementPreservesProtectedIslandsAndIsOneUndoGroup(string replacement, string expected, int caret)
    {
        const string original = "ab[LOCK]cd";
        using var session = new EditorSession(new TextDocument(original));
        Protect(session, 2, 6);
        session.SelectAll();
        var notifications = 0;
        session.Changed += (_, _) => notifications++;
        session.ReplaceSelection(replacement);
        Assert.That(session.Document.Text, Is.EqualTo(expected));
        Assert.That(session.CaretOffset, Is.EqualTo(caret));
        Assert.That(session.SelectionLength, Is.Zero);
        Assert.That(notifications, Is.EqualTo(1));
        session.Undo();
        Assert.That(session.Document.Text, Is.EqualTo(original));
        Assert.That(session.Document.UndoStack.CanUndo, Is.False);
        session.Redo();
        Assert.That(session.Document.Text, Is.EqualTo(expected));
    }

    [Test]
    public void FullyProtectedSelectionRemainsSelectedAndCreatesNoUndoOperation()
    {
        using var session = new EditorSession(new TextDocument("protected"));
        Protect(session, 0, session.Document.TextLength);
        session.SelectAll();
        var notifications = 0;
        session.Changed += (_, _) => notifications++;
        session.ReplaceSelection("ignored");
        Assert.That(session.SelectedText, Is.EqualTo("protected"));
        Assert.That(session.Document.UndoStack.CanUndo, Is.False);
        Assert.That(notifications, Is.Zero);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void BlockedDeletionDoesNotMoveTheCaretOrCreateASelection(bool backwards)
    {
        using var session = new EditorSession(new TextDocument("protected"));
        Protect(session, 0, session.Document.TextLength);
        session.MoveTo(4);
        var notifications = 0;
        session.Changed += (_, _) => notifications++;
        session.Delete(backwards);
        Assert.That(session.CaretOffset, Is.EqualTo(4));
        Assert.That(session.SelectionLength, Is.Zero);
        Assert.That(session.Document.Text, Is.EqualTo("protected"));
        Assert.That(notifications, Is.Zero);
    }

    [Test]
    public void WordDeletionSkipsProtectedText()
    {
        using var session = new EditorSession(new TextDocument("one locked two"));
        Protect(session, 4, 6);
        session.SelectAll();
        session.Delete(true, true);
        Assert.That(session.Document.Text, Is.EqualTo("locked"));
    }

    [Test]
    public void MultipleProtectedIslandsAndTheirAnchorsSurviveReplacement()
    {
        using var session = new EditorSession(new TextDocument("aLOCKbKEEPc"));
        var provider = Protect(session, 1, 4);
        provider.Segments.Add(new TextSegment { StartOffset = 6, Length = 4 });
        session.SelectAll();
        session.ReplaceSelection("Z");
        Assert.That(session.Document.Text, Is.EqualTo("LOCKKEEPZ"));
        Assert.That(provider.Segments.Select(s => session.Document.GetText(s)), Is.EqualTo(new[] { "LOCK", "KEEP" }));
        Assert.That(session.CaretOffset, Is.EqualTo(9));
    }

    [Test]
    public void ReplacementUsesTheLastDeletableSegmentEvenWhenInsertionAloneIsForbidden()
    {
        using var session = new EditorSession(new TextDocument("abc"));
        session.ReadOnlySectionProvider = new Provider
        {
            Insert = _ => false,
            Delete = _ => new ISegment[] { new SimpleSegment(0, 1), new SimpleSegment(2, 1) }
        };
        session.SelectAll();
        session.ReplaceSelection("Z");
        Assert.That(session.Document.Text, Is.EqualTo("bZ"));
        Assert.That(session.CaretOffset, Is.EqualTo(2));
    }

    [Test]
    public void ReadOnlyToggleDoesNotDiscardACustomProvider()
    {
        using var session = new EditorSession(new TextDocument("abc"));
        var provider = Protect(session, 0, 3);
        session.IsReadOnly = true;
        session.IsReadOnly = false;
        Assert.That(session.ReadOnlySectionProvider, Is.SameAs(provider));
        session.MoveTo(1);
        session.Enter();
        session.Indent();
        Assert.That(session.Document.Text, Is.EqualTo("abc"));
    }

    [Test]
    public void BlockIndentSkipsDisallowedLineStartsAndGroupsUndo()
    {
        using var session = new EditorSession(new TextDocument("a\nb\nc"));
        session.Options.ConvertTabsToSpaces = true;
        session.Options.IndentationSize = 2;
        session.ReadOnlySectionProvider = new Provider { Insert = offset => offset != 2 };
        session.SelectAll();
        session.Indent();
        Assert.That(session.Document.Text, Is.EqualTo("  a\nb\n  c"));
        session.Undo();
        Assert.That(session.Document.Text, Is.EqualTo("a\nb\nc"));
        Assert.That(session.Document.UndoStack.CanUndo, Is.False);
    }

    [Test]
    public void UnindentRemovesOnlyEditableWhitespace()
    {
        using var session = new EditorSession(new TextDocument("    first\n    second"));
        session.Options.IndentationSize = 4;
        Protect(session, 1, 2);
        session.SelectAll();
        session.Indent(true);
        Assert.That(session.Document.Text, Is.EqualTo("  first\nsecond"));
        session.Undo();
        Assert.That(session.Document.Text, Is.EqualTo("    first\n    second"));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void InvalidProviderRangesAreRejectedBeforeAnyEdit(int scenario)
    {
        using var session = new EditorSession(new TextDocument("abcdef"));
        session.SelectAll();
        session.ReadOnlySectionProvider = new Provider
        {
            Delete = _ => scenario switch
            {
                0 => new ISegment[] { new SimpleSegment(0, 2), new SimpleSegment(1, 2) },
                1 => new ISegment[] { new SimpleSegment(0, 2), new SimpleSegment(5, 5) },
                2 => new ISegment[] { new SimpleSegment(0, 2), null },
                _ => null
            }
        };
        Assert.Throws<InvalidOperationException>(() => session.ReplaceSelection("bad"));
        Assert.That(session.Document.Text, Is.EqualTo("abcdef"));
        Assert.That(session.SelectedText, Is.EqualTo("abcdef"));
        Assert.That(session.Document.UndoStack.CanUndo, Is.False);
    }

    [Test]
    public void AProviderCannotCauseStaleOffsetsToBeAppliedAfterDocumentMutation()
    {
        using var session = new EditorSession(new TextDocument("original"));
        session.SelectAll();
        session.ReadOnlySectionProvider = new Provider
        {
            Delete = segment =>
            {
                session.Document.Insert(0, "external ");
                return new[] { segment };
            }
        };
        Assert.Throws<InvalidOperationException>(() => session.ReplaceSelection("must not apply"));
        Assert.That(session.Document.Text, Is.EqualTo("external original"));
    }

    [Test]
    public void ASecondSessionTracksProtectedReplacementWithoutSelectingInsertedText()
    {
        var document = new TextDocument("ab[LOCK]cd");
        using var first = new EditorSession(document);
        using var second = new EditorSession(document);
        Protect(first, 2, 6);
        second.MoveTo(document.TextLength);
        first.SelectAll();
        first.ReplaceSelection("Z");
        Assert.That(second.CaretOffset, Is.EqualTo(document.TextLength));
        Assert.That(second.SelectionLength, Is.Zero);
    }

    [Test]
    public void NullProviderIsRejected()
    {
        using var session = new EditorSession(new TextDocument());
        Assert.Throws<ArgumentNullException>(() => session.ReadOnlySectionProvider = null);
    }
}
