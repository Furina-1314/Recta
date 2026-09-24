using Avalonia.Controls;
using Recta.App.NativeInterop;

namespace Recta.App.Pages;

public sealed record BudgetRowVm(
    string Month, string Flexible, string Faculty, string ClassFund, string OutflowTotal,
    string InflowTotal);

public partial class BudgetPage : UserControl
{
    public BudgetPage()
    {
        InitializeComponent();
        Loaded += (_, _) => _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (!AppServices.NativeReady)
        {
            FooterText.Text = "原生核心未初始化";
            return;
        }

        try
        {
            var overviewTask = Task.Run(() => AppServices.Client.GetOverview());
            var budgetTask = Task.Run(() => AppServices.Client.GetBudgetOverview());
            var overview = await overviewTask;
            var budget = await budgetTask;

            FlexibleValue.Text = RectaClient.FormatMoney(overview.FlexibleBalanceCents);
            FacultyValue.Text = RectaClient.FormatMoney(overview.FacultyHangingCents);
            ClassFundValue.Text = RectaClient.FormatMoney(overview.Custody.BalancesSumCents);

            Rows.ItemsSource = budget.Months.Take(6).Select(m =>
            {
                var flexible = m.SettledByChannel.TryGetValue("FLEXIBLE", out var f) ? f : 0;
                var faculty = m.SettledByChannel.TryGetValue("FACULTY", out var fa) ? fa : 0;
                var classFund = m.SettledByChannel.TryGetValue("CLASS_FUND", out var c) ? c : 0;
                return new BudgetRowVm(
                    m.Month,
                    RectaClient.FormatMoney(flexible),
                    RectaClient.FormatMoney(faculty),
                    RectaClient.FormatMoney(classFund),
                    RectaClient.FormatMoney(flexible + faculty + classFund),
                    RectaClient.FormatMoney(m.InflowCents));
            }).ToList();

            FooterText.Text = "近月走势按已办结出账与入账台账聚合;金额均为整数分换算的定点展示。";
        }
        catch (RectaException ex)
        {
            FooterText.Text = $"加载失败:{ex.Message}";
        }
    }
}
