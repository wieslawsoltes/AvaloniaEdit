"""Build-tool unit tests. Synthetic ZIPs test metadata gates, not binary execution."""
from __future__ import annotations

import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET
from zipfile import ZipFile

SPEC = importlib.util.spec_from_file_location('unoedit_build', Path(__file__).resolve().parents[1] / 'tools/build.py')
build = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(build)
VERSION = '0.1.0-alpha.1'


class PackageValidationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.directory = Path(self.temporary.name)
        for name in build.PACKAGES:
            self.make_package(name)

    def make_package(self, name, *, dependency_version=VERSION, baseline=False, readme=True, library=True):
        package = ET.Element('package', {'xmlns': 'http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd'})
        metadata = ET.SubElement(package, 'metadata')
        ET.SubElement(metadata, 'id').text = name
        ET.SubElement(metadata, 'version').text = VERSION
        ET.SubElement(metadata, 'readme').text = 'README.md'
        dependencies = ET.SubElement(metadata, 'dependencies')
        for dependency in {
            'UnoEdit.Core': [],
            'UnoEdit.Rendering.Skia': ['UnoEdit.Core'],
            'UnoEdit.Uno': ['UnoEdit.Core', 'UnoEdit.Rendering.Skia'],
        }[name]:
            ET.SubElement(dependencies, 'dependency', {'id': dependency, 'version': dependency_version})
        if baseline:
            ET.SubElement(dependencies, 'dependency', {'id': 'Avalonia', 'version': '12.0.0'})
        path = self.directory / f'{name}.{VERSION}.nupkg'
        with ZipFile(path, 'w') as archive:
            archive.writestr(name + '.nuspec', ET.tostring(package))
            if readme:
                archive.writestr('README.md', 'This is an API-incomplete preview.')
            if library:
                archive.writestr(f'lib/net10.0/{name}.dll', b'metadata-only-test-fixture')
        with ZipFile(path.with_suffix('.snupkg'), 'w') as archive:
            archive.writestr(f'lib/net10.0/{name}.pdb', b'metadata-only-test-fixture')
        return path

    def test_complete_package_set_is_accepted(self):
        result = build.validate_packages(self.directory, VERSION)
        self.assertEqual({entry['id'] for entry in result}, set(build.PACKAGES))

    def test_version_mismatch_is_rejected(self):
        self.make_package('UnoEdit.Uno', dependency_version='12.0.0')
        with self.assertRaisesRegex(RuntimeError, 'version mismatch'):
            build.validate_packages(self.directory, VERSION)

    def test_nuget_version_range_is_understood(self):
        self.make_package('UnoEdit.Uno', dependency_version=f'[{VERSION}, )')
        self.assertEqual(len(build.validate_packages(self.directory, VERSION)), 3)

    def test_avalonia_baseline_dependency_is_rejected(self):
        self.make_package('UnoEdit.Uno', baseline=True)
        with self.assertRaisesRegex(RuntimeError, 'Avalonia baseline'):
            build.validate_packages(self.directory, VERSION)

    def test_stale_extra_package_is_rejected(self):
        (self.directory / 'stale.nupkg').write_bytes(b'not-a-package')
        with self.assertRaisesRegex(RuntimeError, 'Expected 3'):
            build.validate_packages(self.directory, VERSION)

    def test_missing_symbol_package_is_rejected(self):
        next(self.directory.glob('*.snupkg')).unlink()
        with self.assertRaisesRegex(RuntimeError, 'Missing symbol package'):
            build.validate_packages(self.directory, VERSION)

    def test_missing_compatibility_readme_is_rejected(self):
        self.make_package('UnoEdit.Core', readme=False)
        with self.assertRaisesRegex(RuntimeError, 'README'):
            build.validate_packages(self.directory, VERSION)

    def test_package_without_library_is_rejected(self):
        self.make_package('UnoEdit.Uno', library=False)
        with self.assertRaisesRegex(RuntimeError, 'no compiled library'):
            build.validate_packages(self.directory, VERSION)

    def test_consumer_uses_isolated_cache_local_native_source_and_no_project_references(self):
        calls = []

        def inspect(*args, cwd):
            calls.append(args)
            project = ET.parse(cwd / 'Consumer.csproj').getroot()
            self.assertEqual(project.findall('.//ProjectReference'), [])
            self.assertEqual(project.find('.//PackageReference').get('Include'), 'UnoEdit.Uno')
            config = ET.parse(cwd / 'NuGet.Config').getroot()
            local = config.find('./packageSourceMapping/packageSource[@key="built-native"]/package')
            self.assertEqual(local.get('pattern'), 'UnoEdit.*')
            source = config.find('./packageSources/add[@key="built-native"]')
            self.assertEqual(Path(source.get('value')), self.directory.resolve())
            if args[1] == 'restore':
                self.assertIn('--configfile', args)
                cache = Path(args[args.index('--packages') + 1])
                self.assertTrue(cache.is_relative_to(cwd))
            else:
                self.assertIn('--no-restore', args)

        with patch.object(build, 'run', side_effect=inspect):
            build.verify_package_consumer(self.directory, VERSION, 'Release')
        self.assertEqual(len(calls), 3)
        self.assertEqual({call[call.index('-f') + 1] for call in calls[1:]}, {'net10.0-desktop', 'net10.0-browserwasm'})


class TestResultValidationTests(unittest.TestCase):
    def test_missing_result_is_not_a_pass(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(RuntimeError):
                build.check_test_result(Path(directory) / 'absent.trx')

    def check_counts(self, counters):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'result.trx'
            root = ET.Element('TestRun')
            ET.SubElement(ET.SubElement(root, 'ResultSummary'), 'Counters', counters)
            ET.ElementTree(root).write(path)
            return build.check_test_result(path)

    def test_zero_executed_tests_is_not_a_pass(self):
        with self.assertRaises(RuntimeError):
            self.check_counts({'total': '0', 'executed': '0', 'passed': '0'})

    def test_failed_result_is_not_a_pass(self):
        with self.assertRaises(RuntimeError):
            self.check_counts({'total': '2', 'executed': '2', 'passed': '1', 'failed': '1'})

    def test_valid_result_preserves_counts(self):
        result = self.check_counts({'total': '5', 'executed': '5', 'passed': '5', 'failed': '0'})
        self.assertEqual(result['passed'], 5)
        self.assertEqual(result['notExecuted'], 0)


if __name__ == '__main__':
    unittest.main()
