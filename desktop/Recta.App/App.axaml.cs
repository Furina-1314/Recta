using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Recta.App.NativeInterop;

namespace Recta.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        PinSystemAccentColors();
    }

    // FluentTheme 会采纳 Windows 系统强调色(用户配色可能非蓝)。
    // 在 App.Resources 顶层以代码钉死全系列为 Recta 7 级蓝色阶——
    // 优先级高于任何主题字典写入,彻底杜绝 OS 配色渗入(Vibe.md §3:强调色固定 #0078D7)。
    private void PinSystemAccentColors()
    {
        Resources["SystemAccentColor"] = Color.Parse("#0078D7");
        Resources["SystemAccentColorDark1"] = Color.Parse("#0067B7");
        Resources["SystemAccentColorDark2"] = Color.Parse("#005598");
        Resources["SystemAccentColorDark3"] = Color.Parse("#00437A");
        Resources["SystemAccentColorLight1"] = Color.Parse("#1A8BE1");
        Resources["SystemAccentColorLight2"] = Color.Parse("#4DA3E8");
        Resources["SystemAccentColorLight3"] = Color.Parse("#8FC7F1");
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
