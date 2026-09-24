using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Recta.App.NativeInterop;

namespace Recta.App.Pages;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => SyncThemeSelection();

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
            PasswordResultText.Foreground = this.FindResource("RectaDangerBrush") as IBrush;
            PasswordResultText.Text = "演示模式,不可修改。";
            return;
        }

        try
        {
            await ConnectionGate.RunAsync(() => AppServices.Client.ChangePassword(
                session.UserId, OldPasswordBox.Text ?? "", NewPasswordBox.Text ?? ""));
            PasswordResultText.Foreground = this.FindResource("RectaPositiveBrush") as IBrush;
            PasswordResultText.Text = "口令已修改。";
            OldPasswordBox.Text = "";
            NewPasswordBox.Text = "";
        }
        catch (RectaException ex)
        {
            PasswordResultText.Foreground = this.FindResource("RectaDangerBrush") as IBrush;
            PasswordResultText.Text = ConnectionGate.Friendly(ex);
        }
    }
}
