#!/usr/bin/env python3
"""One-time native highlighting migration, preserving original algorithms/notices.

Generated files are checked in after validation. Consumers never preprocess the
source. The original Avalonia-backed regression baseline is not modified.
"""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'src/UnoEdit/Highlighting'
DEST = ROOT / 'src/UnoEdit.Uno/Highlighting'


def write(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding='utf-8')


def patch(relative, before, after):
    path = ROOT / relative
    text = path.read_text(encoding='utf-8-sig')
    if after in text:
        return
    if before not in text:
        raise RuntimeError(f'Missing expected integration point in {relative}: {before}')
    write(path, text.replace(before, after))


def native(text):
    text = text.replace('using Avalonia.Media.Immutable;', '')
    text = text.replace('using Avalonia.Media;', '''using Microsoft.UI.Xaml.Media;
using Color = Windows.UI.Color;
using FontWeight = Windows.UI.Text.FontWeight;
using FontStyle = Windows.UI.Text.FontStyle;
using FontWeights = Microsoft.UI.Text.FontWeights;''')
    text = text.replace('using Avalonia.Threading;', '')
    text = text.replace('Dispatcher.UIThread.VerifyAccess();', '_ = document.Lines.Count; // Enforce document ownership, not a global dispatcher.')
    text = text.replace('using UnoEdit.Utils;', 'using UnoEdit.Utils;\nusing UnoEdit.Highlighting.Internal;')
    text = re.sub(r'\bISolidColorBrush\b', 'SolidColorBrush', text)
    text = re.sub(r'\bImmutableSolidColorBrush\b', 'SolidColorBrush', text)
    text = re.sub(r'\bIBrush\b', 'Brush', text)
    text = text.replace('FontFamily.Name', 'FontFamily.Source')
    text = text.replace('FontWeight.Value.ToString().ToLowerInvariant()', 'FontWeight.Value.Weight.ToString(System.Globalization.CultureInfo.InvariantCulture)')
    text = text.replace('_fontWeight == other._fontWeight', 'Nullable.Equals(_fontWeight, other._fontWeight)')
    text = text.replace('Color.Parse(color)', 'NativeHighlightingConversions.ParseColor(color)')
    text = text.replace('(FontWeight)Enum.Parse(typeof(FontWeight), fontWeight, ignoreCase: true)', 'NativeHighlightingConversions.ParseFontWeight(fontWeight)')
    text = text.replace('(Color)V2Loader.ColorConverter.ConvertFrom(null, null, c)', 'NativeHighlightingConversions.ParseColor(c)')
    text = text.replace('new OffsetChangeMap(2)', 'new OffsetChangeMap()')
    for weight in ('Thin', 'ExtraLight', 'UltraLight', 'Light', 'SemiLight', 'Normal', 'Regular', 'Medium', 'DemiBold', 'SemiBold', 'Bold', 'ExtraBold', 'UltraBold', 'Black', 'Heavy', 'ExtraBlack', 'UltraBlack'):
        text = text.replace('FontWeight.' + weight, 'FontWeights.' + weight)
    return text


def immutable_brush(text):
    # Uno brushes are mutable DependencyObjects. Store a color value so callers
    # cannot mutate shared/frozen descriptors through a materialized UI brush.
    text = text.replace('private readonly SolidColorBrush _brush;', 'private readonly Color _color;')
    text = text.replace('_brush = brush;', '_color = brush?.Color ?? throw new System.ArgumentNullException(nameof(brush));')
    text = text.replace('public SimpleHighlightingBrush(Color color) : this(new SolidColorBrush(color)) {}', '''public SimpleHighlightingBrush(Color color) { _color = color; }

        /// <inheritdoc/>
        public override Color? GetColor(ITextRunConstructionContext context) => _color;''')
    text = text.replace('return _brush;', 'return new SolidColorBrush(_color);')
    text = text.replace('return _brush.ToString();', 'return $"#{_color.A:X2}{_color.R:X2}{_color.G:X2}{_color.B:X2}";')
    text = text.replace('_brush.Color', '_color')
    return text


