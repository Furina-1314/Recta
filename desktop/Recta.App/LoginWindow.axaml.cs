using Avalonia.Controls;
using Avalonia.Interactivity;
using Recta.App.NativeInterop;

namespace Recta.App;

public partial class LoginWindow : Window
{
    private LoginSession? _pendingSession;

    public LoginWindow()
    {
        InitializeComponent();
        Branding.ApplyIcon(this);
        if (AppServices.TestMode)
        {
            Title = "Recta 矩衡【测试库】· 登录";
        }
        Loaded += async (_, _) => await DetectFirstRunAsync();
        if (AppServices.SmokeMode && Environment.GetEnvironmentVariable("RECTA_SMOKE_LOGIN") == "1")
        {
            // 冒烟:截图登录窗后自动退出(验证登录/引导面板渲染)。
            Loaded += async (_, _) =>
            {
                await Task.Delay(2600);
                await CaptureSmokeAsync();
                Close();
            };
        }
    }

    // users 空表 → 显示一次性首任团支书引导(否则空系统无人可登录)。
    private async Task DetectFirstRunAsync()
    {
        if (!AppServices.NativeReady)
        {
            return;
        }
        try
        {
            var users = await ConnectionGate.RunAsync(() => AppServices.Client.ListUsers());
            if (users.Count == 0)
            {
                LoginPanel.IsVisible = false;
                BootstrapPanel.IsVisible = true;
            }
        }
        catch (RectaException)
        {
            // 检测失败按普通登录处理,错误会在登录时呈现。
        }
    }

    private async void OnBootstrap(object? sender, RoutedEventArgs e)
    {
        ErrorText.IsVisible = false;
        var username = (BootstrapUsernameBox.Text ?? "").Trim();
        var name = (BootstrapNameBox.Text ?? "").Trim();
        if (username.Length == 0 || name.Length == 0)
        {
            ShowError("用户名与姓名必填。");
            return;
        }

        try
        {
            var tempPassword = await Task.Run(() =>
                AppServices.Client.BootstrapSecretary(username, name));
            TempPasswordBox.Text = tempPassword;
            TempPasswordBox.IsVisible = true;
            BootstrapDoneButton.IsVisible = true;
        }
        catch (RectaException ex)
        {
            ShowError(ConnectionGate.Friendly(ex));
        }
    }

    private void OnBootstrapDone(object? sender, RoutedEventArgs e)
    {
        BootstrapPanel.IsVisible = false;
        LoginPanel.IsVisible = true;
        UsernameBox.Text = (BootstrapUsernameBox.Text ?? "").Trim();
        PasswordBox.Text = string.Empty;
        ErrorText.IsVisible = false;
    }

    private async Task CaptureSmokeAsync()
    {
        try
        {
            var outDir = Environment.GetEnvironmentVariable("RECTA_SMOKE_OUT")
                         ?? Path.Combine(AppContext.BaseDirectory, "smoke");
            Directory.CreateDirectory(outDir);
            var scale = VisualRoot is Avalonia.Rendering.IRenderRoot root ? root.RenderScaling : 1.0;
            var size = new Avalonia.PixelSize((int)(Bounds.Width * scale), (int)(Bounds.Height * scale));
            using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size,
                new Avalonia.Vector(96 * scale, 96 * scale));
            bitmap.Render(this);
            bitmap.Save(Path.Combine(outDir, "smoke-login.png"));
        }
        catch
        {
            // 截图失败不阻断退出
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }

    private async void OnLogin(object? sender, RoutedEventArgs e)
    {
        ErrorText.IsVisible = false;
        var username = UsernameBox.Text?.Trim();
        var password = PasswordBox.Text ?? string.Empty;

        if (string.IsNullOrEmpty(username) || password.Length == 0)
        {
            ShowError("请输入用户名与口令。");
            return;
        }

        LoginButton.IsEnabled = false;
        try
        {
            var session = await ConnectionGate.RunAsync(() => AppServices.Client.Login(username, password));
            AppServices.SetSession(session);

            if (session.MustChangePassword)
            {
                _pendingSession = session;
                LoginPanel.IsVisible = false;
                ChangePanel.IsVisible = true;
            }
            else
            {
                EnterMain();
            }
        }
        catch (RectaException ex)
        {
            ShowError(ex.IsDatabase
                ? $"数据库连接失败：{ConnectionGate.Friendly(ex)}\n请检查网络与 .env.local 配置。"
                : ConnectionGate.Friendly(ex));
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private async void OnChangePassword(object? sender, RoutedEventArgs e)
    {
        ErrorText.IsVisible = false;
        if (_pendingSession is null)
        {
            return;
        }

        var oldPassword = OldPasswordBox.Text ?? string.Empty;
        var newPassword = NewPasswordBox.Text ?? string.Empty;
        var confirm = ConfirmPasswordBox.Text ?? string.Empty;

        if (newPassword != confirm)
        {
            ShowError("两次输入的新口令不一致。");
            return;
        }

        try
        {
            await ConnectionGate.RunAsync(() => AppServices.Client.ChangePassword(_pendingSession.UserId, oldPassword,
                                                                   newPassword));
            AppServices.SetSession(_pendingSession with { MustChangePassword = false });
            EnterMain();
        }
        catch (RectaException ex)
        {
            ShowError(ConnectionGate.Friendly(ex));
        }
    }

    private void EnterMain()
    {
        var main = new MainWindow();
        main.Show();
        Close();
        if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = main;
        }
    }
}
