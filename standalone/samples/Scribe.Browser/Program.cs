using Avalonia;
using Avalonia.Browser;
using Scribe.Sample;
using System.Runtime.Versioning;
[assembly:SupportedOSPlatform("browser")]
namespace Scribe.Browser;
internal static class Program
{
    public static async Task Main(string[] args)
    {
        TestBridge.Enabled=args.Any(a=>a.Contains("test=1",StringComparison.Ordinal));
        await AppBuilder.Configure<App>().WithInterFont().StartBrowserAppAsync("out");
    }
}
