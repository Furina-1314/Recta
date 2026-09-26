using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Recta.App.NativeInterop;

namespace Recta.App.Windows;

public sealed class StudentCheckVm : INotifyPropertyChanged
{
    public string StudentId { get; init; } = "";
    public string Label { get; init; } = "";

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
            {
                return;
            }
            _isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record RelatedRequestVm(int RequestId, string Label);

/// <summary>统一提单入口:支出提单(三渠道)或入账登记(三通道,班费充值支持批量)。</summary>
public partial class NewRequestDialog : Window
{
    private readonly ObservableCollection<StudentCheckVm> _students = [];
    private readonly ObservableCollection<StudentCheckVm> _incomeStudents = [];
    private bool _incomeMode;

    /// <summary>提交成功时为 true(调用方负责刷新)。</summary>
    public bool Submitted { get; private set; }

    public NewRequestDialog()
    {
        InitializeComponent();
        StudentChecks.ItemsSource = _students;
        IncomeChecks.ItemsSource = _incomeStudents;
        Loaded += async (_, _) => await LoadLookupsAsync();
    }

    private async Task LoadLookupsAsync()
    {
        if (!AppServices.NativeReady)
        {
            return;
        }
        try
        {
            var students = await ConnectionGate.RunAsync(() => AppServices.Client.ListStudents());
            foreach (var s in students)
            {
                var label = $"{s.Name}({s.StudentId})";

                // 支出平摊名单
                var expenseOption = new StudentCheckVm { StudentId = s.StudentId, Label = label };
                expenseOption.PropertyChanged += (_, _) =>
                {
                    RefreshTailBearers();
                    UpdateSplitSelectAllState();
                    UpdatePreview();
                };
                _students.Add(expenseOption);

                // 入账充值对象
                var incomeOption = new StudentCheckVm { StudentId = s.StudentId, Label = label };
                incomeOption.PropertyChanged += (_, _) => UpdateIncomeSelectAllState();
                _incomeStudents.Add(incomeOption);
            }
        }
        catch (RectaException)
        {
            // 名单加载失败时提交会给出明确错误。
        }

        try
        {
            var settled = await ConnectionGate.RunAsync(() =>
                AppServices.Client.ListRequests("SETTLED", "FACULTY", null));
            IncomeRequestBox.ItemsSource = settled
                .Select(r => new RelatedRequestVm(r.Id,
                    $"单号 REQ-{r.Id:D6} {r.Title} {RectaClient.FormatMoney(r.SettledAmountCents ?? 0)} 元"))
                .ToList();
            if (IncomeRequestBox.ItemCount > 0)
            {
                IncomeRequestBox.SelectedIndex = 0;
            }
        }
        catch (RectaException)
        {
            // 无已办结系报销单时留空,提交时校验。
        }
    }

    // ---------- 模式切换 ----------

    private void OnTabExpense(object? sender, RoutedEventArgs e) => SetMode(income: false);

    private void OnTabIncome(object? sender, RoutedEventArgs e) => SetMode(income: true);

    private void SetMode(bool income)
    {
        _incomeMode = income;
        ExpensePanel.IsVisible = !income;
        IncomePanel.IsVisible = income;
        ExpenseTab.Classes.Set("accent", !income);
        IncomeTab.Classes.Set("accent", income);
        ErrorText.IsVisible = false;
    }

    // ---------- 支出 ----------

