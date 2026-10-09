using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnoEdit.Document;
using UnoEdit.Editing;
using UnoEdit.Search;

namespace UnoEdit.Core.Tests;

[TestFixture]
public sealed class SearchSessionTests
{
    [TestCase(false, false, 4)]
    [TestCase(true, false, 3)]
    [TestCase(false, true, 3)]
    [TestCase(true, true, 2)]
    public void CaseAndWholeWordOptionsUseTheOriginalSearchEngine(bool matchCase, bool wholeWords, int expected)
    {
        using var editor = new EditorSession(new TextDocument("cat Cat scatter cat"));
        using var search = new SearchSession(editor);
        search.Configure("cat", matchCase, wholeWords);
        Assert.That(search.Results.Count, Is.EqualTo(expected));
    }

    [Test]
    public void NavigationWrapsBothWaysAndNeverRescansAnUnchangedDocument()
    {
        using var editor = new EditorSession(new TextDocument("a x a x a"));
        using var search = new SearchSession(editor);
        search.Configure("a");
        Assert.That(search.FindNext().Offset, Is.EqualTo(0));
        Assert.That(search.FindNext().Offset, Is.EqualTo(4));
        Assert.That(search.FindNext().Offset, Is.EqualTo(8));
        Assert.That(search.FindNext().Offset, Is.EqualTo(0));
        Assert.That(search.FindPrevious().Offset, Is.EqualTo(8));
        editor.MoveTo(3);
        Assert.That(search.FindPrevious().Offset, Is.EqualTo(0));
        Assert.That(search.SearchExecutionCount, Is.EqualTo(1));
        editor.Document.Insert(0, "a ");
        Assert.That(search.Results.Count, Is.EqualTo(4));
        Assert.That(search.SearchExecutionCount, Is.EqualTo(2));
    }

    [Test]
    public void ZeroWidthMatchesAdvanceWithoutLoopsAndReplaceOnlyTheOriginalMatches()
    {
        using var editor = new EditorSession(new TextDocument("a\nb"));
        using var search = new SearchSession(editor);
        search.Configure("^", useRegex: true);
        Assert.That(search.FindNext().Offset, Is.Zero);
        Assert.That(search.FindNext().Offset, Is.EqualTo(2));
        Assert.That(search.FindNext().Offset, Is.Zero);
        Assert.That(search.ReplaceAll(">"), Is.EqualTo(2));
        Assert.That(editor.Document.Text, Is.EqualTo(">a\n>b"));
        editor.Undo();
        Assert.That(editor.Document.Text, Is.EqualTo("a\nb"));
    }

    [TestCase("x", "xx", "xx xx")]
    [TestCase("x", "$&-$1-$$", "$&-$1-$$ $&-$1-$$")]
    public void LiteralReplacementDoesNotExpandDollarsOrRematchInsertedText(string pattern, string replacement, string expected)
    {
        using var editor = new EditorSession(new TextDocument("x x"));
        using var search = new SearchSession(editor);
        search.Configure(pattern);
        Assert.That(search.ReplaceAll(replacement), Is.EqualTo(2));
        Assert.That(editor.Document.Text, Is.EqualTo(expected));
        editor.Undo();
        Assert.That(editor.Document.Text, Is.EqualTo("x x"));
        Assert.That(editor.Document.UndoStack.CanUndo, Is.False);
    }

    [Test]
    public void RegexReplacementExpandsNamedAndNumberedCaptures()
    {
        using var editor = new EditorSession(new TextDocument("foo12 bar34"));
        using var search = new SearchSession(editor);
        search.Configure(@"(?<name>\w+?)(\d+)", useRegex: true);
        Assert.That(search.ReplaceAll("${name}:$1"), Is.EqualTo(2));
        Assert.That(editor.Document.Text, Is.EqualTo("foo:12 bar:34"));
    }

    [Test]
    public void ReplaceAllSkipsPartialOrEntireProtectedMatchesAndGroupsUndo()
    {
        using var editor = new EditorSession(new TextDocument("cat cat cat"));
        var provider = new TextSegmentReadOnlySectionProvider<TextSegment>(editor.Document);
        provider.Segments.Add(new TextSegment { StartOffset = 5, Length = 1 });
        editor.ReadOnlySectionProvider = provider;
        using var search = new SearchSession(editor);
        search.Configure("cat");
        Assert.That(search.ReplaceAll("doggy"), Is.EqualTo(2));
        Assert.That(search.LastSkippedCount, Is.EqualTo(1));
        Assert.That(editor.Document.Text, Is.EqualTo("doggy cat doggy"));
        editor.Undo();
        Assert.That(editor.Document.Text, Is.EqualTo("cat cat cat"));
        Assert.That(provider.Segments.Single().StartOffset, Is.EqualTo(5));
    }

