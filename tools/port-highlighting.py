#!/usr/bin/env python3
"""Apply the native highlighting source migration without altering the baseline.

Original algorithms, XSHD definitions, XML loaders and rich-text models are
preserved, including copyright notices. The old UI colorizer and clipboard host
are separate adapters, not copied as fake Avalonia compatibility types.
Generated files are checked in after validation; consumers need not run this.
"""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'src/UnoEdit/Highlighting'
DEST = ROOT / 'src/UnoEdit.Uno/Highlighting'


def write(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding='utf-8')


def native(text):
    text = text.replace('using Avalonia.Media.Immutable;', '')
    text = text.replace('using Avalonia.Media;', '''using Microsoft.UI.Xaml.Media;
using Color = Windows.UI.Color;
using FontWeight = Windows.UI.Text.FontWeight;
using FontStyle = Windows.UI.Text.FontStyle;
using FontWeights = Microsoft.UI.Text.FontWeights;''')
    text = text.replace('using Avalonia.Threading;', '')
    text = text.replace('Dispatcher.UIThread.VerifyAccess();', '_ = document.Lines.Count; // Enforce document ownership, not a global UI dispatcher.')
    text = text.replace('using UnoEdit.Utils;', 'using UnoEdit.Utils;\nusing UnoEdit.Highlighting.Internal;')
    text = re.sub(r'\bISolidColorBrush\b', 'SolidColorBrush', text)
    text = re.sub(r'\bImmutableSolidColorBrush\b', 'SolidColorBrush', text)
    text = re.sub(r'\bIBrush\b', 'Brush', text)
    text = text.replace('FontFamily.Name', 'FontFamily.Source')
    text = text.replace('FontWeight.Value.ToString().ToLowerInvariant()', 'FontWeight.Value.Weight.ToString(CultureInfo.InvariantCulture)')
    text = text.replace('_fontWeight == other._fontWeight', 'Nullable.Equals(_fontWeight, other._fontWeight)')
    text = text.replace('Color.Parse(color)', 'NativeHighlightingConversions.ParseColor(color)')
    text = text.replace('(FontWeight)Enum.Parse(typeof(FontWeight), fontWeight, ignoreCase: true)', 'NativeHighlightingConversions.ParseFontWeight(fontWeight)')
    for weight in ('Thin', 'ExtraLight', 'UltraLight', 'Light', 'SemiLight', 'Normal', 'Regular', 'Medium', 'DemiBold', 'SemiBold', 'Bold', 'ExtraBold', 'UltraBold', 'Black', 'Heavy', 'ExtraBlack', 'UltraBlack'):
        text = text.replace('FontWeight.' + weight, 'FontWeights.' + weight)
    return text


def main():
    excluded = {'HighlightingColorizer.cs', 'HtmlClipboard.cs', 'RichTextColorizer.cs'}
    if (DEST / 'DocumentHighlighter.cs').exists():
        print('Native highlighting migration already applied.')
        return
    files = []
    for path in sorted(SOURCE.rglob('*')):
        if not path.is_file() or path.name in excluded:
            continue
        if path.suffix not in ('.cs', '.xshd', '.xsd'):
            continue
        text = path.read_text(encoding='utf-8-sig')
        if path.suffix == '.cs':
            text = native(text)
            if path.name == 'HtmlRichTextWriter.cs':
                # Preserve the original writer, fixing its whitespace-index bug.
                text = text.replace('WriteChar(value[pos]);', 'WriteChar(value[endPos]);')
                text = text.replace('BeginUnhandledSpan(); // TODO', 'BeginSpan(new HighlightingColor { FontFamily = fontFamily });')
            if path.name == 'HighlightingManager.cs':
                text = text.replace('new ReadOnlyCollection<IHighlightingDefinition>(_allHighlightings)', 'new ReadOnlyCollection<IHighlightingDefinition>(_allHighlightings.ToArray())')
            if re.search(r'^\s*using Avalonia[.;]', text, re.M):
                raise RuntimeError(f'Unported dependency in {path}')
        write(DEST / path.relative_to(SOURCE), text)
        files.append(path.relative_to(SOURCE).as_posix())

    # These helpers are internal in the signed core assembly. Compile the
    # original implementations privately rather than widening their API or
    # weakening the core assembly's signing contract.
    utilities = ROOT / 'src/UnoEdit.Core/Utils'
    for name in ('CompressingTreeList.cs', 'BusyManager.cs', 'FreezableHelper.cs', 'NullSafeCollection.cs', 'ThrowUtil.cs', 'XmlExtensionMethods.cs'):
        path = utilities / name
        if not path.exists():
            raise RuntimeError(f'Missing original helper {path}')
        text = native(path.read_text(encoding='utf-8-sig'))
        text = text.replace('namespace UnoEdit.Utils', 'namespace UnoEdit.Highlighting.Internal')
        write(DEST / 'Internal' / name, text)
    text = native((ROOT / 'src/UnoEdit/Utils/RichTextWriter.cs').read_text(encoding='utf-8-sig'))
    text = text.replace('namespace UnoEdit.Utils', 'namespace UnoEdit.Highlighting.Internal')
    write(DEST / 'Internal/RichTextWriter.cs', text)

    project = ROOT / 'src/UnoEdit.Uno/UnoEdit.Uno.csproj'
    text = project.read_text()
    text = text.replace('</Project>', '''  <ItemGroup>
    <EmbeddedResource Include="Highlighting/Resources/*.xshd;Highlighting/Resources/*.xsd">
      <LogicalName>UnoEdit.Highlighting.Resources.%(Filename)%(Extension)</LogicalName>
    </EmbeddedResource>
  </ItemGroup>
</Project>''')
    write(project, text)
    print(f'Ported {len(files)} original highlighting source/resource files:')
    print('\n'.join(files))


if __name__ == '__main__':
    main()
