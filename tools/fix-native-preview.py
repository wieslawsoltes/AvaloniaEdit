from pathlib import Path

path = Path('src/UnoEdit.Uno/Editing/TextArea.Extensibility.cs')
text = path.read_text()
edits = [
    ('        KeyUp += OnExtendedKeyUp;', '        PreviewKeyDown += OnExtendedPreviewKeyDown;\n        KeyUp += OnExtendedKeyUp;'),
    ('    private bool DispatchExtendedKey(KeyRoutedEventArgs e)\n    {', '''    private void OnExtendedPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_disposed || e.Handled || !IsEditorInputSource(e.OriginalSource)) return;
        foreach (var handler in _stackedHandlers.AsEnumerable().Reverse().ToArray())
            if (_stackedHandlers.Contains(handler) && !e.Handled) handler.OnPreviewKeyDown(e);
    }
    private bool DispatchExtendedKey(KeyRoutedEventArgs e)
    {'''),
    ('''        foreach (var handler in _stackedHandlers.AsEnumerable().Reverse().ToArray())
        {
            if (_stackedHandlers.Contains(handler) && !e.Handled) handler.OnPreviewKeyDown(e);
        }
''', ''),
    ('        KeyUp -= OnExtendedKeyUp;', '        PreviewKeyDown -= OnExtendedPreviewKeyDown;\n        KeyUp -= OnExtendedKeyUp;'),
]
for before, after in edits:
    if text.count(before) != 1:
        raise RuntimeError(f'Unexpected source while installing native preview routing: {before}')
    text = text.replace(before, after)
path.write_text(text)
