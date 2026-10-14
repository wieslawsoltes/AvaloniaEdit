#!/usr/bin/env python3
"""Build and execute a clean NuGet-only consumer outside the hosting repository."""
from pathlib import Path
import json
import os
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile
root = Path(__file__).resolve().parents[1]
packages = root / 'artifacts/packages'
output = root / 'artifacts/validation'
output.mkdir(parents=True, exist_ok=True)
version = '0.2.0-preview.1'
expected = [f'Scribe.Core.{version}.nupkg', f'Scribe.Avalonia.{version}.nupkg']
for name in expected:
    with zipfile.ZipFile(packages / name) as archive:
        assert archive.testzip() is None
        assert 'README.md' in archive.namelist() and 'LICENSE' in archive.namelist()
        assert not any(p.lower().endswith(('.ttf', '.otf', '.woff', '.woff2')) for p in archive.namelist())
        nuspec = ET.fromstring(archive.read(next(p for p in archive.namelist() if p.endswith('.nuspec'))))
        dependencies = [e.attrib['id'] for e in nuspec.iter() if e.tag.endswith('dependency')]
        assert set(dependencies) <= {'Avalonia', 'Scribe.Core'}, dependencies
with tempfile.TemporaryDirectory(prefix='scribe-isolated-consumer-') as temporary:
    target = Path(temporary)
    config = ET.Element('configuration')
    sources = ET.SubElement(config, 'packageSources')
    ET.SubElement(sources, 'clear')
    ET.SubElement(sources, 'add', key='built', value=str(packages))
    ET.SubElement(sources, 'add', key='nuget.org', value='https://api.nuget.org/v3/index.json')
    ET.ElementTree(config).write(target / 'NuGet.config', encoding='utf-8', xml_declaration=True)
    (target / 'Consumer.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup><PackageReference Include="Scribe.Avalonia" Version="{version}"/><PackageReference Include="Avalonia.Headless" Version="12.1.4"/><PackageReference Include="Avalonia.Themes.Fluent" Version="12.1.4"/></ItemGroup></Project>''', encoding='utf-8')
    (target / 'Program.cs').write_text('''using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Scribe.Core;
using Scribe.Avalonia;
AppBuilder.Configure<ConsumerApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions {UseHeadlessDrawing=true}).SetupWithoutStarting();
if(typeof(EditorViewport).BaseType!=typeof(Control))throw new Exception("Not an original viewport");
if(typeof(Document).Assembly.GetReferencedAssemblies().Any(a=>a.Name!.Contains("Avalonia")))throw new Exception("Core depends on Avalonia");
using var editor=new ScribeEditor{Document=new Document("cat dog cat")};
var window=new Window{Width=700,Height=450,Content=editor};window.Show();editor.ApplyTemplate();window.Measure(new Size(700,450));window.Arrange(new Rect(0,0,700,450));
if(editor.Viewport.Bounds.Width<100)throw new Exception("Packaged template did not render");
editor.Session.Select(new Selection(0,3),new Selection(8,11));editor.Session.Insert("tiger");
if(editor.Document.Snapshot.ToString()!="tiger dog tiger")throw new Exception("Multi-selection package editing failed");
editor.Document.Undo();
if(editor.Document.Snapshot.ToString()!="cat dog cat")throw new Exception("Package undo failed");
window.Close();Console.WriteLine("SCRIBE_PACKAGE_CONSUMER: PASS");
public sealed class ConsumerApp:Application
{
    public override void Initialize(){Styles.Add(new FluentTheme());Styles.Add(new StyleInclude(new Uri("avares://Consumer/")){Source=new Uri("avares://Scribe.Avalonia/Themes/Generic.axaml")});}
}
''', encoding='utf-8')
    result = subprocess.run(['dotnet', 'run', '--project', 'Consumer.csproj', '-c', 'Release'], cwd=target, env={**os.environ, 'NUGET_PACKAGES': str(target / 'fresh-packages')}, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=180)
    print(result.stdout, end='')
    (output / 'package-consumer.log').write_text(result.stdout, encoding='utf-8')
    assets = json.loads((target / 'obj/project.assets.json').read_text())
    dependencies = sorted(assets['libraries'])
    assert not any(token in item.lower() for item in dependencies for token in ('avaloniaedit', 'avalonedit', 'monaco', 'scintilla', 'textmatesharp', 'protext'))
    passed = result.returncode == 0 and 'SCRIBE_PACKAGE_CONSUMER: PASS' in result.stdout
    (output / 'package-consumer.json').write_text(json.dumps({'passed': passed, 'freshPackageCache': True, 'outsideHostingRepository': True, 'projectReferences': 0, 'dependencies': dependencies, 'packages': expected}, indent=2), encoding='utf-8')
    raise SystemExit(0 if passed else 1)
