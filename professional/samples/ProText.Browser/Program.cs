using Avalonia;
using Avalonia.Browser;
using ProText.Sample;
namespace ProText.Browser;
internal static class Program
{
    public static async Task Main(string[] args)
    {
        TestBridge.Enabled=args.Any(arg=>arg.Contains("test=1",StringComparison.Ordinal));
        await AppBuilder.Configure<App>().WithInterFont().StartBrowserAppAsync("out");
    }
}
