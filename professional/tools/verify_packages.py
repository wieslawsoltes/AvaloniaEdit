#!/usr/bin/env python3
"""Build/run an isolated NuGet consumer; no ProjectReference or shared package cache."""
from pathlib import Path
import json
import os
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile

root = Path(__file__).resolve().parents[1]
packages = root / 'artifacts/packages'
validation = root / 'artifacts/validation'
validation.mkdir(parents=True, exist_ok=True)
expected = ['ProText.Core.0.1.0.nupkg', 'ProText.Avalonia.0.1.0.nupkg']
for name in expected:
    package = packages / name
    if not package.is_file():
        raise SystemExit(f'Missing package: {package}')
    with zipfile.ZipFile(package) as archive:
        assert 'README.md' in archive.namelist(), f'{name}: missing README'
        assert any(path.endswith('/' + name.split('.0.1.0')[0] + '.dll') for path in archive.namelist()), f'{name}: missing library assembly'
        assert not any(path.lower().endswith(('.ttf', '.otf', '.woff', '.woff2')) for path in archive.namelist()), f'{name}: unexpected standalone font'

with tempfile.TemporaryDirectory(prefix='protext-consumer-') as temporary:
    target = Path(temporary)
    configuration = ET.Element('configuration')
    sources = ET.SubElement(configuration, 'packageSources')
    ET.SubElement(sources, 'clear')
    ET.SubElement(sources, 'add', key='built', value=str(packages.resolve()))
    ET.SubElement(sources, 'add', key='nuget.org', value='https://api.nuget.org/v3/index.json')
    ET.ElementTree(configuration).write(target / 'NuGet.config', encoding='utf-8', xml_declaration=True)
    (target / 'Consumer.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><PackageReference Include="ProText.Avalonia" Version="0.1.0"/><PackageReference Include="Avalonia.Headless" Version="12.1.4"/><PackageReference Include="Avalonia.Themes.Fluent" Version="12.1.4"/></ItemGroup></Project>''', encoding='utf-8')
    (target / 'Program.cs').write_text('''using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using ProText.Avalonia;
using ProText.Avalonia.Controls;
using ProText.Core;
AppBuilder.Configure<ConsumerApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing=true }).SetupWithoutStarting();
using var session = new EditorSession("package consumer");
using var editor = new ProTextEditor { Session=session };
var window = new Window { Width=640,Height=400,Content=editor };
window.Show(); editor.ApplyTemplate(); window.Measure(new Size(640,400)); window.Arrange(new Rect(0,0,640,400));
if (editor.Editor.Bounds.Width <= 0 || editor.Editor.Bounds.Height <= 0) throw new InvalidOperationException("Packaged theme did not create an editing surface.");
session.ApplyEdits([new TextEdit(new TextRange(0,7),"isolated")]);
if(editor.Text!="isolated consumer") throw new InvalidOperationException("Packaged core editing failed.");
editor.Editor.Undo();
if(editor.Text!="package consumer") throw new InvalidOperationException("Packaged undo failed.");
window.Close();
Console.WriteLine("PROTEXT_PACKAGE_CONSUMER: PASS");
public sealed class ConsumerApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://Consumer/")) { Source=new Uri("avares://ProText.Avalonia/Themes/Generic.axaml") });
    }
}
''', encoding='utf-8')
    environment = {**os.environ, 'NUGET_PACKAGES': str(target / 'cache')}
    result = subprocess.run(['dotnet', 'run', '--project', str(target / 'Consumer.csproj'), '-c', 'Release'], cwd=target, env=environment, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=180)
    print(result.stdout, end='')
    (validation / 'package-consumer.log').write_text(result.stdout, encoding='utf-8')
    passed = result.returncode == 0 and 'PROTEXT_PACKAGE_CONSUMER: PASS' in result.stdout
    (validation / 'package-consumer.json').write_text(json.dumps({'passed': passed, 'isolatedNugetCache': True, 'projectReferences': 0, 'packages': expected}, indent=2), encoding='utf-8')
    raise SystemExit(0 if passed else 1)
