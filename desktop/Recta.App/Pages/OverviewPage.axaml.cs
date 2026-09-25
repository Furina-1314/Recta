using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Recta.App.NativeInterop;

namespace Recta.App.Pages;

public partial class OverviewPage : UserControl, IRefreshable
{
    public OverviewPage()
    {
        InitializeComponent();
        Loaded += (_, _) => _ = LoadAsync();
    }

    private async void OnRefresh(object? sender, RoutedEventArgs e) => await LoadAsync();

    public Task RefreshAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        ErrorText.IsVisible = false;
        if (!AppServices.NativeReady)
        {
            ShowLoadError("原生核心未初始化(缺少 recta_capi.dll 或连接配置)。");
            return;
        }

        try
        {
            var overview = await ConnectionGate.RunAsync(() => AppServices.Client.GetOverview());

            FlexibleValue.Text = Format(overview.FlexibleBalanceCents);
            FacultyValue.Text = Format(overview.FacultyHangingCents);
            CashValue.Text = Format(overview.Custody.CustodianCashCents);
            AdvanceValue.Text = Format(overview.Custody.AdvanceTotalCents);

            ConservationFormula.Text =
                $"结余合计 {Format(overview.Custody.BalancesSumCents)} = " +
                $"{Format(overview.Custody.CustodianCashCents)} − {Format(overview.Custody.AdvanceTotalCents)}";
            var conserved = overview.Custody.Conserved;
            BadgeOk.IsVisible = conserved;
            BadgeBad.IsVisible = !conserved;

            PendingCount.Text = CountOf(overview, "PENDING_REVIEW").ToString();
            ApprovedCount.Text = CountOf(overview, "APPROVED").ToString();
            SettledCount.Text = CountOf(overview, "SETTLED").ToString();
            RejectedCount.Text = CountOf(overview, "REJECTED").ToString();
        }
        catch (RectaException ex)
        {
            ShowLoadError(ex.IsDatabase
                ? $"数据库连接失败:{ConnectionGate.Friendly(ex)}"
                : ConnectionGate.Friendly(ex));
        }
    }

    private static long CountOf(OverviewDto overview, string status) =>
        overview.StatusCounts.TryGetValue(status, out var count) ? count : 0;

    private static string Format(long cents) => RectaClient.FormatMoney(cents);

    private void ShowLoadError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }
}
