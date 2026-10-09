using System;
using System.Collections.Generic;
using UnoEdit.Document;
using UnoEdit.Search;

namespace UnoEdit.Editing;

public sealed partial class EditorSession
{
    internal int ApplySearchReplacements(IReadOnlyList<(ISearchResult Match, string Text)> candidates,
        ITextSourceVersion expectedVersion, bool collapseSelection, out int skipped)
    {
        ThrowIfDisposed();
        skipped = 0;
        var document = _document;
        VerifyUnchangedDocument(document, expectedVersion ?? throw new ArgumentNullException(nameof(expectedVersion)));
        if (IsReadOnly) return 0;
        var provider = ReadOnlySectionProvider;
        var edits = new List<(SimpleSegment Range, string Text)>();
        var previousEnd = 0;
        foreach (var candidate in candidates)
        {
            var match = candidate.Match ?? throw new ArgumentException("A replacement match is null.", nameof(candidates));
            if (match.Offset < previousEnd || match.Length < 0 || (long)match.Offset + match.Length > document.TextLength)
                throw new ArgumentException("Replacement ranges are invalid or overlap.", nameof(candidates));
            previousEnd = match.EndOffset;
            var allowed = true;
            if (match.Length == 0) allowed = provider.CanInsert(match.Offset);
            else
            {
                var ranges = GetEditableSegments(match.Offset, match.Length);
                var length = 0;
                foreach (var range in ranges) length += range.Length;
                allowed = length == match.Length;
                if (allowed && candidate.Text.Length != 0) allowed = provider.CanInsert(match.Offset);
            }
            VerifyPlan();
            if (allowed) edits.Add((new SimpleSegment(match.Offset, match.Length), candidate.Text));
            else skipped++;
        }
        if (edits.Count == 0) return 0;
        _transaction++;
        try
        {
            using (document.RunUpdate())
            {
                // User UpdateStarted callbacks can change the document or
                // provider. Reject that change before consuming planned offsets.
                VerifyPlan();
                for (var i = edits.Count - 1; i >= 0; i--)
                {
                    var edit = edits[i];
                    document.Replace(edit.Range.Offset, edit.Range.Length, edit.Text);
                }
                if (collapseSelection) _anchor = _caret = edits[0].Range.Offset + edits[0].Text.Length;
                _changed = true;
            }
        }
        finally
        {
            _transaction--;
            if (_transaction == 0 && _changed) NotifyChanged();
        }
        return edits.Count;

        void VerifyPlan()
        {
            ThrowIfDisposed();
            VerifyUnchangedDocument(document, expectedVersion);
            if (IsReadOnly || !ReferenceEquals(provider, ReadOnlySectionProvider))
                throw new InvalidOperationException("Edit protection changed while planning search replacements.");
        }
    }
}
