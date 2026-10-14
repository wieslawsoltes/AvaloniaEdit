# Third-party notices

Scribe's original editor source does not copy or depend on an existing editor implementation. These framework/build dependencies are referenced through their normal packages:

- Avalonia framework, Avalonia.Desktop, Avalonia.Browser, Avalonia.Themes.Fluent and Avalonia.Headless: AvaloniaUI contributors, MIT. https://github.com/AvaloniaUI/Avalonia
- Avalonia.Fonts.Inter is used by the sample applications only. Inter: Rasmus Andersson and contributors, SIL Open Font License. https://github.com/rsms/inter
- .NET runtime and SDK: .NET Foundation and contributors, distributed with their own licenses and notices. https://github.com/dotnet/runtime
- Browser validation tools: Playwright (Apache-2.0), Chromium and Pillow under their respective licenses. These are not reusable-control runtime dependencies.

Preserve licenses/notices from all NuGet and runtime dependencies when distributing compiled applications. There are no standalone font files in the Scribe source or Scribe library packages.