    [Test]
    public void ReplaceNextSkipsProtectedMatchAndSelectsFollowingMatch()
    {
        using var editor = new EditorSession(new TextDocument("cat cat"));
        var provider = new TextSegmentReadOnlySectionProvider<TextSegment>(editor.Document);
        provider.Segments.Add(new TextSegment { StartOffset = 0, Length = 3 });
        editor.ReadOnlySectionProvider = provider;
        using var search = new SearchSession(editor);
        search.Configure("cat");
        search.FindNext();
        Assert.That(search.ReplaceNext("dog"), Is.False);
        Assert.That(editor.SelectionStart, Is.EqualTo(4));
        Assert.That(search.ReplaceNext("dog"), Is.True);
        Assert.That(editor.Document.Text, Is.EqualTo("cat dog"));
    }

    [Test]
    public void ReadOnlyBlocksBothReplacementCommandsWithoutChangingSelection()
    {
        using var editor = new EditorSession(new TextDocument("cat cat")) { IsReadOnly = true };
        using var search = new SearchSession(editor);
        search.Configure("cat");
        Assert.That(search.FindNext(), Is.Not.Null);
        Assert.That(search.ReplaceNext("dog"), Is.False);
        Assert.That(search.ReplaceAll("dog"), Is.Zero);
        Assert.That(editor.SelectedText, Is.EqualTo("cat"));
        Assert.That(editor.Document.Text, Is.EqualTo("cat cat"));
        Assert.That(editor.Document.UndoStack.CanUndo, Is.False);
    }

    [Test]
    public void TruncatedResultsAreExplicitAndNeverPermitPartialReplaceAll()
    {
        using var editor = new EditorSession(new TextDocument("aaaa"));
        using var search = new SearchSession(editor) { MaximumResults = 2 };
        search.Configure("a");
        Assert.That(search.Results.Count, Is.EqualTo(2));
        Assert.That(search.IsTruncated, Is.True);
        Assert.Throws<InvalidOperationException>(() => search.ReplaceAll("b"));
        Assert.That(editor.Document.Text, Is.EqualTo("aaaa"));
        search.MaximumResults = 4;
        Assert.That(search.ReplaceAll("b"), Is.EqualTo(4));
        Assert.That(search.IsTruncated, Is.False);
    }

    [Test]
    public void MalformedPatternClearsOldResultsAndRecovers()
    {
        using var editor = new EditorSession(new TextDocument("word"));
        using var search = new SearchSession(editor);
        search.Configure("word");
        Assert.That(search.Results.Count, Is.EqualTo(1));
        search.Configure("[", useRegex: true);
        Assert.That(search.Results, Is.Empty);
        Assert.That(search.Error, Is.TypeOf<SearchPatternException>());
        Assert.Throws<InvalidOperationException>(() => search.ReplaceAll("bad"));
        search.Configure("word");
        Assert.That(search.Results.Count, Is.EqualTo(1));
        Assert.That(search.Error, Is.Null);
    }

    [Test]
    public void BacktrackingTimeoutIsReportedWithoutKeepingPartialResults()
    {
        using var editor = new EditorSession(new TextDocument(new string('a', 10000) + "!"));
        using var search = new SearchSession(editor) { MatchTimeout = TimeSpan.FromMilliseconds(1) };
        search.Configure("(a+)+$", useRegex: true);
        Assert.That(search.Results, Is.Empty);
        Assert.That(search.Error, Is.TypeOf<RegexMatchTimeoutException>());
        Assert.That(editor.Document.UndoStack.CanUndo, Is.False);
    }

    [Test]
    public void InactiveSearchDoesNotRescanAndDocumentReplacementDetachesPreviousSource()
    {
        var original = new TextDocument("old");
        using var editor = new EditorSession(original);
        using var search = new SearchSession(editor);
        search.Configure("new");
        search.IsActive = false;
        editor.Document = new TextDocument("new");
        Assert.That(search.Results, Is.Empty);
        Assert.That(search.SearchExecutionCount, Is.Zero);
        search.IsActive = true;
        Assert.That(search.Results.Count, Is.EqualTo(1));
        var invalidations = 0;
        search.ResultsInvalidated += (_, _) => invalidations++;
        original.Insert(0, "ignored");
        Assert.That(invalidations, Is.Zero);
        search.Dispose();
        editor.Document.Insert(0, "after disposal");
        Assert.That(invalidations, Is.Zero);
    }

    [Test]
    public void QueryResultsCannotBeMutatedThroughTheOriginalTextSegmentType()
    {
        using var editor = new EditorSession(new TextDocument("a"));
        using var search = new SearchSession(editor);
        search.Configure("a");
        Assert.That(search.Results[0], Is.Not.InstanceOf<TextSegment>());
        Assert.Throws<NotSupportedException>(() => ((IList<ISearchResult>)search.Results).Clear());
    }

