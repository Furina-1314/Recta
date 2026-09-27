using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Recta.App.Windows;

/// <summary>
/// 结果提示/确认对话框。默认仅"确定";showCancel 时提供"取消",
/// 关闭后经 <see cref="Confirmed"/> 读取用户选择。
/// </summary>
public partial class MessageDialog : Window
{
    public bool Confirmed { get; private set; }

    public MessageDialog()
    {
        InitializeComponent();
    }

    /// <param name="isError">true 时消息着危险色,便于一眼区分成败。</param>
    public MessageDialog(string title, string message, bool isError = false,
                         bool showCancel = false)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        CancelButton.IsVisible = showCancel;
        if (isError)
        {
            MessageText.Foreground =
                Application.Current?.FindResource("RectaDangerBrush") as IBrush;
        }
        Loaded += (_, _) => OkButton.Focus();
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }
}