    private string SelectedChannel =>
        (ChannelBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    private void OnChannelChanged(object? sender, SelectionChangedEventArgs e)
    {
        SplitSection.IsVisible = SelectedChannel == "CLASS_FUND";
        UpdatePreview();
    }

    private void RefreshTailBearers()
    {
        var previous = (TailBearerBox.SelectedItem as StudentCheckVm)?.StudentId;
        var checkedOptions = _students.Where(s => s.IsChecked).ToList();
        TailBearerBox.ItemsSource = checkedOptions;
        if (checkedOptions.Count == 0)
        {
            TailBearerBox.SelectedItem = null;
        }
        else if (checkedOptions.All(c => c.StudentId != previous))
        {
            TailBearerBox.SelectedItem = checkedOptions[0];
        }
        UpdatePreview();
    }

    private void OnSplitSelectAll(object? sender, RoutedEventArgs e)
    {
        // 语义:当前未全选 → 全选;已全选 → 取消全部。
        var target = !_students.All(s => s.IsChecked);
        foreach (var option in _students)
        {
            option.IsChecked = target;
        }
    }

    private void UpdateSplitSelectAllState()
    {
        SplitSelectAll.IsChecked = _students.Count > 0 && _students.All(s => s.IsChecked)
            ? true
            : _students.Any(s => s.IsChecked) ? null : false;
    }

    private void UpdatePreview()
    {
        SplitPreview.Text = "—";
        if (SelectedChannel != "CLASS_FUND")
        {
            return;
        }
        var ids = _students.Where(s => s.IsChecked).Select(s => s.StudentId).ToList();
        var bearer = TailBearerBox.SelectedItem as StudentCheckVm;
        if (ids.Count == 0 || bearer is null || !TryParseAmount(AmountBox.Text, out var cents))
        {
            return;
        }
        try
        {
            var result = RectaClient.Distribute(cents, ids, bearer.StudentId);
            var perHead = result.Allocations.FirstOrDefault(a => !a.IsTailBearer)?.AmountCents
                          ?? result.Allocations[0].AmountCents;
            var tail = result.Allocations.FirstOrDefault(a => a.IsTailBearer);
            var bearerName = bearer.Label.Contains('(')
                ? bearer.Label[..bearer.Label.IndexOf('(')]
                : bearer.Label;
            SplitPreview.Text =
                $"共 {result.Count} 人参摊:人均 {RectaClient.FormatMoney(perHead)} 元" +
                (tail is not null && tail.AmountCents != perHead
                    ? $";尾差承担人 {bearerName} 扣 {RectaClient.FormatMoney(tail.AmountCents)} 元"
                    : ";无尾差(整除)") +
                $";分项合计 {RectaClient.FormatMoney(result.SumCents)} 元";
        }
        catch (RectaException)
        {
            SplitPreview.Text = "预演失败:名单或金额不合法。";
        }
    }

    // ---------- 入账 ----------

    private string SelectedIncomeKind =>
        (IncomeKindBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    private void OnIncomeKindChanged(object? sender, SelectionChangedEventArgs e)
    {
        var kind = SelectedIncomeKind;
        IncomeStudentsSection.IsVisible = kind == "TO_STUDENT_SUB_ACCOUNT";
        IncomeRequestSection.IsVisible = kind == "TO_FACULTY_REIMBURSE";
    }

    private void OnIncomeSelectAll(object? sender, RoutedEventArgs e)
    {
        var target = !_incomeStudents.All(s => s.IsChecked);
        foreach (var option in _incomeStudents)
        {
            option.IsChecked = target;
        }
    }

    private void UpdateIncomeSelectAllState()
    {
        IncomeSelectAll.IsChecked = _incomeStudents.Count > 0 && _incomeStudents.All(s => s.IsChecked)
            ? true
            : _incomeStudents.Any(s => s.IsChecked) ? null : false;
    }

    // ---------- 提交 ----------

    private static bool TryParseAmount(string? text, out long cents)
    {
        cents = 0;
        try
        {
            cents = RectaClient.ParseMoney(text ?? "");
            return cents > 0;
        }
        catch (RectaException)
        {
            return false;
        }
    }

    private async void OnSubmit(object? sender, RoutedEventArgs e)
    {
        ErrorText.IsVisible = false;
        var session = AppServices.Session;
        if (session is null)
        {
            ShowError("会话无效,请重新登录。");
            return;
        }

        try
        {
            if (_incomeMode)
            {
                await SubmitIncomeAsync(session);
            }
            else
            {
                await SubmitExpenseAsync(session);
            }
            Submitted = true;
            Close();
        }
        catch (RectaException ex)
        {
            ShowError(ConnectionGate.Friendly(ex));
        }
    }

    private async Task SubmitExpenseAsync(LoginSession session)
    {
        var channel = SelectedChannel;
        var title = (TitleBox.Text ?? "").Trim();
        if (title.Length == 0)
        {
            throw new RectaException(RectaErrors.InvalidArg, "请填写动账事项。");
        }
        if (!TryParseAmount(AmountBox.Text, out var cents))
        {
            throw new RectaException(RectaErrors.InvalidArg, "金额必须是正数,例如 140.00。");
        }

        List<string>? participants = null;
        string? bearerId = null;
        if (channel == "CLASS_FUND")
        {
            participants = _students.Where(s => s.IsChecked).Select(s => s.StudentId).ToList();
            bearerId = (TailBearerBox.SelectedItem as StudentCheckVm)?.StudentId;
            if (participants.Count == 0 || bearerId is null)
            {
                throw new RectaException(RectaErrors.InvalidArg, "班费平摊须勾选参摊同学并指定尾差承担人。");
            }
        }

        var voucher = (VoucherBox.Text ?? "").Trim();
        await ConnectionGate.RunAsync(() => AppServices.Client.SubmitRequest(
            session.UserId, title, channel, cents, participants, bearerId,
            voucher.Length == 0 ? null : voucher));
    }

    private async Task SubmitIncomeAsync(LoginSession session)
    {
        var kind = SelectedIncomeKind;
        if (!TryParseAmount(IncomeAmountBox.Text, out var cents))
        {
            throw new RectaException(RectaErrors.InvalidArg, "金额必须是正数,例如 500.00。");
        }
        var source = (IncomeSourceBox.Text ?? "").Trim();
        if (source.Length == 0)
        {
            throw new RectaException(RectaErrors.InvalidArg, "请填写来源说明。");
        }
        var voucher = (IncomeVoucherBox.Text ?? "").Trim();

        switch (kind)
        {
            case "TO_STUDENT_SUB_ACCOUNT":
            {
                // 批量充值:勾选的同学每人统一充入等额,单事务原子。
                var ids = _incomeStudents.Where(s => s.IsChecked).Select(s => s.StudentId).ToList();
                if (ids.Count == 0)
                {
                    throw new RectaException(RectaErrors.InvalidArg, "请勾选充值对象。");
                }
                await ConnectionGate.RunAsync(() => AppServices.Client.RecordInflowBatch(
                    session.UserId, source, cents, voucher.Length == 0 ? null : voucher, ids));
                break;
            }
            case "TO_FACULTY_REIMBURSE":
            {
                var related = (IncomeRequestBox.SelectedItem as RelatedRequestVm)?.RequestId;
                if (related is null)
                {
                    throw new RectaException(RectaErrors.InvalidArg, "系报销核销必须关联一笔已办结的报销单。");
                }
                await ConnectionGate.RunAsync(() => AppServices.Client.RecordInflow(
                    session.UserId, kind, cents, source, null, related,
                    voucher.Length == 0 ? null : voucher));
                break;
            }
            default:
                await ConnectionGate.RunAsync(() => AppServices.Client.RecordInflow(
                    session.UserId, kind, cents, source, null, null,
                    voucher.Length == 0 ? null : voucher));
                break;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }
}
