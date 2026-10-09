using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using NUnit.Framework.Api;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using Uno.UI.Hosting;

namespace UnoEdit.Uno.Tests;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => UnoPlatformHostBuilder.Create().App(() => new TestApp()).UseX11().UseLinuxFrameBuffer().UseMacOS().UseWin32().Build().Run();
}

internal sealed class TestApp : Application
{
    internal static Grid Root { get; private set; }
    private Window _window;
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Resources.MergedDictionaries.Add(new XamlControlsResources());
        Root = new Grid();
        _window = new Window { Content = Root };
        Root.Loaded += Run;
        _window.Activate();
    }
    private void Run(object sender, RoutedEventArgs e)
    {
        Root.Loaded -= Run;
        try
        {
            var runner = new NUnitTestAssemblyRunner(new DefaultTestAssemblyBuilder());
            runner.Load(typeof(TestApp).Assembly, new Dictionary<string, object>
            {
                [NUnit.FrameworkPackageSettings.NumberOfTestWorkers] = 0,
                [NUnit.FrameworkPackageSettings.RunOnMainThread] = true
            });
            var result = runner.Run(TestListener.NULL, TestFilter.Empty);
            var directory = Environment.GetEnvironmentVariable("UNOEDIT_TEST_RESULTS") ?? "artifacts/tests/native";
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "nunit.xml"), result.ToXml(true).OuterXml);
            Console.WriteLine($"Native Uno NUnit: passed={result.PassCount}; failed={result.FailCount}; skipped={result.SkipCount}; inconclusive={result.InconclusiveCount}; total={result.TotalCount}");
            if (result.FailCount != 0) Console.WriteLine(result.ToXml(true).OuterXml);
            Environment.Exit(result.FailCount == 0 && result.PassCount > 0 && result.SkipCount == 0 && result.InconclusiveCount == 0 ? 0 : 1);
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.Exit(2); }
    }
}
