using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
namespace ProText.Sample;
public partial class App : Application
{
    public static MainView? Gallery { get; private set; }
    public override void Initialize()=>AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        Gallery=new MainView();
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow=new Window { Title="ProText — Avalonia editor controls",Width=1440,Height=900,MinWidth=520,MinHeight=420,Content=Gallery };
        else if(ApplicationLifetime is ISingleViewApplicationLifetime single) single.MainView=Gallery;
        base.OnFrameworkInitializationCompleted();
    }
}
