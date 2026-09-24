using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Recta.App.NativeInterop;

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
            // RECTA_SMOKE=1:冒烟模式——跳过登录直入主窗,截图后自动退出(供构建验证)。
            if (AppServices.SmokeMode)
            {
                // 演示会话:角色/姓名可经 RECTA_SMOKE_ROLE/USER/UID 定制,用于不同角色的视觉走查。
                var role = Environment.GetEnvironmentVariable("RECTA_SMOKE_ROLE") ?? "BRANCH_SECRETARY";
                var name = Environment.GetEnvironmentVariable("RECTA_SMOKE_USER") ?? "演示用户";
                var uid = Environment.GetEnvironmentVariable("RECTA_SMOKE_UID") ?? "u_sec_01";
                AppServices.SetSession(new LoginSession(uid, "smoke", name, role, false));

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
