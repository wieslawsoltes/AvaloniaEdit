#!/usr/bin/env python3
"""One-time, idempotent source migration from the validated UnoEdit identity baseline.

Moves the original implementations, rather than replacing them. The migration
workflow builds both target frameworks and executes both the original suite and
the UI-independent suite before committing the resulting source tree.
"""
from pathlib import Path
import json
import shutil

ROOT = Path(__file__).resolve().parents[1]
KEY = ('0024000004800000940000000602000000240000525341310004000001000100'
       'c1bba1142285fe0419326fb25866ba62c47e6c2b5c1ab0c95b46413fad3754712'
       '32cb81706932e1cef38781b9ebd39d5100401bacb651c6c5bbf59e571e81b3bc08'
       'd2a622004e08b1a6ece82a7e0b9857525c86d2b95fab4bc3dce148558d7f3ae61aa'
       '3a234086902aeface87d9dfdd32b9d2fe3c6dd4055b5ab4b104998bd87')

# Verified against the exported types of the compiled core assembly. Nested
# public types are forwarded by forwarding their enclosing type.
FORWARDS = {
    'CodeCompletion': 'CompletionAcceptAction',
    'Document': '''AnchorMovementType AnchorSegment CaretPositioningMode CharacterClass
        DocumentChangedEventArgs DocumentChangeEventArgs DocumentLine DocumentTextWriter
        IDocument IDocumentLine ILineTracker ISegment ITextAnchor ITextSource ITextSourceVersion
        IUndoableOperation LogicalDirection OffsetChangeMap OffsetChangeMapEntry
        OffsetChangeMappingType RopeTextSource SegmentExtensions SimpleSegment StringTextSource
        TextAnchor TextChangeEventArgs TextDocument TextDocumentWeakEventManager TextLocation
        TextLocationConverter TextSegment TextSegmentCollection<> TextSourceVersionProvider
        TextUtilities UndoStack WeakLineTracker''',
    'Indentation.CSharp': 'CSharpIndentationStrategy IDocumentAccessor TextDocumentAccessor',
    'Indentation': 'DefaultIndentationStrategy IIndentationStrategy',
    'Rendering': 'CollapsedLineSection',
    'Search': 'ISearchResult ISearchStrategy SearchMode SearchPatternException SearchStrategyFactory',
    '': 'TextEditorOptions TextViewPosition',
    'Utils': 'CharRope FileReader IServiceContainer Rope<> RopeTextReader ServiceExtensions StringSegment WeakEventManagerBase<,,,>',
}


def write(relative, content):
    path = ROOT / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding='utf-8')


