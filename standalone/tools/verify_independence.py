#!/usr/bin/env python3
"""Fail if an editor dependency or an outside project reference enters the standalone solution."""
from pathlib import Path
import json
import re
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
forbidden = ('avaloniaedit', 'avalonedit', 'scintilla', 'monaco', 'textmatesharp', 'protext')
projects = []
for project in sorted(root.rglob('*.csproj')):
    if any(part in ('obj', 'bin', 'artifacts') for part in project.relative_to(root).parts):
        continue
    tree = ET.parse(project)
    packages = [element.attrib.get('Include', '') for element in tree.iter('PackageReference')]
    assert not any(word in package.lower() for package in packages for word in forbidden), (project, packages)
    for element in tree.iter('ProjectReference'):
        target = (project.parent / element.attrib['Include']).resolve()
        assert target.is_relative_to(root), f'Outside project reference: {project} -> {target}'
        assert target.is_file(), target
    if project.stem == 'Scribe.Core':
        assert packages == [], 'Core acquired a package dependency'
    if project.stem == 'Scribe.Avalonia':
        assert packages == ['Avalonia'], 'Control library acquired a non-framework dependency'
    projects.append({'path': str(project.relative_to(root)), 'packages': packages})
for source in (root / 'src').rglob('*.cs'):
    if any(part in ('obj', 'bin') for part in source.parts):
        continue
    text = source.read_text(encoding='utf-8')
    assert not any(re.search(r'\b' + word + r'\b', text, re.I) for word in forbidden), f'Forbidden editor symbol in {source}'
viewport = (root / 'src/Scribe.Avalonia/EditorViewport.cs').read_text(encoding='utf-8')
assert re.search(r'class\s+EditorViewport\s*:\s*Control\b', viewport), 'Editing viewport no longer derives directly from Control'
editor = (root / 'src/Scribe.Avalonia/ScribeEditor.cs').read_text(encoding='utf-8')
assert re.search(r'class\s+ScribeEditor\s*:\s*TemplatedControl\b', editor), 'Editor is not directly templated'
assert 'new TextBox' not in viewport and 'new TextBox' not in editor, 'Document editor wraps TextBox'
assets = []
for name in ('Scribe.Core', 'Scribe.Avalonia'):
    path = root / f'src/{name}/obj/project.assets.json'
    if path.is_file():
        packages = sorted(json.loads(path.read_text())['libraries'])
        assert not any(word in package.lower() for package in packages for word in forbidden), f'Transitive editor dependency in {path}'
        assets.append({'project': name, 'resolvedLibraries': packages})
report = {'passed': True, 'coreNugetDependencies': 0, 'controlDirectPackageDependencies': ['Avalonia'], 'outsideProjectReferences': 0, 'viewportBase': 'Avalonia.Controls.Control', 'editorBase': 'Avalonia.Controls.Primitives.TemplatedControl', 'projects': projects, 'resolved': assets}
out = root / 'artifacts/validation'
out.mkdir(parents=True, exist_ok=True)
(out / 'independence.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print('SCRIBE_INDEPENDENCE: PASS; no existing editor libraries; no outside project references')
