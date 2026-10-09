using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using UnoEdit.Document;
using UnoEdit.Editing;

namespace UnoEdit.Search;

/// <summary>
/// Document-backed search state shared by native controls and headless tests.
/// Results are immutable and cached per document version and query. Navigation
/// is indexed; moving the caret never rescans or copies the document.
/// </summary>
public sealed class SearchSession : IDisposable
{
    private sealed class MatchResult : ISearchResult
    {
        private readonly ISearchResult _match;
        internal MatchResult(ISearchResult match) { _match = match; Offset = match.Offset; Length = match.Length; }
        public int Offset { get; }
        public int Length { get; }
        public int EndOffset => Offset + Length;
        public string ReplaceWith(string replacement) => _match.ReplaceWith(replacement);
    }

    private readonly EditorSession _editor;
    private TextDocument _document;
    private ITextSourceVersion _version;
    private IReadOnlyList<ISearchResult> _results = Array.Empty<ISearchResult>();
    private bool _dirty = true;
    private bool _active = true;
    private bool _disposed;
    private int _currentIndex = -1;
    private string _pattern = string.Empty;
    private bool _matchCase;
    private bool _wholeWords;
    private bool _useRegex;
    private int _maximumResults = 100000;
    private TimeSpan _matchTimeout = TimeSpan.FromMilliseconds(250);

    public SearchSession(EditorSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _document = editor.Document;
        _document.TextChanged += OnTextChanged;
        _editor.Changed += OnEditorChanged;
    }

    public string SearchPattern => _pattern;
    public bool MatchCase => _matchCase;
    public bool WholeWords => _wholeWords;
    public bool UseRegex => _useRegex;
    public TextDocument Document => _document;
    public IReadOnlyList<ISearchResult> Results { get { Refresh(); return _results; } }
    public int CurrentResultIndex => _currentIndex;
    public Exception Error { get; private set; }
    public bool IsTruncated { get; private set; }
    public int LastReplaceCount { get; private set; }
    public int LastSkippedCount { get; private set; }
    public long SearchExecutionCount { get; private set; }
    public event EventHandler ResultsInvalidated;
    public event EventHandler ResultsChanged;

    /// <summary>Closed search panels retain no document snapshot or result list.</summary>
    public bool IsActive
    {
        get => _active;
        set
        {
            ThrowIfDisposed();
            if (_active == value) return;
            _active = value;
            if (value) _document.TextChanged += OnTextChanged;
            else _document.TextChanged -= OnTextChanged;
            Invalidate();
        }
    }

    /// <summary>
    /// Maximum retained matches. Truncation is explicit; ReplaceAll refuses to
    /// modify a truncated result set rather than silently replacing a prefix.
    /// </summary>
    public int MaximumResults
    {
        get => _maximumResults;
        set
        {
            ThrowIfDisposed();
            if (value < 1) throw new ArgumentOutOfRangeException(nameof(value));
            if (_maximumResults == value) return;
            _maximumResults = value;
            Invalidate();
        }
    }

    /// <summary>Finite per-match budget for backtracking regular expressions.</summary>
    public TimeSpan MatchTimeout
    {
        get => _matchTimeout;
        set
        {
            ThrowIfDisposed();
            if (value.TotalMilliseconds < 1 || value.TotalMilliseconds > int.MaxValue - 1)
                throw new ArgumentOutOfRangeException(nameof(value));
            if (_matchTimeout == value) return;
            _matchTimeout = value;
            Invalidate();
        }
    }

    public void Configure(string pattern, bool matchCase = false, bool wholeWords = false, bool useRegex = false)
    {
        ThrowIfDisposed();
        pattern ??= string.Empty;
        if (_pattern == pattern && _matchCase == matchCase && _wholeWords == wholeWords && _useRegex == useRegex) return;
        _pattern = pattern;
        _matchCase = matchCase;
        _wholeWords = wholeWords;
        _useRegex = useRegex;
        Invalidate();
    }

    /// <summary>Recompute only after invalidation; failures clear stale matches.</summary>
    public void Refresh()
    {
        ThrowIfDisposed();
        if (!_active || !_dirty) return;
        _document.VerifyAccess();
        _dirty = false;
        _currentIndex = -1;
        Error = null;
        IsTruncated = false;
        _results = Array.Empty<ISearchResult>();
        _version = _document.Version;
        if (_pattern.Length != 0)
        {
            SearchExecutionCount++;
            try
            {
                var strategy = SearchStrategyFactory.Create(_pattern, !_matchCase, _wholeWords,
                    _useRegex ? SearchMode.RegEx : SearchMode.Normal, _matchTimeout);
                var snapshot = _document.CreateSnapshot();
                var matches = new List<ISearchResult>();
                foreach (var match in strategy.FindAll(snapshot, 0, snapshot.TextLength))
                {
                    if (matches.Count == _maximumResults) { IsTruncated = true; break; }
                    matches.Add(new MatchResult(match));
                }
                _results = new ReadOnlyCollection<ISearchResult>(matches);
            }
            catch (Exception error) when (error is SearchPatternException || error is RegexMatchTimeoutException)
            {
                Error = error;
            }
        }
        ResultsChanged?.Invoke(this, EventArgs.Empty);
    }

