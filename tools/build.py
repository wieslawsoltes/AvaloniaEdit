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
import tempfile
import xml.etree.ElementTree as ET
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS = ROOT / 'artifacts'
DEMO = 'src/UnoEdit.Uno.Demo/UnoEdit.Uno.Demo.csproj'
TESTS = {
    'core': 'test/UnoEdit.Core.Tests/UnoEdit.Core.Tests.csproj',
    'rendering': 'test/UnoEdit.Rendering.Skia.Tests/UnoEdit.Rendering.Skia.Tests.csproj',
    'baseline': 'test/UnoEdit.Tests/UnoEdit.Tests.csproj',
}
PACKAGES = ['UnoEdit.Core', 'UnoEdit.Rendering.Skia', 'UnoEdit.Uno']


def run(*args: str, cwd: Path = ROOT) -> None:
    print('+ ' + ' '.join(args), flush=True)
    subprocess.run(args, cwd=cwd, check=True)


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
    (destination / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n', encoding='utf-8')


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
        'toolchain': json.loads((ROOT / 'global.json').read_text(encoding='utf-8')),
        'fullApiParity': False,
    }, indent=2) + '\n', encoding='utf-8')
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
    (directory / 'SHA256SUMS.txt').write_text('\n'.join(lines) + '\n', encoding='utf-8')


def validate_packages(directory: Path, version: str) -> list[dict]:
    """Reject stale/missing packages, incorrect dependencies and baseline leakage."""
    packages = sorted(directory.glob('*.nupkg'))
    if len(packages) != len(PACKAGES):
        raise RuntimeError(f'Expected {len(PACKAGES)} native packages; found {packages}')
    found = set()
    summary = []
    for path in packages:
        with ZipFile(path) as package:
            names = package.namelist()
            specs = [name for name in names if name.endswith('.nuspec')]
            if len(specs) != 1:
                raise RuntimeError(f'Invalid package manifest in {path}')
            root = ET.fromstring(package.read(specs[0]))
            metadata = root.find('{*}metadata')
            if metadata is None:
                raise RuntimeError(f'Missing metadata in {path}')
            identifier = metadata.findtext('{*}id')
            actual_version = metadata.findtext('{*}version')
            if identifier not in PACKAGES or identifier in found or actual_version != version:
                raise RuntimeError(f'Unexpected package identity: {identifier} {actual_version}')
            found.add(identifier)
            if metadata.findtext('{*}readme') != 'README.md' or 'README.md' not in names:
                raise RuntimeError(f'Missing compatibility README in {identifier}')
            if not any(name.startswith('lib/') and name.endswith('.dll') for name in names):
                raise RuntimeError(f'Package contains no compiled library: {identifier}')
            native_dependencies = set()
            for dependency in metadata.findall('.//{*}dependency'):
                name = dependency.get('id', '')
                if name.casefold().startswith('avalonia'):
                    raise RuntimeError(f'Native package {identifier} depends on the Avalonia baseline: {name}')
                if name in PACKAGES:
                    minimum = dependency.get('version', '').split(',')[0].strip('[]() ')
                    if minimum != version:
                        raise RuntimeError(f'Native package version mismatch: {identifier} -> {name} {minimum}, expected {version}')
                    native_dependencies.add(name)
            required = {
                'UnoEdit.Core': set(),
                'UnoEdit.Rendering.Skia': {'UnoEdit.Core'},
                'UnoEdit.Uno': {'UnoEdit.Core', 'UnoEdit.Rendering.Skia'},
            }[identifier]
            if not required.issubset(native_dependencies):
                raise RuntimeError(f'Missing native dependencies in {identifier}: {required - native_dependencies}')
        symbols = path.with_suffix('.snupkg')
        if not symbols.is_file():
            raise RuntimeError(f'Missing symbol package: {symbols}')
        with ZipFile(symbols) as package:
            if not any(name.endswith('.pdb') for name in package.namelist()):
                raise RuntimeError(f'Symbol package has no portable PDB: {symbols}')
        summary.append({'id': identifier, 'version': actual_version, 'dependencies': sorted(native_dependencies)})
    return summary