    [Test]
    public void ReplacementNotifiesSharedSessionOnlyOnce()
    {
        using var editor = new EditorSession(new TextDocument("a a a"));
        using var search = new SearchSession(editor);
        search.Configure("a");
        var changes = 0;
        editor.Changed += (_, _) => changes++;
        Assert.That(search.ReplaceAll("b"), Is.EqualTo(3));
        Assert.That(changes, Is.EqualTo(1));
    }

    [Test]
    public void MalformedLaterProviderResultDoesNotApplyEarlierPlannedReplacements()
    {
        using var editor = new EditorSession(new TextDocument("cat cat"));
        editor.ReadOnlySectionProvider = new CallbackProvider(range =>
            range.Offset == 0 ? new[] { range } : new ISegment[] { new SimpleSegment(0, 1) });
        using var search = new SearchSession(editor);
        search.Configure("cat");
        Assert.Throws<InvalidOperationException>(() => search.ReplaceAll("bad"));
        Assert.That(editor.Document.Text, Is.EqualTo("cat cat"));
        Assert.That(editor.Document.UndoStack.CanUndo, Is.False);
    }

    [Test]
    public void ProviderChangingEditProtectionAbortsBeforeMutation()
    {
        using var editor = new EditorSession(new TextDocument("cat"));
        editor.ReadOnlySectionProvider = new CallbackProvider(range => { editor.IsReadOnly = true; return new[] { range }; });
        using var search = new SearchSession(editor);
        search.Configure("cat");
        Assert.Throws<InvalidOperationException>(() => search.ReplaceAll("bad"));
        Assert.That(editor.Document.Text, Is.EqualTo("cat"));
    }

    [Test]
    public void UpdateStartedMutationNeverAppliesStaleSearchOffsets()
    {
        using var editor = new EditorSession(new TextDocument("cat"));
        using var search = new SearchSession(editor);
        search.Configure("cat");
        editor.Document.UpdateStarted += (_, _) => editor.Document.Insert(0, "callback ");
        Assert.Throws<InvalidOperationException>(() => search.ReplaceAll("bad"));
        Assert.That(editor.Document.Text, Is.EqualTo("callback cat"));
    }

    [Test]
    public void ZeroWidthReplaceNextAdvancesAndCanBeUndone()
    {
        using var editor = new EditorSession(new TextDocument("ab"));
        using var search = new SearchSession(editor);
        search.Configure("(?=.)", useRegex: true);
        search.FindNext();
        Assert.That(search.ReplaceNext(""), Is.True);
        Assert.That(editor.CaretOffset, Is.EqualTo(1));
        Assert.That(search.ReplaceNext("x"), Is.True);
        Assert.That(editor.Document.Text, Is.EqualTo("axb"));
        editor.Undo();
        Assert.That(editor.Document.Text, Is.EqualTo("ab"));
    }

    [Test]
    public void SearchStrategyEqualityIncludesWholeWordAndTimeoutSettings()
    {
        var basic = SearchStrategyFactory.Create("a", false, false, SearchMode.Normal);
        Assert.That(basic.Equals(SearchStrategyFactory.Create("a", false, false, SearchMode.Normal)), Is.True);
        Assert.That(basic.Equals(SearchStrategyFactory.Create("a", false, true, SearchMode.Normal)), Is.False);
        Assert.That(basic.Equals(SearchStrategyFactory.Create("a", false, false, SearchMode.Normal, TimeSpan.FromMilliseconds(10))), Is.False);
    }

    [Test]
    public void InvalidSearchRangesAndBudgetsAreRejected()
    {
        var strategy = SearchStrategyFactory.Create("a", false, false, SearchMode.Normal);
        var text = new StringTextSource("a");
        Assert.Throws<ArgumentOutOfRangeException>(() => strategy.FindAll(text, -1, 1).ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => strategy.FindAll(text, 0, 2).ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => SearchStrategyFactory.Create("a", false, false, (SearchMode)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => SearchStrategyFactory.Create("a", false, false, SearchMode.Normal, Regex.InfiniteMatchTimeout));
    }

    private sealed class CallbackProvider : IReadOnlySectionProvider
    {
        private readonly Func<ISegment, IEnumerable<ISegment>> _callback;
        internal CallbackProvider(Func<ISegment, IEnumerable<ISegment>> callback) => _callback = callback;
        public bool CanInsert(int offset) => true;
        public IEnumerable<ISegment> GetDeletableSegments(ISegment segment) => _callback(segment);
    }
}