    public ISearchResult FindNext() => Navigate(false);
    public ISearchResult FindPrevious() => Navigate(true);

    private ISearchResult Navigate(bool backwards)
    {
        Refresh();
        if (_results.Count == 0) return null;
        int index;
        if (SelectionMatchesCurrent()) index = _currentIndex + (backwards ? -1 : 1);
        else
        {
            index = LowerBound(_editor.SelectionStart);
            if (backwards) index--;
            else if (index < _results.Count && _editor.SelectionLength != 0 && SelectionMatches(_results[index])) index++;
        }
        return SelectResult((index + _results.Count) % _results.Count);
    }

    /// <summary>Select the first match at or after an offset, wrapping at EOF.</summary>
    public ISearchResult FindAtOrAfter(int offset)
    {
        Refresh();
        if (offset < 0 || offset > _document.TextLength) throw new ArgumentOutOfRangeException(nameof(offset));
        if (_results.Count == 0) return null;
        return SelectResult(LowerBound(offset) % _results.Count);
    }

    private ISearchResult SelectResult(int index)
    {
        var result = _results[index];
        _editor.Select(result.Offset, result.Length);
        _currentIndex = index;
        return result;
    }

    private bool SelectionMatchesCurrent() => _currentIndex >= 0 && _currentIndex < _results.Count && SelectionMatches(_results[_currentIndex]);
    private bool SelectionMatches(ISearchResult match) => _editor.SelectionStart == match.Offset && _editor.SelectionLength == match.Length;

    private int LowerBound(int offset)
    {
        var low = 0;
        var high = _results.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (_results[middle].Offset < offset) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    /// <summary>
    /// Replace the current/next complete editable match. A protected match is
    /// skipped without partial replacement. Regex groups expand only in regex
    /// mode; dollar signs in normal replacement text remain literal.
    /// </summary>
    public bool ReplaceNext(string replacement)
    {
        Refresh();
        LastReplaceCount = LastSkippedCount = 0;
        if (_editor.IsReadOnly || _results.Count == 0) return false;
        replacement ??= string.Empty;
        var index = SelectionMatchesCurrent() ? _currentIndex : LowerBound(_editor.SelectionStart) % _results.Count;
        var match = _results[index];
        var text = _useRegex ? match.ReplaceWith(replacement) : replacement;
        var replaced = _editor.ApplySearchReplacements(new[] { (match, text) }, _version, true, out var skipped);
        LastReplaceCount = replaced;
        LastSkippedCount = skipped;
        if (replaced == 0)
        {
            SelectResult((index + 1) % _results.Count);
            return false;
        }
        var resume = Math.Min(_document.TextLength, match.Offset + text.Length);
        Refresh();
        if (_results.Count > 0)
        {
            var next = LowerBound(resume);
            // Zero-width expressions must advance even if they match again
            // immediately after inserted text (or insert an empty string).
            if (match.Length == 0 && next < _results.Count && _results[next].Offset == resume && _results[next].Length == 0) next++;
            SelectResult(next % _results.Count);
        }
        return true;
    }

    /// <summary>
    /// Apply one version-checked, prevalidated batch in one undo group. The
    /// original matches are evaluated once, so inserted text is not re-matched.
    /// </summary>
    public int ReplaceAll(string replacement)
    {
        Refresh();
        LastReplaceCount = LastSkippedCount = 0;
        if (Error != null) throw new InvalidOperationException("Correct the search pattern before replacing text.", Error);
        if (IsTruncated) throw new InvalidOperationException("Search results are truncated. Narrow the search or increase MaximumResults before Replace All.");
        if (_editor.IsReadOnly || _results.Count == 0) return 0;
        replacement ??= string.Empty;
        var edits = new List<(ISearchResult, string)>(_results.Count);
        foreach (var match in _results) edits.Add((match, _useRegex ? match.ReplaceWith(replacement) : replacement));
        var count = _editor.ApplySearchReplacements(edits, _version, false, out var skipped);
        LastReplaceCount = count;
        LastSkippedCount = skipped;
        Refresh();
        return count;
    }

    private void OnTextChanged(object sender, EventArgs args) => Invalidate();
    private void OnEditorChanged(object sender, EventArgs args)
    {
        if (ReferenceEquals(_editor.Document, _document)) return;
        if (_active) _document.TextChanged -= OnTextChanged;
        _document = _editor.Document;
        if (_active) _document.TextChanged += OnTextChanged;
        Invalidate();
    }

    private void Invalidate()
    {
        _dirty = true;
        _results = Array.Empty<ISearchResult>();
        _version = null;
        _currentIndex = -1;
        Error = null;
        IsTruncated = false;
        ResultsInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SearchSession));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _document.TextChanged -= OnTextChanged;
        _editor.Changed -= OnEditorChanged;
        _results = Array.Empty<ISearchResult>();
        _version = null;
        ResultsChanged = ResultsInvalidated = null;
    }
}
