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
            var session = await Task.Run(() => AppServices.Client.Login(username, password));
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
                ? $"数据库连接失败：{ex.Message}\n请检查网络与 .env.local 配置。"
                : ex.Message);
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
            await Task.Run(() => AppServices.Client.ChangePassword(_pendingSession.UserId, oldPassword,
                                                                   newPassword));
            AppServices.SetSession(_pendingSession with { MustChangePassword = false });
            EnterMain();
        }
        catch (RectaException ex)
        {
            ShowError(ex.Message);
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