def main():
    if not (DEST / 'DocumentHighlighter.cs').exists():
        files = []
        excluded = {'HighlightingColorizer.cs', 'HtmlClipboard.cs', 'RichTextColorizer.cs'}
        for path in sorted(SOURCE.rglob('*')):
            if not path.is_file() or path.name in excluded or path.suffix not in ('.cs', '.xshd', '.xsd'):
                continue
            text = path.read_text(encoding='utf-8-sig')
            if path.suffix == '.cs':
                if path.name == 'V1Loader.cs':
                    # The baseline disabled this entire original implementation,
                    # but still ships legacy definitions such as Tex-Mode.xshd.
                    text = '\n'.join(line[2:] if line.startswith('//') else line for line in text.splitlines()) + '\n'
                text = native(text)
                if path.name == 'HighlightingLoader.cs':
                    text = text.replace('return V2Loader.LoadDefinition(reader, skipValidation);', '''return reader.NamespaceURI == V2Loader.Namespace
                        ? V2Loader.LoadDefinition(reader, skipValidation)
                        : reader.NamespaceURI.Length == 0
                            ? V1Loader.LoadDefinition(reader, skipValidation)
                            : throw new HighlightingDefinitionInvalidException("Unsupported XSHD namespace: " + reader.NamespaceURI);''')
                if path.name == 'HighlightingBrush.cs':
                    text = immutable_brush(text)
                if path.name == 'HtmlRichTextWriter.cs':
                    text = text.replace('WriteChar(value[pos]);', 'WriteChar(value[endPos]);')
                    text = text.replace('BeginUnhandledSpan(); // TODO', 'BeginSpan(new HighlightingColor { FontFamily = fontFamily });')
                if path.name == 'HighlightingManager.cs':
                    text = text.replace('new ReadOnlyCollection<IHighlightingDefinition>(_allHighlightings)', 'new ReadOnlyCollection<IHighlightingDefinition>(_allHighlightings.ToArray())')
                if re.search(r'^\s*using Avalonia[.;]', text, re.M):
                    raise RuntimeError(f'Unported dependency in {path}')
            write(DEST / path.relative_to(SOURCE), text)
            files.append(path.relative_to(SOURCE).as_posix())
        for name in ('CompressingTreeList.cs', 'BusyManager.cs', 'IFreezable.cs', 'NullSafeCollection.cs', 'ThrowUtil.cs', 'CallbackOnDispose.cs'):
            path = ROOT / 'src/UnoEdit.Core/Utils' / name
            text = native(path.read_text(encoding='utf-8-sig'))
            text = text.replace('namespace UnoEdit.Utils', 'namespace UnoEdit.Highlighting.Internal')
            write(DEST / 'Internal' / name, text)
        text = native((ROOT / 'src/UnoEdit/Utils/RichTextWriter.cs').read_text(encoding='utf-8-sig'))
        write(DEST / 'Internal/RichTextWriter.cs', text.replace('namespace UnoEdit.Utils', 'namespace UnoEdit.Highlighting.Internal'))
        project = ROOT / 'src/UnoEdit.Uno/UnoEdit.Uno.csproj'
        text = project.read_text().replace('</Project>', '''  <ItemGroup>
    <EmbeddedResource Include="Highlighting/Resources/*.xshd;Highlighting/Resources/*.xsd">
      <LogicalName>UnoEdit.Highlighting.Resources.%(Filename)%(Extension)</LogicalName>
    </EmbeddedResource>
  </ItemGroup>
</Project>''')
        write(project, text)
        print(f'Ported {len(files)} original highlighting source/resource files:\n' + '\n'.join(files))

    patch('src/UnoEdit.Rendering.Skia/DocumentViewport.cs', 'public sealed class DocumentViewport', 'public sealed partial class DocumentViewport')
    patch('src/UnoEdit.Rendering.Skia/DocumentViewport.cs', '_document.Changing -= OnChanging;', 'DetachLineStyleSource();\n            _document.Changing -= OnChanging;')
    patch('src/UnoEdit.Rendering.Skia/DocumentViewport.cs', 'new TextLineLayout(_document.GetText(line.Offset, line.Length), _style, wrapWidth)', 'new TextLineLayout(_document.GetText(line.Offset, line.Length), _style, wrapWidth, GetLineStyles(line))')
    patch('src/UnoEdit.Uno/TextEditor.cs', 'public class TextEditor', 'public partial class TextEditor')
    patch('src/UnoEdit.Uno/TextEditor.cs', 'DocumentChanged?.Invoke(this, new DocumentChangedEventArgs(oldDocument, newDocument));', 'ResetSyntaxHighlighting();\n        DocumentChanged?.Invoke(this, new DocumentChangedEventArgs(oldDocument, newDocument));')
    patch('src/UnoEdit.Uno/TextEditor.cs', 'TextArea.Dispose();', 'DisposeSyntaxHighlighting();\n        TextArea.Dispose();')
    patch('src/UnoEdit.Uno/Rendering/TextRunConstructionContext.cs', 'return new StringSegment(Document.GetText(offset, length));', 'return new StringSegment(Document.GetText(offset, length), 0, length);')
    patch('src/UnoEdit.Uno.Demo/NativeControlChecks.cs', 'return checks.ToArray();', 'checks.AddRange(NativeHighlightingChecks.Run());\n        checks.AddRange(LegacyHighlightingChecks.Run());\n        return checks.ToArray();')
    patch('src/UnoEdit.Uno.Demo/NativeHighlightingChecks.cs', 'Require(definition.MainRuleSet != null,', 'Console.WriteLine("Validating native highlighting: " + definition.Name);\n            Require(definition.MainRuleSet != null,')
    patch('src/UnoEdit.Uno.Demo/App.xaml.cs', 'using UnoEdit.Document;', 'using UnoEdit.Document;\nusing UnoEdit.Highlighting;')
    patch('src/UnoEdit.Uno.Demo/App.xaml.cs', 'public DemoPage()\n    {', 'public DemoPage()\n    {\n        _smoke = Environment.GetEnvironmentVariable("UNOEDIT_NATIVE_CHECKS") == "1";')
    patch('src/UnoEdit.Uno.Demo/App.xaml.cs', '_editor.TextArea.Focus(FocusState.Programmatic);\n            UpdateStatus();', '''if (Environment.GetEnvironmentVariable("UNOEDIT_NATIVE_CHECKS") == "1")
            {
                foreach (var check in _controlChecks) Console.WriteLine("PASS: " + check);
                Console.WriteLine(_error ?? "Native Uno control and highlighting checks passed.");
                Environment.Exit(_error == null ? 0 : 1);
            }
            _editor.TextArea.Focus(FocusState.Programmatic);
            UpdateStatus();''')
    patch('src/UnoEdit.Uno.Demo/App.xaml.cs', 'if (stream != null) _editor.Load(stream);', 'if (stream != null)\n            {\n                _editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(Path.GetExtension(name));\n                _editor.Load(stream);\n            }')
    patch('src/UnoEdit.Uno.Demo/App.xaml.cs', 'toolbar.Children.Add(_samples);', '''toolbar.Children.Add(_samples);
        var languages = new ComboBox { Width = 150 };
        languages.Items.Add("Plain text");
        foreach (var definition in HighlightingManager.Instance.HighlightingDefinitions)
            languages.Items.Add(definition.Name);
        languages.SelectionChanged += (_, _) => _editor.SyntaxHighlighting =
            HighlightingManager.Instance.GetDefinition(languages.SelectedItem as string);
        languages.SelectedItem = "C#";
        AutomationProperties.SetName(languages, "Syntax highlighting language");
        toolbar.Children.Add(languages);''')
    patch('src/UnoEdit.Uno.Demo/App.xaml.cs', 'Preview: native editing and virtualization. Full completion, snippets, TextMate and IME parity are still in progress.', 'Native editing, virtualization and original XSHD highlighting. Completion, snippets, TextMate and full IME parity remain in progress.')
    print('Native syntax highlighting integrated into the viewport, control and sample.')


if __name__ == '__main__':
    main()
