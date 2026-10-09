using Microsoft.UI.Xaml;
using UnoEdit.Highlighting;

namespace UnoEdit;

public partial class TextEditor
{
    private DocumentHighlightingSource _syntaxSource;

    /// <summary>Identifies the native syntax-highlighting dependency property.</summary>
    public static readonly DependencyProperty SyntaxHighlightingProperty = DependencyProperty.Register(
        nameof(SyntaxHighlighting), typeof(IHighlightingDefinition), typeof(TextEditor),
        new PropertyMetadata(null, (sender, _) => ((TextEditor)sender).ResetSyntaxHighlighting()));

    /// <summary>
    /// Gets or sets the original XSHD highlighting definition. Null selects
    /// plain text. Definitions and their named colors can be shared by editors;
    /// each document/view receives its own incremental highlighting state.
    /// </summary>
    public IHighlightingDefinition SyntaxHighlighting
    {
        get => (IHighlightingDefinition)GetValue(SyntaxHighlightingProperty);
        set => SetValue(SyntaxHighlightingProperty, value);
    }

    /// <summary>Gets the active document highlighter, or null for plain text.</summary>
    public IHighlighter Highlighter => _syntaxSource?.Highlighter;

    private void ResetSyntaxHighlighting()
    {
        DisposeSyntaxHighlighting();
        if (_disposed || TextArea == null || Document == null || SyntaxHighlighting == null) return;
        _syntaxSource = new DocumentHighlightingSource(TextArea.TextView, SyntaxHighlighting);
        TextArea.TextView.Viewport.LineStyleSource = _syntaxSource;
    }

    private void DisposeSyntaxHighlighting()
    {
        if (_syntaxSource == null) return;
        if (ReferenceEquals(TextArea.TextView.Viewport.LineStyleSource, _syntaxSource))
            TextArea.TextView.Viewport.LineStyleSource = null;
        _syntaxSource.Dispose();
        _syntaxSource = null;
    }
}
