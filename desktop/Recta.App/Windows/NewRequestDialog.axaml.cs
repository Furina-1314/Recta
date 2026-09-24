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

public sealed record StudentComboVm(string StudentId, string Label);

public sealed record RelatedRequestVm(int RequestId, string Label);

/// <summary>统一提单入口:支出提单(三渠道)或入账登记(三通道)。</summary>
public partial class NewRequestDialog : Window
{
    private readonly ObservableCollection<StudentCheckVm> _students = [];
    private bool _incomeMode;

    /// <summary>提交成功时为 true(调用方负责刷新)。</summary>
    public bool Submitted { get; private set; }

    public NewRequestDialog()
    {
        InitializeComponent();
        StudentChecks.ItemsSource = _students;
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
                var option = new StudentCheckVm { StudentId = s.StudentId, Label = $"{s.Name}({s.StudentId})" };
                option.PropertyChanged += (_, _) =>
                {
                    RefreshTailBearers();
                    UpdatePreview();
                };
                _students.Add(option);
            }
            IncomeStudentBox.ItemsSource = students
                .Select(s => new StudentComboVm(s.StudentId, $"{s.Name}({s.StudentId})")).ToList();
            if (IncomeStudentBox.ItemCount > 0)
            {
                IncomeStudentBox.SelectedIndex = 0;
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

    private string SelectedChannel => (ChannelBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

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
            SplitPreview.Text =
                $"共 {result.Count} 人参摊:人均 {RectaClient.FormatMoney(perHead)} 元" +
                (tail is not null && tail.AmountCents != perHead
                    ? $";尾差承担人 {bearer.Label[..bearer.Label.IndexOf('(')]} 扣 {RectaClient.FormatMoney(tail.AmountCents)} 元"
                    : ";无尾差(整除)") +
                $";分项合计 {RectaClient.FormatMoney(result.SumCents)} 元";
        }
        catch (RectaException)
        {
            SplitPreview.Text = "预演失败:名单或金额不合法。";
        }
    }

    // ---------- 增资 ----------

    private string SelectedIncomeKind =>
        (IncomeKindBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    private void OnIncomeKindChanged(object? sender, SelectionChangedEventArgs e)
    {
        var kind = SelectedIncomeKind;
        IncomeStudentSection.IsVisible = kind == "TO_STUDENT_SUB_ACCOUNT";
        IncomeRequestSection.IsVisible = kind == "TO_FACULTY_REIMBURSE";
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

        string? targetStudent = null;
        int? relatedRequest = null;
        switch (kind)
        {
            case "TO_STUDENT_SUB_ACCOUNT":
                targetStudent = (IncomeStudentBox.SelectedItem as StudentComboVm)?.StudentId;
                if (targetStudent is null)
                {
                    throw new RectaException(RectaErrors.InvalidArg, "请选择充值对象。");
                }
                break;
            case "TO_FACULTY_REIMBURSE":
                relatedRequest = (IncomeRequestBox.SelectedItem as RelatedRequestVm)?.RequestId;
                if (relatedRequest is null)
                {
                    throw new RectaException(RectaErrors.InvalidArg, "系报销核销必须关联一笔已办结的报销单。");
                }
                break;
        }

        await ConnectionGate.RunAsync(() => AppServices.Client.RecordInflow(
            session.UserId, kind, cents, source, targetStudent, relatedRequest,
            voucher.Length == 0 ? null : voucher));
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }
}
