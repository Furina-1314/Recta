using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using Recta.App.NativeInterop;

namespace Recta.App.Pages;

// 台账行视图模型(Brush 直接携带,避免 DataTrigger)。
public sealed record RequestRow(
    int Id,
    string IdText,
    string Title,
    string Category,
    string CategoryLabel,
    string Status,
    string StatusLabel,
    IBrush StatusBrush,
    string AmountText,
    long AppliedCents);

public sealed record SplitLineVm(string Display, string AmountText);

public partial class RequestsPage : UserControl, IRefreshable
{
    private IReadOnlyList<ExpenseRequestDto> _all = [];
    private Dictionary<string, string> _userNameById = new();
    private RequestBundle? _current;

    public RequestsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => _ = LoadAsync();
    }

    private async void OnToggleNewRequest(object? sender, RoutedEventArgs e)
    {
        var dialog = new Windows.NewRequestDialog();
        await dialog.ShowDialog(TopLevel.GetTopLevel(this) as Window
                                ?? throw new InvalidOperationException());
        if (dialog.Submitted)
        {
            await LoadAsync();
        }
    }

    private void OnRefresh(object? sender, RoutedEventArgs e) => _ = LoadAsync();

    // ---------- 数据装载 ----------

    public Task RefreshAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        HideInspectorError();
        if (!AppServices.NativeReady)
        {
            PageSummary.Text = "原生核心未初始化";
            return;
        }

        try
        {
            var status = SelectedTag(StatusFilter);
            var category = SelectedTag(CategoryFilter);
            var search = (SearchBox.Text ?? string.Empty).Trim();

            var usersTask = ConnectionGate.RunAsync(() => AppServices.Client.ListUsers());
            var requestsTask = ConnectionGate.RunAsync(() => AppServices.Client.ListRequests(
                string.IsNullOrEmpty(status) ? null : status,
                string.IsNullOrEmpty(category) ? null : category));
            var users = await usersTask;
            var requests = await requestsTask;

            _userNameById = users.ToDictionary(u => u.Id, u => u.DisplayName);

            IEnumerable<ExpenseRequestDto> view = requests;
            if (search.Length > 0)
            {
                view = view.Where(r =>
                    r.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    r.Id.ToString().Contains(search, StringComparison.OrdinalIgnoreCase));
            }
            _all = requests;
            Rows.ItemsSource = view.Select(ToRow).ToList();
            EmptyHint.IsVisible = Rows.ItemsSource is not IReadOnlyCollection<RequestRow> rows || rows.Count == 0;

            var pending = requests.Count(r => r.Status == "PENDING_REVIEW");
            var approved = requests.Count(r => r.Status == "APPROVED");
            PageSummary.Text = $"共 {requests.Count} 项记录 | 待审理 {pending} 项 | 待办结 {approved} 项";
        }
        catch (RectaException ex)
        {
            PageSummary.Text = $"加载失败:{ConnectionGate.Friendly(ex)}";
        }
    }

    private static string SelectedTag(ComboBox box) =>
        (box.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    private RequestRow ToRow(ExpenseRequestDto r) => new(
        r.Id,
        $"REQ-{r.Id:D6}",
        r.Title,
        r.AccountCategory,
        CategoryLabel(r.AccountCategory),
        r.Status,
        StatusLabel(r.Status),
        StatusBrush(r.Status),
        RectaClient.FormatMoney(r.AppliedAmountCents),
        r.AppliedAmountCents);

    // ---------- 筛选/搜索 ----------

    private void OnFilterChanged(object? sender, SelectionChangedEventArgs e) => _ = LoadAsync();

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => _ = LoadAsync();

    // ---------- 选中 → Inspector ----------

    private async void OnRowSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (Rows.SelectedItem is not RequestRow row)
        {
            return;
        }
        HideInspectorError();
        ResultPanel.IsVisible = false;

        try
        {
            _current = await ConnectionGate.RunAsync(() => AppServices.Client.GetRequest(row.Id));
            FillInspector(_current);
        }
        catch (RectaException ex)
        {
            ShowInspectorError(ConnectionGate.Friendly(ex));
        }
    }

    private void FillInspector(RequestBundle bundle)
    {
        var r = bundle.Request;
        EmptyPanel.IsVisible = false;
        DetailPanel.IsVisible = true;

        ReqNoText.Text = $"单号 REQ-{r.Id:D6}";
        ReqTitleText.Text = $"{r.Title}({CategoryLabel(r.AccountCategory)})";
        ApplicantText.Text = _userNameById.TryGetValue(r.ApplicantId, out var name) ? name : r.ApplicantId;

        StatusText.Text = StatusLabel(r.Status);
        StatusText.Foreground = StatusBrush(r.Status);

        AppliedText.Text = RectaClient.FormatMoney(r.AppliedAmountCents);
        ApprovedText.Text = r.ApprovedAmountCents is { } a ? RectaClient.FormatMoney(a) : "—";
        SettledText.Text = r.SettledAmountCents is { } s ? RectaClient.FormatMoney(s) : "—";

        ReviewNotesLabel.IsVisible = ReviewNotesText.IsVisible = !string.IsNullOrEmpty(r.ReviewNotes);
        ReviewNotesText.Text = r.ReviewNotes ?? "";
        SettleNotesLabel.IsVisible = SettleNotesText.IsVisible = !string.IsNullOrEmpty(r.SettlementNotes);
        SettleNotesText.Text = r.SettlementNotes ?? "";

        // 平摊名单(班费)
        SplitsPanel.IsVisible = bundle.Splits.Count > 0;
        if (bundle.Splits.Count > 0)
        {
            var perHead = bundle.Request.ApprovedAmountCents is { } approved
                ? approved / bundle.Splits.Count
                : bundle.Request.AppliedAmountCents / bundle.Splits.Count;
            SplitsHeaderText.Text =
                $"平摊名单({bundle.Splits.Count} 人,人均 {RectaClient.FormatMoney(perHead)} 元)";
            SplitsList.ItemsSource = bundle.Splits.Select(s => new SplitLineVm(
                $"{s.StudentName ?? s.StudentId}{(s.IsTailBearer ? "(承担尾差)" : "")}",
                RectaClient.FormatMoney(s.AmountCents))).ToList();
        }

        // 角色门控(服务端仍强校验,这里只做呈现层隐藏)
        var session = AppServices.Session;
        var role = session?.Role ?? "";
        var canReview = r.Status == "PENDING_REVIEW" && CanReview(role, r.AccountCategory);
        var canSettle = r.Status == "APPROVED" && CanSettle(role, r.AccountCategory);

        ReviewPanel.IsVisible = canReview;
        if (canReview)
        {
            ApproveAmountBox.Text = RectaClient.FormatMoney(r.AppliedAmountCents);
            ReviewNotesBox.Text = "";
        }
        SettlePanel.IsVisible = canSettle;
        if (canSettle)
        {
            SettleNotesBox.Text = "";
        }
    }

    internal static bool CanReview(string role, string category) => (role, category) switch
    {
        ("BRANCH_SECRETARY", "FLEXIBLE") => true,
        ("LIFE_COMMITTEE", "FACULTY") => true,
        ("BRANCH_SECRETARY", "CLASS_FUND") => true,
        ("LIFE_COMMITTEE", "CLASS_FUND") => true,
        _ => false,
    };

    internal static bool CanSettle(string role, string category) => (role, category) switch
    {
        ("BRANCH_SECRETARY", "FLEXIBLE") => true,
        ("LIFE_COMMITTEE", "FACULTY") => true,
        ("LIFE_COMMITTEE", "CLASS_FUND") => true,
        _ => false,
    };

    // ---------- 操作 ----------

    private async void OnApprove(object? sender, RoutedEventArgs e)
    {
        if (_current is null)
        {
            return;
        }
        HideInspectorError();

        long approvedCents;
        try
        {
            approvedCents = RectaClient.ParseMoney(ApproveAmountBox.Text ?? "");
        }
        catch (RectaException ex)
        {
            ShowInspectorError($"核准金额格式错误:{ConnectionGate.Friendly(ex)}");
            return;
        }

        var notes = (ReviewNotesBox.Text ?? "").Trim();
        if (approvedCents < _current.Request.AppliedAmountCents && notes.Length == 0)
        {
            ShowInspectorError("核减时必须填写核减原因。");
            return;
        }

        try
        {
            await ConnectionGate.RunAsync(() => AppServices.Client.ApproveRequest(
                AppServices.Session!.UserId, _current.Request.Id, approvedCents,
                notes.Length == 0 ? null : notes));
            await LoadAndReselectAsync();
        }
        catch (RectaException ex)
        {
            ShowInspectorError(ConnectionGate.Friendly(ex));
        }
    }

    private async void OnReject(object? sender, RoutedEventArgs e)
    {
        if (_current is null)
        {
            return;
        }
        HideInspectorError();

        var notes = (ReviewNotesBox.Text ?? "").Trim();
        if (notes.Length == 0)
        {
            ShowInspectorError("请填写驳回原因。");
            return;
        }

        try
        {
            await ConnectionGate.RunAsync(() => AppServices.Client.RejectRequest(
                AppServices.Session!.UserId, _current.Request.Id, notes));
            await LoadAndReselectAsync();
        }
        catch (RectaException ex)
        {
            ShowInspectorError(ConnectionGate.Friendly(ex));
        }
    }

    private async void OnSettle(object? sender, RoutedEventArgs e)
    {
        if (_current is null)
        {
            return;
        }
        HideInspectorError();

        var extra = (SettleNotesBox.Text ?? "").Trim();

        try
        {
            var outcome = await ConnectionGate.RunAsync(() => AppServices.Client.SettleRequest(
                AppServices.Session!.UserId, _current.Request.Id, extra.Length == 0 ? null : extra));

            ResultPanel.IsVisible = true;
            if (outcome.Advances.Count == 0)
            {
                ResultText.Text = $"办结出账 {RectaClient.FormatMoney(outcome.SettledCents)} 元,无生委垫资。";
            }
            else
            {
                var lines = string.Join(";", outcome.Advances
                    .Select(a => $"{a.Name} {RectaClient.FormatMoney(a.AmountCents)} 元"));
                ResultText.Text =
                    $"办结出账 {RectaClient.FormatMoney(outcome.SettledCents)} 元;生委垫资共 " +
                    $"{RectaClient.FormatMoney(outcome.TotalAdvanceCents)} 元:{lines}。";
            }
            await LoadAndReselectAsync();
        }
        catch (RectaException ex)
        {
            ShowInspectorError(ex.IsState ? ConnectionGate.Friendly(ex) : $"办结失败:{ConnectionGate.Friendly(ex)}");
        }
    }

    // 操作后重载并保持选中。
    private async Task LoadAndReselectAsync()
    {
        var selectedId = _current?.Request.Id;
        await LoadAsync();
        if (selectedId is { } id && Rows.ItemsSource is IEnumerable<RequestRow> rows)
        {
            Rows.SelectedItem = rows.FirstOrDefault(r => r.Id == id);
        }
    }

    // ---------- 呈现辅助 ----------

    private static string CategoryLabel(string category) => category switch
    {
        "FLEXIBLE" => "灵活公款",
        "FACULTY" => "系里报销",
        "CLASS_FUND" => "班费平摊",
        _ => category,
    };

    private static string StatusLabel(string status) => status switch
    {
        "PENDING_REVIEW" => "待审理",
        "APPROVED" => "待办结",
        "SETTLED" => "已办结",
        "REJECTED" => "已驳回",
        _ => status,
    };

    private IBrush StatusBrush(string status)
    {
        var key = status switch
        {
            "PENDING_REVIEW" => "RectaPendingBrush",
            "APPROVED" => "RectaAccentBrush",
            "SETTLED" => "RectaPositiveBrush",
            _ => "RectaDangerBrush",
        };
        return (Application.Current?.FindResource(key) as IBrush) ?? Brushes.Gray;
    }

    private void ShowInspectorError(string message)
    {
        InspectorError.Text = message;
        InspectorError.IsVisible = true;
    }

    private void HideInspectorError() => InspectorError.IsVisible = false;
}
