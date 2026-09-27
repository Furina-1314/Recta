using Avalonia.Controls;

namespace Recta.App.Windows;

/// <summary>下载进度对话框:比例条 + 已下载量,可取消(经 <see cref="CancelRequested"/> 通知调用方)。</summary>
public partial class ProgressDialog : Window
{
    /// <summary>用户点击取消时触发(UI 线程)。</summary>
    public event EventHandler? CancelRequested;

    public ProgressDialog()
    {
        InitializeComponent();
    }

    public ProgressDialog(string title, string initialStatus)
    {
        InitializeComponent();
        Title = title;
        StatusText.Text = initialStatus;
    }

    /// <param name="fraction">0~1。</param>
    public void SetProgress(double fraction, long writtenBytes, long totalBytes)
    {
        DownloadBar.Value = fraction;
        StatusText.Text = totalBytes > 0
            ? $"正在下载… {writtenBytes / 1024.0 / 1024.0:F1} / {totalBytes / 1024.0 / 1024.0:F1} MB" +
              $"（{fraction:P0}）"
            : $"正在下载… {writtenBytes / 1024.0 / 1024.0:F1} MB";
    }

    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }
}
