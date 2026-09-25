using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Recta.App.NativeInterop;

namespace Recta.App.Pages;

public sealed record StudentRowVm(
    string StudentId, string Name, long BalanceCents, string BalanceText, IBrush BalanceBrush);

public sealed record LedgerLineVm(
    string TypeLabel, IBrush TypeBrush, string Note, string ChangeText, string BalanceText);

public partial class StudentsPage : UserControl, IRefreshable
{
    private IReadOnlyList<StudentDto> _students = [];
    private List<LedgerEntryDto> _studentLedger = [];
    private StudentRowVm? _selectedStudent;

    // 主题画刷统一取用:控制级 FindResource 在部分时机返回 UnsetValue,
    // 改走 Application 资源并提供灰兜底。
    private IBrush ThemeBrush(string key) =>
        (Application.Current?.FindResource(key) as IBrush) ?? Brushes.Gray;

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

            _students = students;
            Rows.ItemsSource = students.Select(s => new StudentRowVm(
                s.StudentId, s.Name, s.BalanceCents,
                RectaClient.FormatMoney(s.BalanceCents),
                s.BalanceCents < 0
                    ? ThemeBrush("RectaDangerBrush")
                    : ThemeBrush("RectaTextPrimaryBrush"))).ToList();

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
        _selectedStudent = row;
        RenameBox.IsEnabled = RenameButton.IsEnabled = true;
        DetailText.Text = $"{row.Name}({row.StudentId}):当前余额 {row.BalanceText} 元。" +
                          (row.BalanceCents < 0 ? "透支部分构成对生活委员个人的无息借贷。" : "结余由生活委员受托代管。");
        try
        {
            var entries = await ConnectionGate.RunAsync(() => AppServices.Client.ListStudentLedger(row.StudentId));
            _studentLedger = [.. entries];
            ExportLedgerButton.IsEnabled = entries.Count > 0;
            LedgerList.ItemsSource = entries.Select(entry => new LedgerLineVm(
                TypeLabel(entry.EntryType),
                entry.ChangeCents >= 0
                    ? ThemeBrush("RectaPositiveBrush")
                    : ThemeBrush("RectaDangerBrush"),
                entry.Notes ?? "",
                (entry.ChangeCents > 0 ? "+" : "") + RectaClient.FormatMoney(entry.ChangeCents),
                RectaClient.FormatMoney(entry.BalanceAfterCents))).ToList();
        }
        catch (RectaException ex)
        {
            _studentLedger = [];
            ExportLedgerButton.IsEnabled = false;
            LedgerList.ItemsSource = Array.Empty<LedgerLineVm>();
            DetailText.Text = $"流水加载失败:{ConnectionGate.Friendly(ex)}";
        }
    }

    private async void OnExportRoster(object? sender, RoutedEventArgs e)
    {
        if (_students.Count == 0)
        {
            SummaryText.Text = "名单为空,先在上方录入同学。";
            return;
        }
        var file = await LedgerPage.PickSaveFileAsync(this, "导出分户名单", "Recta分户");
        if (file is null)
        {
            return;
        }
        var data = new List<IReadOnlyList<string?>>();
        data.Add(["学号", "姓名", "余额(元)"]);
        foreach (var s2 in _students)
        {
            data.Add([s2.StudentId, s2.Name, RectaClient.FormatMoney(s2.BalanceCents)]);
        }
        var path = file.Path.LocalPath;
        CsvExport.Write(path, data);
        SummaryText.Text = $"已导出 {_students.Count} 名同学 → {path}";
    }

    private async void OnExportStudentLedger(object? sender, RoutedEventArgs e)
    {
        if (_selectedStudent is null || _studentLedger.Count == 0)
        {
            return;
        }
        var file = await LedgerPage.PickSaveFileAsync(this, "导出个人流水",
            $"Recta流水_{_selectedStudent.Name}");
        if (file is null)
        {
            return;
        }
        var data = new List<IReadOnlyList<string?>>();
        data.Add(["日期", "类型", "变动(元)", "变动后余额(元)", "备注"]);
        foreach (var entry in _studentLedger)
        {
            data.Add([
                entry.CreatedAt is { Length: >= 10 } t ? t[..10] : "—",
                TypeLabel(entry.EntryType),
                (entry.ChangeCents > 0 ? "+" : "") + RectaClient.FormatMoney(entry.ChangeCents),
                RectaClient.FormatMoney(entry.BalanceAfterCents),
                entry.Notes ?? "",
            ]);
        }
        var path = file.Path.LocalPath;
        CsvExport.Write(path, data);
        DetailText.Text = $"已导出 {_studentLedger.Count} 条流水 → {path}";
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
