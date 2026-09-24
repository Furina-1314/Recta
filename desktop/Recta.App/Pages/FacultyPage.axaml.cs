using Avalonia.Controls;
using Avalonia.Interactivity;
using Recta.App.NativeInterop;

namespace Recta.App.Pages;

public partial class FacultyPage : UserControl
{
    public FacultyPage()
    {
        InitializeComponent();
        SubmitPanel.FixedChannel = "FACULTY";
        SubmitPanel.RequestSubmitted += (_, _) => _ = RefreshAsync();
        Loaded += (_, _) => _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (!AppServices.NativeReady)
        {
            return;
        }

        var isLife = AppServices.Session?.Role == "LIFE_COMMITTEE";
        RedeemCard.IsVisible = isLife;

        try
        {
            var accounts = await Task.Run(() => AppServices.Client.ListAccounts());
            var faculty = accounts.FirstOrDefault(a => a.Type == "FACULTY_REIMBURSE");
            HangingValue.Text = faculty is null ? "未初始化" : RectaClient.FormatMoney(faculty.BalanceCents);

            if (isLife)
            {
                // 关联单据:仅已办结的系报销单。
                var settled = await Task.Run(() => AppServices.Client.ListRequests("SETTLED", "FACULTY", null));
                RelatedBox.ItemsSource = settled
                    .Select(r => new RelatedOption(r.Id, $"#{r.Id:D3} {r.Title} " +
                        $"{RectaClient.FormatMoney(r.SettledAmountCents ?? 0)} 元"))
                    .ToList();
                if (RelatedBox.ItemCount > 0)
                {
                    RelatedBox.SelectedIndex = 0;
                }
            }
        }
        catch (RectaException)
        {
            HangingValue.Text = "—";
        }
        await ChannelList.ReloadAsync();
    }

    private sealed record RelatedOption(int RequestId, string Label);

    private async void OnRedeem(object? sender, RoutedEventArgs e)
    {
        RedeemError.IsVisible = false;
        var session = AppServices.Session;
        if (session is null)
        {
            return;
        }

        long cents;
        try
        {
            cents = RectaClient.ParseMoney(RedeemAmountBox.Text ?? "");
        }
        catch (RectaException ex)
        {
            ShowRedeemError($"金额格式错误:{ex.Message}");
            return;
        }
        var source = (RedeemSourceBox.Text ?? "").Trim();
        if (source.Length == 0)
        {
            ShowRedeemError("来源必填(§5.3)。");
            return;
        }
        if (RelatedBox.SelectedItem is not RelatedOption related)
        {
            ShowRedeemError("必须关联已办结的系报销单据。");
            return;
        }

        try
        {
            await Task.Run(() => AppServices.Client.RecordInflow(
                session.UserId, "TO_FACULTY_REIMBURSE", cents, source, null, related.RequestId));
            RedeemAmountBox.Text = "";
            RedeemSourceBox.Text = "";
            await RefreshAsync();
        }
        catch (RectaException ex)
        {
            ShowRedeemError(ex.Message);
        }
    }

    private void ShowRedeemError(string message)
    {
        RedeemError.Text = message;
        RedeemError.IsVisible = true;
    }
}
