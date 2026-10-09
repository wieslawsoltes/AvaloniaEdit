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
    ('    private void OnExtendedFocus(object sender, RoutedEventArgs e) => RoutedCommand.SetFocusedTarget(this);', '    private void OnExtendedFocus(object sender, RoutedEventArgs e) { RoutedCommand.SetFocusedTarget(this); EnsureBrowserKeyboardFocus(); }'),
    ('        TextEntered?.Invoke(this, args);', '        TextEntered?.Invoke(this, args);\n        EnsureBrowserKeyboardFocus();'),
    ('    private void InitializeExtensibility()', '''#if __WASM__
    [System.Runtime.InteropServices.JavaScript.JSImport("globalThis.eval")]
    private static partial void InvokeBrowserFocusScript(string script);
#endif
    private void EnsureBrowserKeyboardFocus()
    {
#if __WASM__
        // Uno 6.7's canvas host retains managed keyboard focus while DOM focus
        // can remain on body. Its accessibility-entry shortcut then consumes
        // Tab before managed preview routing. Focus the existing canvas only
        // from an already focused editor; never hide the accessibility entry
        // or steal focus from an input, popup, or other browser element.
        if (!_focused || _disposed || !OperatingSystem.IsBrowser()) return;
        InvokeBrowserFocusScript("(() => { const a = document.activeElement; if (a && a !== document.body && a !== document.documentElement) return; const c = document.querySelector('canvas'); if (c) { if (!c.hasAttribute('tabindex')) c.tabIndex = -1; c.focus({ preventScroll: true }); } })()");
#endif
    }

    private void InitializeExtensibility()'''),
]
for before, after in edits:
    if text.count(before) != 1:
        raise RuntimeError(f'Unexpected source while installing native preview routing: {before}')
    text = text.replace(before, after)
path.write_text(text)
project = Path('src/UnoEdit.Uno/UnoEdit.Uno.csproj')
text = project.read_text()
before = '<ImplicitUsings>disable</ImplicitUsings>'
if text.count(before) != 1:
    raise RuntimeError('Unexpected native control project')
project.write_text(text.replace(before, '<AllowUnsafeBlocks>true</AllowUnsafeBlocks>\n    ' + before))