def verify_package_consumer(directory: Path, version: str, configuration: str) -> None:
    """Build outside the source tree, using only the newly packed NuGet binaries."""
    with tempfile.TemporaryDirectory(prefix='unoedit-package-consumer-') as temporary, \
            tempfile.TemporaryDirectory(prefix='unoedit-package-cache-') as cache:
        consumer = Path(temporary)
        shutil.copy2(ROOT / 'global.json', consumer / 'global.json')
        config = ET.Element('configuration')
        sources = ET.SubElement(config, 'packageSources')
        ET.SubElement(sources, 'clear')
        ET.SubElement(sources, 'add', {'key': 'built-native', 'value': str(directory.resolve())})
        ET.SubElement(sources, 'add', {'key': 'nuget.org', 'value': 'https://api.nuget.org/v3/index.json'})
        mapping = ET.SubElement(config, 'packageSourceMapping')
        native = ET.SubElement(mapping, 'packageSource', {'key': 'built-native'})
        ET.SubElement(native, 'package', {'pattern': 'UnoEdit.*'})
        external = ET.SubElement(mapping, 'packageSource', {'key': 'nuget.org'})
        ET.SubElement(external, 'package', {'pattern': '*'})
        ET.ElementTree(config).write(consumer / 'NuGet.Config', encoding='utf-8', xml_declaration=True)
        (consumer / 'Consumer.csproj').write_text(f'''<Project Sdk="Uno.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net10.0-desktop;net10.0-browserwasm</TargetFrameworks>
    <OutputType>Library</OutputType>
    <UnoFeatures>SkiaRenderer</UnoFeatures>
    <ImplicitUsings>disable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="UnoEdit.Uno" Version="{version}" /></ItemGroup>
</Project>
''', encoding='utf-8')
        (consumer / 'Consumer.cs').write_text('''using UnoEdit;
using UnoEdit.Document;
using UnoEdit.Editing;
using UnoEdit.Highlighting;

public static class PackageConsumer
{
    public static TextEditor Create()
    {
        var editor = new TextEditor
        {
            Text = "class PackagedConsumer { }",
            ShowLineNumbers = true,
            SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#")
        };
        editor.TextArea.ReadOnlySectionProvider =
            new TextSegmentReadOnlySectionProvider<TextSegment>(editor.Document);
        editor.Select(0, 5);
        editor.TextArea.ReplaceSelectionWithText("struct");
        return editor;
    }
}
''', encoding='utf-8')
        # An isolated cache prevents a previously installed package with the
        # same prerelease version from making this validation a false positive.
        # It is outside the consumer directory so SDK source/resource globs
        # cannot accidentally compile files shipped inside dependency packages.
        # Source mapping makes native packages come only from the artifact feed.
        run('dotnet', 'restore', 'Consumer.csproj', '--configfile', 'NuGet.Config',
            '--packages', cache, cwd=consumer)
        for framework in ('net10.0-desktop', 'net10.0-browserwasm'):
            run('dotnet', 'build', 'Consumer.csproj', '-c', configuration, '-f', framework, '--no-restore', cwd=consumer)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('target', choices=['all', 'test', 'core', 'rendering', 'baseline', 'desktop', 'browser', 'pack', 'site', 'verify-packages'])
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
    if args.target in ('all', 'pack', 'verify-packages'):
        destination = ARTIFACTS / 'packages'
        if args.target != 'verify-packages':
            if destination.exists():
                shutil.rmtree(destination)
            destination.mkdir(parents=True)
            for name in PACKAGES:
                run('dotnet', 'pack', f'src/{name}/{name}.csproj', '-c', args.configuration,
                    f'-p:Version={args.version}', f'-p:PackageVersion={args.version}', '-o', str(destination))
        summary = validate_packages(destination, args.version)
        verify_package_consumer(destination, args.version, args.configuration)
        (destination / 'validation.json').write_text(json.dumps({
            'packages': summary, 'independentConsumerBuilds': ['net10.0-desktop', 'net10.0-browserwasm']
        }, indent=2) + '\n', encoding='utf-8')
        checksums(destination)
        print('All three native packages, symbols and independent desktop/browser consumers validated.', flush=True)


if __name__ == '__main__':
    try:
        main()
    except (OSError, RuntimeError, subprocess.CalledProcessError) as error:
        print(f'Build failed: {error}', file=sys.stderr)
        raise SystemExit(1)
