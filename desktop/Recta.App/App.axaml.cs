using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Recta.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppServices.InitNative();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // RECTA_SMOKE=1:冒烟模式——跳过登录直入主窗,2.5s 后自动退出(供构建验证)。
            if (AppServices.SmokeMode)
            {
                var main = new MainWindow();
                main.Show();
                desktop.MainWindow = main;
            }
            else
            {
                var login = new LoginWindow();
                login.Show();
                desktop.MainWindow = login;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
