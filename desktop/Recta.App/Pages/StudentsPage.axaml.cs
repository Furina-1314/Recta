using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Recta.App.NativeInterop;

namespace Recta.App.Pages;

public sealed record StudentRowVm(
    string StudentId, string Name, long BalanceCents, string BalanceText, IBrush BalanceBrush,
    string RechargedText, string SpentText);

public sealed record LedgerLineVm(
    string TypeLabel, IBrush TypeBrush, string Note, string ChangeText, string BalanceText);

public partial class StudentsPage : UserControl, IRefreshable
{
    public StudentsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => _ = LoadAsync();
    }

    private async void OnRefresh(object? sender, RoutedEventArgs e) => await LoadAsync();

    public Task RefreshAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        RosterCard.IsVisible = AppServices.Session?.Role == "BRANCH_SECRETARY";
        RenameBox.IsEnabled = RenameButton.IsEnabled = false;
        RosterError.Text = "";

        if (!AppServices.NativeReady)
        {
            SummaryText.Text = "原生核心未初始化";
            return;
        }

        try
        {
            var students = await ConnectionGate.RunAsync(() => AppServices.Client.ListStudents());
            var overview = await ConnectionGate.RunAsync(() => AppServices.Client.GetOverview());

            SumValue.Text = RectaClient.FormatMoney(overview.Custody.BalancesSumCents);
            CashValue.Text = RectaClient.FormatMoney(overview.Custody.CustodianCashCents);
            AdvanceValue.Text = RectaClient.FormatMoney(overview.Custody.AdvanceTotalCents);
            ConservedValue.Text = overview.Custody.Conserved ? "平衡" : "异常!";

            Rows.ItemsSource = students.Select(s => new StudentRowVm(
                s.StudentId, s.Name, s.BalanceCents,
                RectaClient.FormatMoney(s.BalanceCents),
                s.BalanceCents < 0
                    ? (IBrush)this.FindResource("RectaDangerBrush")!
                    : (IBrush)this.FindResource("RectaTextPrimaryBrush")!,
                RectaClient.FormatMoney(s.TotalRechargedCents),
                RectaClient.FormatMoney(s.TotalSpentCents))).ToList();

            var overdrawn = students.Where(s => s.BalanceCents < 0)
                .OrderBy(s => s.BalanceCents)
                .ToList();
            AdvanceDisclosure.Text = overdrawn.Count == 0
                ? "当前无透支同学,生委无未收回垫资。"
                : string.Join("; ", overdrawn.Select(s =>
                      $"{s.Name} 欠 {RectaClient.FormatMoney(-s.BalanceCents)} 元")) +
                  $"。合计 {RectaClient.FormatMoney(overview.Custody.AdvanceTotalCents)} 元。";

            SummaryText.Text =
                $"共 {students.Count} 名同学 | 透支 {overdrawn.Count} 人 | 账目" +
                (overview.Custody.Conserved ? "平衡" : "异常!");
        }
        catch (RectaException ex)
        {
            SummaryText.Text = $"加载失败:{ConnectionGate.Friendly(ex)}";
        }
    }

    private async void OnRowSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (Rows.SelectedItem is not StudentRowVm row)
        {
            return;
        }
        RenameBox.IsEnabled = RenameButton.IsEnabled = true;
        DetailText.Text = $"{row.Name}({row.StudentId}):当前余额 {row.BalanceText} 元。" +
                          (row.BalanceCents < 0 ? "透支部分构成对生活委员个人的无息借贷。" : "结余由生活委员受托代管。");
        try
        {
            var entries = await ConnectionGate.RunAsync(() => AppServices.Client.ListStudentLedger(row.StudentId));
            LedgerList.ItemsSource = entries.Select(entry => new LedgerLineVm(
                TypeLabel(entry.EntryType),
                entry.ChangeCents >= 0
                    ? (IBrush)this.FindResource("RectaPositiveBrush")!
                    : (IBrush)this.FindResource("RectaDangerBrush")!,
                entry.Notes ?? "",
                (entry.ChangeCents > 0 ? "+" : "") + RectaClient.FormatMoney(entry.ChangeCents),
                RectaClient.FormatMoney(entry.BalanceAfterCents))).ToList();
        }
        catch (RectaException ex)
        {
            LedgerList.ItemsSource = Array.Empty<LedgerLineVm>();
            DetailText.Text = $"流水加载失败:{ConnectionGate.Friendly(ex)}";
        }
    }

    private static string TypeLabel(string entryType) => entryType switch
    {
        "EXPENSE_SPLIT" => "支出分摊",
        "RECHARGE" => "充值",
        "DISBURSEMENT" => "渠道出账",
        "INFLOW" => "渠道入账",
        _ => entryType,
    };

    private async void OnAddStudent(object? sender, RoutedEventArgs e)
    {
        RosterError.Text = "";
        var session = AppServices.Session;
        if (session is null)
        {
            return;
        }
        var id = (NewStudentIdBox.Text ?? "").Trim();
        var name = (NewStudentNameBox.Text ?? "").Trim();
        if (id.Length == 0 || name.Length == 0)
        {
            RosterError.Text = "学号与姓名必填。";
            return;
        }

        try
        {
            await ConnectionGate.RunAsync(() => AppServices.Client.AddStudent(session.UserId, id, name));
            NewStudentIdBox.Text = "";
            NewStudentNameBox.Text = "";
            await LoadAsync();
        }
        catch (RectaException ex)
        {
            RosterError.Text = ConnectionGate.Friendly(ex);
        }
    }

    private async void OnRenameStudent(object? sender, RoutedEventArgs e)
    {
        RosterError.Text = "";
        var session = AppServices.Session;
        if (session is null || Rows.SelectedItem is not StudentRowVm row)
        {
            return;
        }
        var name = (RenameBox.Text ?? "").Trim();
        if (name.Length == 0)
        {
            RosterError.Text = "新姓名必填。";
            return;
        }

        try
        {
            await ConnectionGate.RunAsync(() => AppServices.Client.RenameStudent(session.UserId, row.StudentId, name));
            RenameBox.Text = "";
            await LoadAsync();
        }
        catch (RectaException ex)
        {
            RosterError.Text = ConnectionGate.Friendly(ex);
        }
    }
}
