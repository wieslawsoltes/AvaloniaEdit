// Copyright (c) 2014 AlphaSierraPapa for the SharpDevelop Team.
// Adapted for native Uno Platform. Licensed under the repository MIT license.
using System;
using System.Collections.Generic;
using System.Linq;
using UnoEdit.Document;
using Windows.ApplicationModel.DataTransfer;

namespace UnoEdit.Editing;

/// <summary>Immutable selection descriptor. Caret movement does not mutate it.</summary>
public abstract class Selection
{
    protected Selection(TextArea textArea) => TextArea = textArea ?? throw new ArgumentNullException(nameof(textArea));
    internal TextArea TextArea { get; }
    public static Selection Create(TextArea textArea, int startOffset, int endOffset)
    {
        if (textArea == null) throw new ArgumentNullException(nameof(textArea));
        return new SimpleSelection(textArea, new TextViewPosition(textArea.Document.GetLocation(startOffset)), new TextViewPosition(textArea.Document.GetLocation(endOffset)));
    }
    public static Selection Create(TextArea textArea, ISegment segment)
    {
        if (segment == null) throw new ArgumentNullException(nameof(segment));
        return Create(textArea, segment.Offset, segment.EndOffset);
    }
    public static Selection Create(TextArea textArea, TextViewPosition start, TextViewPosition end) => new SimpleSelection(textArea, start, end);
    public abstract TextViewPosition StartPosition { get; }
    public abstract TextViewPosition EndPosition { get; }
    public abstract IEnumerable<SelectionSegment> Segments { get; }
    public abstract ISegment SurroundingSegment { get; }
    public abstract void ReplaceSelectionWithText(string newText);
    public abstract Selection UpdateOnDocumentChange(DocumentChangeEventArgs e);
    public virtual bool IsEmpty => Length == 0;
    public virtual bool EnableVirtualSpace => TextArea.Options.EnableVirtualSpace;
    public abstract int Length { get; }
    public abstract Selection SetEndpoint(TextViewPosition endPosition);
    public abstract Selection StartSelectionOrSetEndpoint(TextViewPosition startPosition, TextViewPosition endPosition);
    public virtual bool IsMultiline => !IsEmpty && StartPosition.Line != EndPosition.Line;
    public virtual string GetText() => string.Join(Environment.NewLine, Segments.Select(s => TextArea.Document.GetText(s.StartOffset, s.Length)));
    public virtual bool Contains(int offset) => Segments.Any(s => offset >= s.StartOffset && offset <= s.EndOffset);
    public virtual DataPackage CreateDataObject(TextArea textArea)
    {
        if (textArea == null) throw new ArgumentNullException(nameof(textArea));
        var data = new DataPackage(); data.SetText(GetText()); return data;
    }
    public abstract override bool Equals(object obj);
    public abstract override int GetHashCode();
}
internal sealed class SimpleSelection : Selection
{
    private readonly int _start, _end;
    private readonly TextViewPosition _startPosition, _endPosition;
    internal SimpleSelection(TextArea area, TextViewPosition start, TextViewPosition end) : base(area)
    {
        _start = area.Document.GetOffset(start.Line, start.Column); _end = area.Document.GetOffset(end.Line, end.Column);
        _startPosition = start; _endPosition = end;
    }
    public override TextViewPosition StartPosition => _startPosition;
    public override TextViewPosition EndPosition => _endPosition;
    public override IEnumerable<SelectionSegment> Segments
    {
        get { if (!IsEmpty) yield return new SelectionSegment(_start, _startPosition.VisualColumn, _end, _endPosition.VisualColumn); }
    }
    public override ISegment SurroundingSegment => IsEmpty ? null : new SimpleSegment(Math.Min(_start, _end), Length);
    public override int Length => Math.Abs(_end - _start);
    public override void ReplaceSelectionWithText(string newText)
    {
        if (newText == null) throw new ArgumentNullException(nameof(newText));
        if (IsEmpty) TextArea.Session.MoveTo(TextArea.Caret.Offset);
        else { TextArea.Session.MoveTo(_start); TextArea.Session.MoveTo(_end, true); }
        TextArea.Session.ReplaceSelection(newText);
    }
    public override Selection UpdateOnDocumentChange(DocumentChangeEventArgs e)
    {
        if (e == null) throw new ArgumentNullException(nameof(e));
        return Create(TextArea, e.GetNewOffset(_start, _start <= _end ? AnchorMovementType.BeforeInsertion : AnchorMovementType.AfterInsertion),
            e.GetNewOffset(_end, _start <= _end ? AnchorMovementType.AfterInsertion : AnchorMovementType.BeforeInsertion));
    }
    public override Selection SetEndpoint(TextViewPosition endPosition) => Create(TextArea, _startPosition, endPosition);
    public override Selection StartSelectionOrSetEndpoint(TextViewPosition startPosition, TextViewPosition endPosition) => Create(TextArea, IsEmpty ? startPosition : _startPosition, endPosition);
    public override bool Equals(object obj) => obj is SimpleSelection other && ReferenceEquals(TextArea, other.TextArea) && _startPosition.Equals(other._startPosition) && _endPosition.Equals(other._endPosition);
    public override int GetHashCode() => HashCode.Combine(TextArea, _startPosition, _endPosition);
}
