using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
namespace Scribe.Sample;
public partial class App : Application
{
    public static MainView? Gallery {get;private set;}
    public override void Initialize()=>AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        Gallery=new MainView();
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)desktop.MainWindow=new Window{Title="Scribe — independent Avalonia editor",Width=1440,Height=940,MinWidth=420,MinHeight=400,Content=Gallery};
        else if(ApplicationLifetime is ISingleViewApplicationLifetime single)single.MainView=Gallery;
        base.OnFrameworkInitializationCompleted();
    }
}
