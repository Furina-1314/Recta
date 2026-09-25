using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Recta.App.NativeInterop;

namespace Recta.App.Pages;

public sealed record UserRowVm(
    string Id, string DisplayName, string Username, string RoleLabel, string StatusLabel,
    IBrush StatusBrush, string LastLogin, string ToggleLabel, bool IsDeactivate, bool CanManage);

public partial class UsersPage : UserControl, IRefreshable
{
    private IReadOnlyList<UserDto> _users = [];

    public UsersPage()
    {
        InitializeComponent();
        Loaded += (_, _) => _ = LoadAsync();
    }

    public Task RefreshAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        if (!AppServices.NativeReady || AppServices.Session?.Role != "BRANCH_SECRETARY")
        {
            return;
        }
        try
        {
            _users = await ConnectionGate.RunAsync(() => AppServices.Client.ListUsers());
            Rows.ItemsSource = _users.Select(ToRow).ToList();
            FooterText.Text = $"共 {_users.Count} 个账号(启用 {_users.Count(u => u.IsActive)} 个)。临时口令只显示一次;新账号首次登录将被要求修改口令。";
        }
        catch (RectaException ex)
        {
            FooterText.Text = $"加载失败:{ConnectionGate.Friendly(ex)}";
        }
        catch (Exception ex)
        {
            FooterText.Text = $"加载异常:{ex.Message}";
        }
    }

    private IBrush ThemeBrush(string key) =>
        (Application.Current?.FindResource(key) as IBrush) ?? Brushes.Gray;

    private UserRowVm ToRow(UserDto u)
    {
        var isSelf = u.Id == AppServices.Session?.UserId;
        return new UserRowVm(
            u.Id, u.DisplayName + (isSelf ? "（本人）" : ""), u.Username, RoleLabel(u.Role),
            u.IsActive ? "启用" : "已停用",
            u.IsActive
                ? ThemeBrush("RectaPositiveBrush")
                : ThemeBrush("RectaPendingBrush"),
            u.LastLoginAt is { Length: >= 10 } t ? t[..10] : "从未登录",
            u.IsActive ? "停用" : "启用",
            u.IsActive,
            // 自保护:自己的行不提供重置/改名/停用(域层同样拒绝,双保险)。
            CanManage: !isSelf);
    }

    private static string RoleLabel(string role) => role switch
    {
        "BRANCH_SECRETARY" => "团支书",
        "LIFE_COMMITTEE" => "生活委员",
        "CLASS_COMMITTEE" => "职能班委",
        _ => role,
    };

    private void ShowTempPassword(string temp)
    {
        TempPasswordLabel.IsVisible = true;
        TempPasswordBox.Text = temp;
        TempPasswordBox.IsVisible = true;
        CopyPasswordButton.IsVisible = true;
        UserError.IsVisible = false;
    }

    private void ShowError(string message)
    {
        UserError.Text = message;
        UserError.IsVisible = true;
    }

    private async void OnCreateUser(object? sender, RoutedEventArgs e)
    {
        UserError.IsVisible = false;
        var session = AppServices.Session;
        if (session is null)
        {
            return;
        }
        var username = (NewUsernameBox.Text ?? "").Trim();
        var name = (NewNameBox.Text ?? "").Trim();
        var role = (NewRoleBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        if (username.Length == 0 || name.Length == 0)
        {
            ShowError("请填写用户名与姓名。");
            return;
        }

        try
        {
            var temp = await ConnectionGate.RunAsync(() =>
                AppServices.Client.CreateUser(session.UserId, $"u_{username}", username, name, role));
            NewUsernameBox.Text = "";
            NewNameBox.Text = "";
            ShowTempPassword(temp);
            await LoadAsync();
        }
        catch (RectaException ex)
        {
            ShowError(ConnectionGate.Friendly(ex));
        }
        catch (Exception ex)
        {
            ShowError($"操作异常:{ex.Message}");
        }
    }

    private async void OnCopyPassword(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(TempPasswordBox.Text ?? "");
            CopyPasswordButton.Content = "已复制";
        }
    }

    private async void OnResetPassword(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: UserRowVm row } || AppServices.Session is null)
        {
            return;
        }
        try
        {
            var temp = await ConnectionGate.RunAsync(() =>
                AppServices.Client.ResetPassword(AppServices.Session!.UserId, row.Id));
            ShowTempPassword(temp);
        }
        catch (RectaException ex)
        {
            ShowError(ConnectionGate.Friendly(ex));
        }
        catch (Exception ex)
        {
            ShowError($"操作异常:{ex.Message}");
        }
    }

    private async void OnRename(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: UserRowVm row } || AppServices.Session is null)
        {
            return;
        }
        var dialog = new Windows.TextInputDialog("改姓名", $"将“{row.DisplayName}”改名为:", row.DisplayName);
        await dialog.ShowDialog(TopLevel.GetTopLevel(this) as Window
                                ?? throw new InvalidOperationException());
        var name = (dialog.Value ?? "").Trim();
        if (name.Length == 0 || name == row.DisplayName)
        {
            return;
        }
        try
        {
            await ConnectionGate.RunAsync(() =>
                AppServices.Client.UpdateDisplayName(AppServices.Session!.UserId, row.Id, name));
            await LoadAsync();
        }
        catch (RectaException ex)
        {
            ShowError(ConnectionGate.Friendly(ex));
        }
        catch (Exception ex)
        {
            ShowError($"操作异常:{ex.Message}");
        }
    }

    private async void OnToggleActive(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: UserRowVm row } || AppServices.Session is null)
        {
            return;
        }
        try
        {
            if (row.IsDeactivate)
            {
                await ConnectionGate.RunAsync(() =>
                    AppServices.Client.DeactivateUser(AppServices.Session!.UserId, row.Id));
            }
            else
            {
                await ConnectionGate.RunAsync(() =>
                    AppServices.Client.ActivateUser(AppServices.Session!.UserId, row.Id));
            }
            await LoadAsync();
        }
        catch (RectaException ex)
        {
            ShowError(ConnectionGate.Friendly(ex));
        }
        catch (Exception ex)
        {
            ShowError($"操作异常:{ex.Message}");
        }
    }

    private void OnRefresh(object? sender, RoutedEventArgs e) => _ = LoadAsync();
}
