using System;
using System.Globalization;
using System.Linq;
using UnoEdit.Document;

namespace UnoEdit.Editing;

/// <summary>The replaceable native command and input-handler hierarchy.</summary>
public class TextAreaDefaultInputHandler : TextAreaInputHandler
{
    public TextAreaDefaultInputHandler(TextArea textArea) : base(textArea)
    {
        NestedInputHandlers.Add(CaretNavigation = CaretNavigationCommandHandler.Create(textArea));
        NestedInputHandlers.Add(Editing = EditingCommandHandler.Create(textArea));
        NestedInputHandlers.Add(MouseSelection = new SelectionMouseHandler(textArea));
        CommandBindings.Add(new RoutedCommandBinding(ApplicationCommands.Undo, (_, _) => textArea.Session.Undo(), (_, e) => e.CanExecute = !textArea.IsReadOnly && textArea.Document.UndoStack.CanUndo));
        CommandBindings.Add(new RoutedCommandBinding(ApplicationCommands.Redo, (_, _) => textArea.Session.Redo(), (_, e) => e.CanExecute = !textArea.IsReadOnly && textArea.Document.UndoStack.CanRedo));
    }
    public TextAreaInputHandler CaretNavigation { get; }
    public TextAreaInputHandler Editing { get; }
    public ITextAreaInputHandler MouseSelection { get; }
}
public class SelectionMouseHandler : TextAreaInputHandler
{
    public SelectionMouseHandler(TextArea textArea) : base(textArea) { }
}
public static class CaretNavigationCommandHandler
{
    public static TextAreaInputHandler Create(TextArea area)
    {
        var result = new TextAreaInputHandler(area);
        void Bind(RoutedCommand command, Action action) => result.CommandBindings.Add(new RoutedCommandBinding(command, (_, _) => action()));
        Bind(EditingCommands.MoveLeftByCharacter, () => area.Session.MoveHorizontal(-1));
        Bind(EditingCommands.MoveRightByCharacter, () => area.Session.MoveHorizontal(1));
        Bind(EditingCommands.SelectLeftByCharacter, () => area.Session.MoveHorizontal(-1, true));
        Bind(EditingCommands.SelectRightByCharacter, () => area.Session.MoveHorizontal(1, true));
        Bind(EditingCommands.MoveLeftByWord, () => area.Session.MoveHorizontal(-1, false, true));
        Bind(EditingCommands.MoveRightByWord, () => area.Session.MoveHorizontal(1, false, true));
        Bind(EditingCommands.SelectLeftByWord, () => area.Session.MoveHorizontal(-1, true, true));
        Bind(EditingCommands.SelectRightByWord, () => area.Session.MoveHorizontal(1, true, true));
        Bind(EditingCommands.MoveUpByLine, () => area.MoveByVisualLine(-area.TextView.DefaultLineHeight, false));
        Bind(EditingCommands.MoveDownByLine, () => area.MoveByVisualLine(area.TextView.DefaultLineHeight, false));
        Bind(EditingCommands.SelectUpByLine, () => area.MoveByVisualLine(-area.TextView.DefaultLineHeight, true));
        Bind(EditingCommands.SelectDownByLine, () => area.MoveByVisualLine(area.TextView.DefaultLineHeight, true));
        Bind(EditingCommands.MoveUpByPage, () => area.MoveByVisualLine(-area.TextView.ActualHeight, false));
        Bind(EditingCommands.MoveDownByPage, () => area.MoveByVisualLine(area.TextView.ActualHeight, false));
        Bind(EditingCommands.SelectUpByPage, () => area.MoveByVisualLine(-area.TextView.ActualHeight, true));
        Bind(EditingCommands.SelectDownByPage, () => area.MoveByVisualLine(area.TextView.ActualHeight, true));
        Bind(EditingCommands.MoveToLineStart, () => area.Session.MoveLineBoundary(false));
        Bind(EditingCommands.MoveToLineEnd, () => area.Session.MoveLineBoundary(true));
        Bind(EditingCommands.SelectToLineStart, () => area.Session.MoveLineBoundary(false, true));
        Bind(EditingCommands.SelectToLineEnd, () => area.Session.MoveLineBoundary(true, true));
        Bind(EditingCommands.MoveToDocumentStart, () => area.Session.MoveLineBoundary(false, false, true));
        Bind(EditingCommands.MoveToDocumentEnd, () => area.Session.MoveLineBoundary(true, false, true));
        Bind(EditingCommands.SelectToDocumentStart, () => area.Session.MoveLineBoundary(false, true, true));
        Bind(EditingCommands.SelectToDocumentEnd, () => area.Session.MoveLineBoundary(true, true, true));
        return result;
    }
}
public static class EditingCommandHandler
{
    public static TextAreaInputHandler Create(TextArea area)
    {
        var result = new TextAreaInputHandler(area);
        void Bind(RoutedCommand command, Action action, bool editable = true) => result.CommandBindings.Add(new RoutedCommandBinding(command, (_, _) => action(), (_, e) => e.CanExecute = !editable || !area.IsReadOnly));
        Bind(ApplicationCommands.Copy, area.Copy, false); Bind(ApplicationCommands.Cut, area.Cut);
        result.CommandBindings.Add(new RoutedCommandBinding(ApplicationCommands.Paste, async (_, _) => { try { await area.PasteAsync(); } catch (Exception error) { area.ReportExtendedError(error); } }, (_, e) => e.CanExecute = !area.IsReadOnly));
        Bind(ApplicationCommands.SelectAll, area.Session.SelectAll, false);
        Bind(ApplicationCommands.Delete, () => area.Session.Delete(false)); Bind(EditingCommands.Delete, () => area.Session.Delete(false));
        Bind(EditingCommands.DeleteNextWord, () => area.Session.Delete(false, true)); Bind(EditingCommands.Backspace, () => area.Session.Delete(true));
        Bind(EditingCommands.DeletePreviousWord, () => area.Session.Delete(true, true));
        Bind(EditingCommands.EnterParagraphBreak, area.Session.Enter); Bind(EditingCommands.EnterLineBreak, area.Session.Enter);
        Bind(EditingCommands.TabForward, () => area.Session.Indent()); Bind(EditingCommands.TabBackward, () => area.Session.Indent(true));
        Bind(UnoEditCommands.ToggleOverstrike, () => area.OverstrikeMode = !area.OverstrikeMode);
        Bind(UnoEditCommands.DeleteLine, () => { var line = area.Document.GetLineByOffset(area.Caret.Offset); area.Session.Select(line.Offset, line.TotalLength); area.Session.ReplaceSelection(string.Empty); });
        Bind(UnoEditCommands.ConvertToUppercase, () => Transform(area, s => s.ToUpper(CultureInfo.CurrentCulture)));
        Bind(UnoEditCommands.ConvertToLowercase, () => Transform(area, s => s.ToLower(CultureInfo.CurrentCulture)));
        Bind(UnoEditCommands.ConvertToTitleCase, () => Transform(area, s => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s)));
        Bind(UnoEditCommands.InvertCase, () => Transform(area, s => string.Concat(s.Select(c => char.IsUpper(c) ? char.ToLower(c) : char.ToUpper(c)))));
        Bind(UnoEditCommands.ConvertTabsToSpaces, () => Transform(area, s => s.Replace("\t", new string(' ', Math.Max(1, area.Options.IndentationSize)))));
        Bind(UnoEditCommands.ConvertSpacesToTabs, () => Transform(area, s => s.Replace(new string(' ', Math.Max(1, area.Options.IndentationSize)), "\t")));
        Bind(UnoEditCommands.RemoveLeadingWhitespace, () => TransformLines(area, s => s.TrimStart(' ', '\t')));
        Bind(UnoEditCommands.RemoveTrailingWhitespace, () => TransformLines(area, s => s.TrimEnd(' ', '\t')));
        Bind(UnoEditCommands.ConvertLeadingTabsToSpaces, () => TransformLines(area, s => Leading(s, x => x.Replace("\t", new string(' ', Math.Max(1, area.Options.IndentationSize))))));
        Bind(UnoEditCommands.ConvertLeadingSpacesToTabs, () => TransformLines(area, s => Leading(s, x => x.Replace(new string(' ', Math.Max(1, area.Options.IndentationSize)), "\t"))));
        Bind(UnoEditCommands.IndentSelection, () =>
        {
            var document = area.Document;
            var first = area.Selection.IsEmpty ? 1 : area.Selection.StartPosition.Line;
            var last = area.Selection.IsEmpty ? document.LineCount : area.Selection.EndPosition.Line;
            if (first > last) (first, last) = (last, first);
            // Run user-supplied indentation on an isolated copy; applying the
            // resulting per-line differences still honors protected sections.
            var copy = new TextDocument(document.Text);
            area.IndentationStrategy?.IndentLines(copy, first, last);
            if (copy.LineCount != document.LineCount) throw new InvalidOperationException("An indentation strategy must preserve the document's line structure.");
            using (document.RunUpdate())
                for (var number = last; number >= first; number--)
                { var line = document.GetLineByNumber(number); ReplaceWholeEditable(area, line, copy.GetText(copy.GetLineByNumber(number))); }
        });
        return result;
    }
    private static string Leading(string text, Func<string, string> transform)
    { var count = 0; while (count < text.Length && (text[count] == ' ' || text[count] == '\t')) count++; return transform(text.Substring(0, count)) + text.Substring(count); }
    private static void Transform(TextArea area, Func<string, string> transform)
    {
        if (area.Selection.IsEmpty) return;
        var range = area.Selection.SurroundingSegment;
        ReplaceWholeEditable(area, range, transform(area.Selection.GetText()));
    }
    private static void TransformLines(TextArea area, Func<string, string> transform)
    {
        var range = area.Selection.SurroundingSegment;
        var first = range == null ? 1 : area.Document.GetLineByOffset(range.Offset).LineNumber;
        var last = range == null ? area.Document.LineCount : area.Document.GetLineByOffset(range.EndOffset).LineNumber;
        using (area.Document.RunUpdate())
            for (var number = last; number >= first; number--)
            { var line = area.Document.GetLineByNumber(number); ReplaceWholeEditable(area, line, transform(area.Document.GetText(line))); }
    }
    private static void ReplaceWholeEditable(TextArea area, ISegment range, string text)
    {
        if (area.IsReadOnly || area.Document.GetText(range) == text) return;
        var document = area.Document; var version = document.Version;
        var permitted = range.Length == 0 ? area.ReadOnlySectionProvider.CanInsert(range.Offset) : area.GetDeletableSegments(range) is var segments && segments.Length == 1 && segments[0].Offset == range.Offset && segments[0].Length == range.Length;
        if (!ReferenceEquals(area.Document, document) || document.Version.CompareAge(version) != 0) throw new InvalidOperationException("The protection provider changed the document.");
        if (permitted) document.Replace(range.Offset, range.Length, text);
    }
}
