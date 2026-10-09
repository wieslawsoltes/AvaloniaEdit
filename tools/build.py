#!/usr/bin/env python3
"""Reproducible UnoEdit build entry point (Python 3.10+, .NET from global.json).

No framework fallback, ignored test failure or automatic source rewrite occurs.
Browser publishing requires the wasm-tools workload; install it explicitly with
--install-workloads. All subprocess arguments are passed without a shell.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS = ROOT / 'artifacts'
DEMO = 'src/UnoEdit.Uno.Demo/UnoEdit.Uno.Demo.csproj'
TESTS = {
    'core': 'test/UnoEdit.Core.Tests/UnoEdit.Core.Tests.csproj',
    'rendering': 'test/UnoEdit.Rendering.Skia.Tests/UnoEdit.Rendering.Skia.Tests.csproj',
    'baseline': 'test/UnoEdit.Tests/UnoEdit.Tests.csproj',
}
PACKAGES = ['UnoEdit.Core', 'UnoEdit.Rendering.Skia', 'UnoEdit.Uno']


def run(*args: str) -> None:
    print('+ ' + ' '.join(args), flush=True)
    subprocess.run(args, cwd=ROOT, check=True)


def check_test_result(result: Path) -> dict:
    if not result.is_file():
        raise RuntimeError(f'Test runner did not create {result}')
    root = ET.parse(result).getroot()
    counters = root.find('.//{*}Counters')
    if counters is None:
        raise RuntimeError(f'Missing test counters in {result}')
    values = {key: int(counters.get(key, '0')) for key in ('total', 'executed', 'passed', 'failed', 'error', 'timeout', 'aborted', 'notExecuted')}
    if values['executed'] <= 0 or any(values[key] for key in ('failed', 'error', 'timeout', 'aborted')):
        raise RuntimeError(f'Invalid or failed test run: {values}')
    print(json.dumps(values, sort_keys=True), flush=True)
    return values


def test(name: str, configuration: str) -> None:
    destination = ARTIFACTS / 'tests' / name
    destination.mkdir(parents=True, exist_ok=True)
    trx = destination / f'{name}.trx'
    trx.unlink(missing_ok=True)
    run('dotnet', 'test', TESTS[name], '-c', configuration,
        '--logger', f'trx;LogFileName={trx.name}', '--results-directory', str(destination))
    summary = check_test_result(trx)
    (destination / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n')


def prepare_site() -> None:
    browser = ARTIFACTS / 'browser'
    indexes = sorted(browser.rglob('index.html'))
    if len(indexes) != 1:
        raise RuntimeError(f'Expected one browser index.html; found {indexes}')
    destination = ARTIFACTS / 'site'
    if destination.exists():
        shutil.rmtree(destination)
    shutil.copytree(indexes[0].parent, destination)
    (destination / '.nojekyll').touch()
    revision = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    (destination / 'build.json').write_text(json.dumps({
        'name': 'UnoEdit', 'status': 'preview', 'revision': revision,
        'toolchain': json.loads((ROOT / 'global.json').read_text()),
        'fullApiParity': False,
    }, indent=2) + '\n')
    print(f'Pages site prepared in {destination}', flush=True)


def checksums(directory: Path) -> None:
    lines = []
    for file in sorted(directory.iterdir()):
        if file.is_file() and file.name != 'SHA256SUMS.txt':
            digest = hashlib.sha256()
            with file.open('rb') as stream:
                for chunk in iter(lambda: stream.read(1024 * 1024), b''):
                    digest.update(chunk)
            lines.append(f'{digest.hexdigest()}  {file.name}')
    (directory / 'SHA256SUMS.txt').write_text('\n'.join(lines) + '\n')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('target', choices=['all', 'test', 'core', 'rendering', 'baseline', 'desktop', 'browser', 'pack', 'site'])
    parser.add_argument('--configuration', default='Release', choices=['Debug', 'Release'])
    parser.add_argument('--version', default='0.1.0-alpha.1')
    parser.add_argument('--base-path', default='/AvaloniaEdit/')
    parser.add_argument('--install-workloads', action='store_true')
    args = parser.parse_args()
    if not re.fullmatch(r'\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?', args.version):
        parser.error('--version must be a SemVer version without build metadata')
    if not re.fullmatch(r'/[A-Za-z0-9._/-]*', args.base_path) or '..' in args.base_path:
        parser.error('--base-path must be an absolute URL path without traversal')
    base = args.base_path.rstrip('/') + '/'
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    if args.install_workloads:
        run('dotnet', 'workload', 'install', 'wasm-tools', '--skip-manifest-update')
    if args.target in ('all', 'test'):
        for name in TESTS:
            test(name, args.configuration)
    elif args.target in TESTS:
        test(args.target, args.configuration)
    if args.target in ('all', 'desktop'):
        run('dotnet', 'build', DEMO, '-c', args.configuration, '-f', 'net10.0-desktop',
            f'-bl:{ARTIFACTS / "desktop.binlog"}')
    if args.target in ('all', 'browser'):
        destination = ARTIFACTS / 'browser'
        if destination.exists():
            shutil.rmtree(destination)
        run('dotnet', 'publish', DEMO, '-c', args.configuration, '-f', 'net10.0-browserwasm',
            f'-p:WasmShellWebAppBasePath={base}', '-o', str(destination),
            f'-bl:{ARTIFACTS / "browser.binlog"}')
        prepare_site()
    if args.target == 'site':
        prepare_site()
    if args.target in ('all', 'pack'):
        destination = ARTIFACTS / 'packages'
        destination.mkdir(parents=True, exist_ok=True)
        for name in PACKAGES:
            run('dotnet', 'pack', f'src/{name}/{name}.csproj', '-c', args.configuration,
                f'-p:Version={args.version}', f'-p:PackageVersion={args.version}', '-o', str(destination))
        checksums(destination)


if __name__ == '__main__':
    try:
        main()
    except (OSError, RuntimeError, subprocess.CalledProcessError) as error:
        print(f'Build failed: {error}', file=sys.stderr)
        raise SystemExit(1)
