using Avalonia.Controls;
using Avalonia.Interactivity;
using Recta.App.NativeInterop;

namespace Recta.App.Pages;

public partial class FlexiblePage : UserControl
{
    public FlexiblePage()
    {
        InitializeComponent();
        SubmitPanel.FixedChannel = "FLEXIBLE";
        SubmitPanel.RequestSubmitted += (_, _) => _ = RefreshAsync();
        Loaded += (_, _) => _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (!AppServices.NativeReady)
        {
            return;
        }

        var isSecretary = AppServices.Session?.Role == "BRANCH_SECRETARY";
        InflowCard.IsVisible = isSecretary;

        try
        {
            var accounts = await Task.Run(() => AppServices.Client.ListAccounts());
            var flexible = accounts.FirstOrDefault(a => a.Type == "FLEXIBLE_PUBLIC");
            BalanceValue.Text = flexible is null ? "未初始化" : RectaClient.FormatMoney(flexible.BalanceCents);
        }
        catch (RectaException)
        {
            BalanceValue.Text = "—";
        }
        await ChannelList.ReloadAsync();
    }

    private async void OnInflow(object? sender, RoutedEventArgs e)
    {
        InflowError.IsVisible = false;
        var session = AppServices.Session;
        if (session is null)
        {
            return;
        }

        long cents;
        try
        {
            cents = RectaClient.ParseMoney(InflowAmountBox.Text ?? "");
        }
        catch (RectaException ex)
        {
            ShowInflowError($"金额格式错误:{ex.Message}");
            return;
        }
        var source = (InflowSourceBox.Text ?? "").Trim();
        if (source.Length == 0)
        {
            ShowInflowError("来源凭据必填(§5.3)。");
            return;
        }

        try
        {
            await Task.Run(() => AppServices.Client.RecordInflow(
                session.UserId, "TO_FLEXIBLE_ACCOUNT", cents, source));
            InflowAmountBox.Text = "";
            InflowSourceBox.Text = "";
            await RefreshAsync();
        }
        catch (RectaException ex)
        {
            ShowInflowError(ex.Message);
        }
    }

    private void ShowInflowError(string message)
    {
        InflowError.Text = message;
        InflowError.IsVisible = true;
    }
}
