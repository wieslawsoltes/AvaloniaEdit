#!/usr/bin/env python3
"""Rename editor identities without deleting implementation or changing framework APIs.

This is the mechanical first stage of the Uno migration. It is deliberately
separate from the native Uno backend changes so API/test regressions can be
attributed to the right stage. Historical copyright notices and web attribution
are retained. Run from the repository root; repeated runs are harmless.
"""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
TEXT_SUFFIXES = {'.cs', '.csproj', '.xaml', '.axaml', '.resx', '.props', '.targets', '.manifest', '.slnx', '.sln'}


def rename_text(text: str) -> str:
    # Preserve historical HTTPS URLs in comments, while changing avares assembly
    # names, namespaces, InternalsVisibleTo declarations and project identities.
    urls = []
    def protect(match):
        urls.append(match.group(0))
        return f'__UPSTREAM_URL_{len(urls)-1}__'
    protected = re.sub(r'https?://[^\s<>"\']+', protect, text)
    renamed = protected.replace('AvaloniaEdit', 'UnoEdit').replace('Avalonia.UnoEdit', 'UnoEdit')
    for index, url in enumerate(urls):
        renamed = renamed.replace(f'__UPSTREAM_URL_{index}__', url)
    return renamed


def main() -> None:
    paths = [p for base in ('src', 'test') for p in (ROOT / base).rglob('*') if p.is_file() and p.suffix in TEXT_SUFFIXES]
    paths += [p for p in ROOT.iterdir() if p.suffix in ('.sln', '.slnx')]
    for path in paths:
        raw = path.read_bytes()
        bom = raw.startswith(b'\xef\xbb\xbf')
        text = raw.decode('utf-8-sig')
        updated = rename_text(text)
        if updated != text:
            path.write_bytes((b'\xef\xbb\xbf' if bom else b'') + updated.encode('utf-8'))
    # Rename children before parents. No file is removed or substituted.
    for base in ('src', 'test'):
        for path in sorted((ROOT / base).rglob('*'), key=lambda p: len(p.parts), reverse=True):
            if 'AvaloniaEdit' in path.name:
                destination = path.with_name(path.name.replace('AvaloniaEdit', 'UnoEdit'))
                if destination.exists():
                    raise RuntimeError(f'Refusing to overwrite {destination}')
                path.rename(destination)
    for path in list(ROOT.glob('AvaloniaEdit.sln*')):
        path.rename(path.with_name(path.name.replace('AvaloniaEdit', 'UnoEdit')))
    print('Editor namespaces, source paths, resources and solution renamed to UnoEdit.')


if __name__ == '__main__':
    main()
