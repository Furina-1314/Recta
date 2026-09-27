using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Recta.App.NativeInterop;
using Recta.App.Windows;

namespace Recta.App.Pages;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => SyncThemeSelection();

        // InformationalVersion 形如 "1.0.1+<源修订哈希>",展示时剥为 vX.Y.Z。
        var raw = CrashLog.AppVersion;
        var plus = raw.IndexOf('+');
        AppVersionText.Text = plus >= 0 ? $"应用版本:v{raw[..plus]}" : $"应用版本:v{raw}";

        try
        {
            VersionText.Text = $"原生核心:{RectaClient.Version()}";
        }
        catch (RectaException ex)
        {
            VersionText.Text = $"原生核心不可用:{ConnectionGate.Friendly(ex)}";
        }
    }

    private void SyncThemeSelection()
    {
        var current = Application.Current?.ActualThemeVariant;
        LightRadio.IsChecked = current == ThemeVariant.Light;
        DarkRadio.IsChecked = current == ThemeVariant.Dark;
    }

    private void OnThemeClick(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is null)
        {
            return;
        }
        Application.Current.RequestedThemeVariant =
            ReferenceEquals(sender, DarkRadio) ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private async void OnChangePassword(object? sender, RoutedEventArgs e)
    {
        var session = AppServices.Session;
        if (session is null)
        {
            SetPasswordResult(false, "演示模式,不可修改。");
            return;
        }

        // 控件值必须在 UI 线程读取——进入 RunAsync 后即切换到工作线程,
        // 跨线程触碰 TextBox 会抛 InvalidOperationException 且被静默吞掉(改密无声失败的根因)。
        var oldPassword = OldPasswordBox.Text ?? "";
        var newPassword = NewPasswordBox.Text ?? "";
        if (oldPassword.Length == 0 || newPassword.Length == 0)
        {
            SetPasswordResult(false, "请填写原口令与新口令。");
            return;
        }

        try
        {
            await ConnectionGate.RunAsync(() =>
                AppServices.Client.ChangePassword(session.UserId, oldPassword, newPassword));
            OldPasswordBox.Text = "";
            NewPasswordBox.Text = "";
            SetPasswordResult(true, "口令已修改。");
            await new MessageDialog("修改口令", "口令修改成功，下次登录请使用新口令。").ShowDialog(
                (VisualRoot as Window)!);
        }
        catch (RectaException ex)
        {
            SetPasswordResult(false, ConnectionGate.Friendly(ex));
            await ShowPasswordErrorAsync(ConnectionGate.Friendly(ex));
        }
        catch (Exception ex)
        {
            SetPasswordResult(false, $"操作异常:{ex.Message}");
            await ShowPasswordErrorAsync($"操作异常:{ex.Message}");
        }
    }

    private async Task ShowPasswordErrorAsync(string message)
    {
        try
        {
            await new MessageDialog("修改口令", $"口令修改失败：{message}", isError: true).ShowDialog(
                (VisualRoot as Window)!);
        }
        catch
        {
            // 弹窗本身失败(如窗口已关闭)时保留行内文案即可。
        }
    }

    private void SetPasswordResult(bool positive, string message)
    {
        PasswordResultText.Foreground = Application.Current?.FindResource(
            positive ? "RectaPositiveBrush" : "RectaDangerBrush") as IBrush;
        PasswordResultText.Text = message;
    }

    // ---- 检查更新 ----

    private Window OwnerWindow => (VisualRoot as Window)!;

    private async void OnCheckForUpdates(object? sender, RoutedEventArgs e)
    {
        if (!UpdateService.IsInstalledBuild)
        {
            await new MessageDialog("检查更新", "当前为开发构建，自动更新不可用；请手动安装新版本。",
                isError: true).ShowDialog(OwnerWindow);
            return;
        }

        CheckUpdateButton.IsEnabled = false;
        UpdateResultText.Text = "正在检查更新…";
        UpdateResultText.Foreground = null;
        try
        {
            UpdateService.ReleaseInfo? release;
            try
            {
                release = await UpdateService.CheckLatestAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                SetUpdateResult(false, $"检查失败:{ex.Message}");
                await new MessageDialog("检查更新", $"检查更新失败：{ex.Message}\n请检查网络后重试。",
                    isError: true).ShowDialog(OwnerWindow);
                return;
            }

            if (release is null)
            {
                SetUpdateResult(false, "未能获取最新版本信息。");
                await new MessageDialog("检查更新", "未能获取最新版本信息(仓库缺少 Windows 安装包或版本号不可解析)。",
                    isError: true).ShowDialog(OwnerWindow);
                return;
            }

            if (!UpdateService.IsNewer(release.LatestVersion))
            {
                var latest = $"已是最新版本(当前 v{UpdateService.CurrentVersion})。";
                SetUpdateResult(true, latest);
                await new MessageDialog("检查更新", latest).ShowDialog(OwnerWindow);
                return;
            }

            // 有新版本:确认 → 下载到安装目录 → 静默安装。
            var sizeText = release.AssetSize > 0
                ? $"({release.AssetSize / 1024.0 / 1024.0:F1} MB)"
                : "";
            var confirmText = $"发现新版本 {release.Tag}(当前 v{UpdateService.CurrentVersion})。\n\n" +
                              $"将下载 {release.AssetName}{sizeText}到安装目录，" +
                              "完成后自动启动安装程序(会关闭本应用)。是否继续？";
            var confirm = new MessageDialog("检查更新", confirmText, showCancel: true);
            await confirm.ShowDialog(OwnerWindow);
            if (!confirm.Confirmed)
            {
                SetUpdateResult(true, $"发现新版本 {release.Tag}，已取消。");
                return;
            }

            await DownloadAndInstallAsync(release);
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private async Task DownloadAndInstallAsync(UpdateService.ReleaseInfo release)
    {
        var cts = new CancellationTokenSource();
        var progress = new ProgressDialog("正在下载更新",
            $"准备下载 {release.AssetName}({release.AssetSize / 1024.0 / 1024.0:F1} MB)…");
        progress.CancelRequested += (_, _) => cts.Cancel();
        IProgress<double> report = new Progress<double>(fraction =>
            progress.SetProgress(fraction, (long)(fraction * release.AssetSize), release.AssetSize));

        var shown = progress.ShowDialog(OwnerWindow);
        string setupPath;
        try
        {
            setupPath = await UpdateService.DownloadAsync(release, report, cts.Token);
        }
        catch (OperationCanceledException)
        {
            SetUpdateResult(true, $"发现新版本 {release.Tag}，已取消下载。");
            return;
        }
        catch (Exception ex)
        {
            SetUpdateResult(false, $"下载失败:{ex.Message}");
            await new MessageDialog("检查更新", $"下载失败：{ex.Message}", isError: true)
                .ShowDialog(OwnerWindow);
            return;
        }
        finally
        {
            progress.Close();
            await shown;
        }

        await new MessageDialog("检查更新",
            $"下载完成:{setupPath}\n\n即将启动安装程序，本应用将自动退出。").ShowDialog(OwnerWindow);
        try
        {
            UpdateService.LaunchInstaller(setupPath);
        }
        catch (Exception ex)
        {
            SetUpdateResult(false, $"无法启动安装程序:{ex.Message}");
            await new MessageDialog("检查更新",
                $"无法启动安装程序：{ex.Message}\n请手动运行安装目录中的 {release.AssetName}。",
                isError: true).ShowDialog(OwnerWindow);
            return;
        }

        if (Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lifetime)
        {
            lifetime.Shutdown(0);
        }
    }

    private void SetUpdateResult(bool positive, string message)
    {
        UpdateResultText.Foreground = Application.Current?.FindResource(
            positive ? "RectaPositiveBrush" : "RectaDangerBrush") as IBrush;
        UpdateResultText.Text = message;
    }
}