def main():
    source = ROOT / 'src/UnoEdit'
    core = ROOT / 'src/UnoEdit.Core'
    if (core / 'Document/TextDocument.cs').exists():
        if (source / 'Document/TextDocument.cs').exists():
            raise RuntimeError('Both source locations exist; refusing an ambiguous migration')
        print('Core extraction already applied; no files changed.')
        return
    if not (source / 'Document/TextDocument.cs').is_file():
        raise RuntimeError('Expected the validated UnoEdit identity baseline')

    paths = [p for p in (source / 'Document').glob('*.cs') if p.name != 'DataObjectCopyingEventArgs.cs']
    paths += list((source / 'Indentation').rglob('*.cs'))
    ui_utilities = {'DataObjectEx.cs', 'ExtensionMethods.cs', 'PixelSnapHelpers.cs',
                    'RichTextWriter.cs', 'TextFormatterFactory.cs'}
    paths += [p for p in (source / 'Utils').glob('*.cs') if p.name not in ui_utilities]
    paths += [source / name for name in (
        'TextEditorOptions.cs', 'TextViewPosition.cs', 'CodeCompletion/CompletionAcceptAction.cs',
        'Rendering/HeightTree.cs', 'Rendering/HeightTreeNode.cs', 'Rendering/HeightTreeLineNode.cs',
        'Rendering/CollapsedLineSection.cs', 'Search/ISearchStrategy.cs',
        'Search/RegexSearchStrategy.cs', 'Search/SearchStrategyFactory.cs')]
    for path in paths:
        if not path.is_file() or (core / path.relative_to(source)).exists():
            raise RuntimeError(f'Cannot safely move {path}')
    for path in paths:
        relative = path.relative_to(source)
        destination = core / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(path, destination)
        edits = {
            'Document/TextDocument.cs': [('private void VerifyAccess()', 'internal void VerifyAccess()')],
            'Document/DocumentLineTree.cs': [('using Avalonia.Threading;', ''),
                ('Dispatcher.UIThread.VerifyAccess();', '_document.VerifyAccess();')],
            'Document/TextSegmentCollection.cs': [('using Avalonia.Threading;', ''),
                ('Dispatcher.UIThread.VerifyAccess();', 'textDocument.VerifyAccess();')],
        }.get(relative.as_posix(), [])
        if relative.as_posix() in ('Rendering/HeightTree.cs', 'Utils/CharRope.cs', 'Utils/Rope.cs'):
            edits += [('namespace UnoEdit.', 'using UnoEdit.Internal;\n\nnamespace UnoEdit.')]
        if edits:
            text = destination.read_text(encoding='utf-8-sig')
            for before, after in edits:
                text = text.replace(before, after)
            destination.write_text(text, encoding='utf-8')

    write('src/UnoEdit.Core/UnoEdit.Core.csproj', '''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
    <PackageId>UnoEdit.Core</PackageId>
    <Description>Framework-independent document, rope, undo, line indexing, search and indentation engine for UnoEdit.</Description>
    <PackageTags>uno;editor;rope;text-editor;document</PackageTags>
  </PropertyGroup>
</Project>
''')
    write('src/UnoEdit.Core/Internal/CoreExtensions.cs', '''using System;
using System.Collections.Immutable;

namespace UnoEdit.Internal;

// Private helpers live outside UnoEdit.Utils, preserving the UI assembly's
// public ExtensionMethods API without ambiguous extension resolution.
internal static class CoreExtensions
{
    internal static bool IsClose(this double left, double right) =>
        left == right || Math.Abs(left - right) < 0.01;

    internal static T PeekOrDefault<T>(this ImmutableStack<T> stack) =>
        stack.IsEmpty ? default : stack.Peek();
}
''')
    write('src/UnoEdit.Core/Properties/InternalsVisibleTo.cs',
          'using System.Runtime.CompilerServices;\n\n' + ''.join(
              f'[assembly: InternalsVisibleTo("{name}, PublicKey={KEY}")]\n'
              for name in ('UnoEdit', 'UnoEdit.Tests', 'UnoEdit.Core.Tests', 'UnoEdit.TextMate')))
    types = sorted('UnoEdit.' + (ns + '.' if ns else '') + name
                   for ns, names in FORWARDS.items() for name in names.split())
    write('src/UnoEdit/Properties/CoreTypeForwarders.cs', ''.join(
        f'[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(global::{name}))]\n'
        for name in types))
    project = source / 'UnoEdit.csproj'
    project.write_text(project.read_text(encoding='utf-8-sig').replace('</Project>', '''  <ItemGroup>
    <ProjectReference Include="../UnoEdit.Core/UnoEdit.Core.csproj" />
  </ItemGroup>
</Project>'''), encoding='utf-8')
    solution = ROOT / 'UnoEdit.slnx'
    solution.write_text(solution.read_text().replace('</Solution>', '''  <Project Path="src/UnoEdit.Core/UnoEdit.Core.csproj" />
  <Project Path="test/UnoEdit.Core.Tests/UnoEdit.Core.Tests.csproj" />
</Solution>'''), encoding='utf-8')
    write('test/UnoEdit.Core.Tests/UnoEdit.Core.Tests.csproj', '''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/UnoEdit.Core/UnoEdit.Core.csproj" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="NUnit" />
    <PackageReference Include="NUnit3TestAdapter" />
    <Compile Include="../UnoEdit.Tests/Document/*.cs" Link="Document/%(Filename)%(Extension)" />
    <Compile Include="../UnoEdit.Tests/Utils/*.cs" Exclude="../UnoEdit.Tests/Utils/ExtensionMethodsTests.cs" Link="Utils/%(Filename)%(Extension)" />
    <Compile Include="../UnoEdit.Tests/Search/FindTests.cs" Link="Search/FindTests.cs" />
  </ItemGroup>
</Project>
''')
    write('test/UnoEdit.Core.Tests/CoreTestExtensions.cs', '''using System.Collections.Generic;

namespace UnoEdit.Utils;

// The original collection tests use the UI assembly's public AddRange helper.
// Their collection operations stay identical when run without a UI assembly.
internal static class CoreTestExtensions
{
    internal static void AddRange<T>(this ICollection<T> collection, IEnumerable<T> items)
    {
        foreach (var item in items)
            collection.Add(item);
    }
}
''')
    write('global.json', json.dumps({
        'sdk': {'version': '10.0.401', 'rollForward': 'latestPatch', 'allowPrerelease': False},
        'msbuild-sdks': {'Uno.Sdk': '6.7.30'}
    }, indent=2) + '\n')
    print(f'Moved {len(paths)} implementations; preserved {len(types)} public type forwards.')


if __name__ == '__main__':
    main()
