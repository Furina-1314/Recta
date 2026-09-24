using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Recta.App.NativeInterop;

namespace Recta.App.Pages;

public sealed record LedgerRowVm(
    string No, string Title, string ChannelLabel, string AmountText, IBrush AmountBrush,
    string DateText, string Kind, string Channel, int RequestId, long InflowId, string DateIso);

public partial class LedgerPage : UserControl, IRefreshable
{
    // 主题画刷统一取用:控制级 FindResource 在部分时机返回 UnsetValue,
    // 改走 Application 资源并提供灰兜底。
    private IBrush ThemeBrush(string key) =>
        (Application.Current?.FindResource(key) as IBrush) ?? Brushes.Gray;

    private IReadOnlyList<ExpenseRequestDto> _requests = [];
    private IReadOnlyList<InflowDto> _inflows = [];
    private Dictionary<string, string> _userNameById = new();
    private Dictionary<string, string> _studentNameById = new();
    private bool _classFundOnly;

    public LedgerPage()
    {
        InitializeComponent();
        Loaded += (_, _) => _ = LoadAsync();
    }

    public Task RefreshAsync() => LoadAsync();

    // 班费相关流水:团支书与生活委员均可见;其余详细流水仅团支书(§5.1 可见性)。
    private static bool RoleSeesAll(string? role) => role == "BRANCH_SECRETARY";

    private async Task LoadAsync()
    {
        if (!AppServices.NativeReady)
        {
            SummaryText.Text = "服务未启动";
            return;
        }

        var role = AppServices.Session?.Role;
        _classFundOnly = !RoleSeesAll(role);

        try
        {
            var usersTask = ConnectionGate.RunAsync(() => AppServices.Client.ListUsers());
            var studentsTask = ConnectionGate.RunAsync(() => AppServices.Client.ListStudents());
            var requestsTask = ConnectionGate.RunAsync(() => AppServices.Client.ListRequests());
            var inflowsTask = ConnectionGate.RunAsync(() => AppServices.Client.ListInflows());
            var overviewTask = ConnectionGate.RunAsync(() => AppServices.Client.GetOverview());

            var users = await usersTask;
            var students = await studentsTask;
            _requests = await requestsTask;
            _inflows = await inflowsTask;
            var overview = await overviewTask;

            _userNameById = users.ToDictionary(u => u.Id, u => u.DisplayName);
            _studentNameById = students.ToDictionary(s => s.StudentId, s => s.Name);

            FlexibleValue.Text = RectaClient.FormatMoney(overview.FlexibleBalanceCents);
            FacultyValue.Text = RectaClient.FormatMoney(overview.FacultyHangingCents);
            CashValue.Text = RectaClient.FormatMoney(overview.Custody.CustodianCashCents);
            AdvanceValue.Text = RectaClient.FormatMoney(overview.Custody.AdvanceTotalCents);

            ApplyFilter();
        }
        catch (RectaException ex)
        {
            SummaryText.Text = $"加载失败:{ConnectionGate.Friendly(ex)}";
        }
        catch (Exception ex)
        {
            var where = ex.StackTrace is { Length: > 0 } st ? st[..Math.Min(st.IndexOf('\n') is int n && n > 0 ? n : st.Length, 160)] : "";
            SummaryText.Text = $"加载异常:{ex.Message} @ {where}";
        }
    }

    private void ApplyFilter()
    {
        var kind = (KindFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        var channel = (ChannelFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        var search = (SearchBox.Text ?? "").Trim();

        IEnumerable<LedgerRowVm> rows = _requests
            .Where(r => !_classFundOnly || r.AccountCategory == "CLASS_FUND")
            .Select(ToRequestRow);
        rows = rows.Concat(_inflows
            .Where(i => !_classFundOnly || i.DestinationType == "TO_STUDENT_SUB_ACCOUNT")
            .Select(ToInflowRow));

        if (kind.Length > 0)
        {
            rows = kind switch
            {
                "EXPENSE" => rows.Where(r => r.Kind == "EXPENSE"),
                "CLASS_FUND" => rows.Where(r => r.Channel == "CLASS_FUND" ||
                                                (r.Kind == "INCOME" && r.Channel == "TO_STUDENT_SUB_ACCOUNT")),
                "INCOME" => rows.Where(r => r.Kind == "INCOME"),
                _ => rows,
            };
        }
        if (channel.Length > 0)
        {
            rows = rows.Where(r => r.Channel == channel || r.Channel == channel);
        }
        if (search.Length > 0)
        {
            rows = rows.Where(r => r.No.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                   r.Title.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var list = rows.OrderByDescending(r => r.DateIso, StringComparer.Ordinal).ToList();
        Rows.ItemsSource = list;
        SummaryText.Text = $"共 {list.Count} 条流水(支出 {_requests.Count} 笔 · 入账 {_inflows.Count} 笔)";
    }

    private LedgerRowVm ToRequestRow(ExpenseRequestDto r) => new(
        $"REQ-{r.Id:D6}",
        r.Title,
        ChannelLabel(r.AccountCategory),
        RectaClient.FormatMoney(r.SettledAmountCents ?? r.ApprovedAmountCents ?? r.AppliedAmountCents),
        ThemeBrush("RectaDangerBrush"),
        ShortDate(r.SettledAt ?? r.CreatedAt),
        "EXPENSE",
        r.AccountCategory,
        r.Id, 0, r.SettledAt ?? r.CreatedAt ?? "");

    private LedgerRowVm ToInflowRow(InflowDto i) => new(
        $"INF-{i.Id:D6}",
        i.SourceTitle,
        InflowLabel(i.DestinationType),
        RectaClient.FormatMoney(i.AmountCents),
        ThemeBrush("RectaPositiveBrush"),
        ShortDate(i.CreatedAt),
        "INCOME",
        i.DestinationType,
        0, i.Id, i.CreatedAt ?? "");

    private static string ShortDate(string? iso) =>
        iso is { Length: >= 10 } t ? t[..10] : "—";

    private static string ChannelLabel(string channel) => channel switch
    {
        "FLEXIBLE" => "灵活公款",
        "FACULTY" => "系里报销",
        "CLASS_FUND" => "班费平摊",
        _ => channel,
    };

    private static string InflowLabel(string destination) => destination switch
    {
        "TO_FLEXIBLE_ACCOUNT" => "灵活公款",
        "TO_FACULTY_REIMBURSE" => "系报销核销",
        "TO_STUDENT_SUB_ACCOUNT" => "班费充值",
        _ => destination,
    };

    private void OnFilterChanged(object? sender, SelectionChangedEventArgs e) => ApplyFilter();

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnRefresh(object? sender, RoutedEventArgs e) => _ = LoadAsync();

    private async void OnNewEntry(object? sender, RoutedEventArgs e)
    {
        var dialog = new Windows.NewRequestDialog();
        await dialog.ShowDialog(TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException());
        if (dialog.Submitted)
        {
            await LoadAsync();
        }
    }

    // ---------- 详情 ----------

    private async void OnRowSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (Rows.SelectedItem is not LedgerRowVm row)
        {
            return;
        }
        DetailPanel.Children.Clear();
        DetailPanel.Children.Add(new TextBlock { Text = "加载中…", Classes = { "secondary" } });

        try
        {
            if (row.Kind == "EXPENSE")
            {
                var bundle = await ConnectionGate.RunAsync(() => AppServices.Client.GetRequest(row.RequestId));
                FillRequestDetail(bundle);
            }
            else
            {
                FillInflowDetail(row);
            }
        }
        catch (RectaException ex)
        {
            DetailPanel.Children.Clear();
            DetailPanel.Children.Add(new TextBlock
            {
                Text = ConnectionGate.Friendly(ex), TextWrapping = TextWrapping.Wrap, Classes = { "danger-text" },
            });
        }
    }

    private void FillRequestDetail(RequestBundle bundle)
    {
        var r = bundle.Request;
        DetailPanel.Children.Clear();

        AddHeader(DetailPanel, $"支出单 {RectaClient.FormatMoney(r.SettledAmountCents ?? r.AppliedAmountCents)} 元",
                  r.Status == "SETTLED" ? "RectaPositiveBrush" : r.Status == "REJECTED" ? "RectaDangerBrush" : "RectaPendingBrush",
                  StatusLabel(r.Status));
        AddField("单号", $"REQ-{r.Id:D6}");
        AddField("事项", r.Title);
        AddField("渠道", ChannelLabel(r.AccountCategory));
        AddField("申请日期", ShortDate(r.CreatedAt));
        AddField("提单人", NameOrId(_userNameById, r.ApplicantId));
        AddField("申报金额", RectaClient.FormatMoney(r.AppliedAmountCents) + " 元");
        if (r.ApprovedAmountCents is { } a)
        {
            AddField("核准金额", RectaClient.FormatMoney(a) + " 元" +
                       (a < r.AppliedAmountCents ? "(核减)" : ""));
        }
        if (r.SettledAmountCents is { } s)
        {
            AddField("实际出账", RectaClient.FormatMoney(s) + " 元");
        }
        if (!string.IsNullOrEmpty(r.ReviewNotes))
        {
            AddField($"审批人·{NameOrId(_userNameById, r.ReviewerId)}的批复", r.ReviewNotes);
        }
        if (!string.IsNullOrEmpty(r.SettlementNotes))
        {
            AddField($"办结人·{NameOrId(_userNameById, r.SettlerId)}的批复", r.SettlementNotes);
        }
        if (!string.IsNullOrEmpty(r.VoucherUrl))
        {
            AddField("证明材料", r.VoucherUrl);
        }

        if (bundle.Splits.Count > 0)
        {
            AddDivider();
            var perHead = (r.SettledAmountCents ?? r.ApprovedAmountCents ?? r.AppliedAmountCents) /
                          bundle.Splits.Count;
            AddLabel($"班费分摊明细({bundle.Splits.Count} 人,人均 {RectaClient.FormatMoney(perHead)} 元)");
            foreach (var split in bundle.Splits)
            {
                var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 3) };
                line.Children.Add(new TextBlock
                {
                    Text = "• " + (split.StudentName ?? split.StudentId) +
                           (split.IsTailBearer ? "(承担尾差)" : "") +
                           (split.AdvanceCents > 0
                               ? $"  由生委垫付 {RectaClient.FormatMoney(split.AdvanceCents)} 元" : ""),
                    FontSize = 13, TextWrapping = TextWrapping.Wrap,
                });
                var amount = new TextBlock
                {
                    Text = RectaClient.FormatMoney(split.AmountCents), Classes = { "money" },
                    FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(amount, 1);
                line.Children.Add(amount);
                DetailPanel.Children.Add(line);
            }
        }
    }

    private void FillInflowDetail(LedgerRowVm row)
    {
        var inflow = _inflows.FirstOrDefault(i => i.Id == row.InflowId);
        if (inflow is null)
        {
            return;
        }
        DetailPanel.Children.Clear();

        AddHeader(DetailPanel, $"入账 {RectaClient.FormatMoney(inflow.AmountCents)} 元",
                  "RectaPositiveBrush", "已入账");
        AddField("单号", $"INF-{inflow.Id:D6}");
        AddField("来源", inflow.SourceTitle);
        AddField("入账类型", InflowLabel(inflow.DestinationType));
        AddField("日期", ShortDate(inflow.CreatedAt));
        AddField("经办人", NameOrId(_userNameById, inflow.OperatorId));
        if (inflow.TargetStudentId is { } sid)
        {
            AddField("充值对象", NameOrId(_studentNameById, sid));
        }
        if (inflow.RelatedRequestId is { } rid)
        {
            AddField("关联报销单", $"REQ-{rid:D6}");
        }
        if (!string.IsNullOrEmpty(inflow.VoucherFileUrl))
        {
            AddField("证明材料", inflow.VoucherFileUrl);
        }
    }

    private static string NameOrId(Dictionary<string, string> map, string? id) =>
        id is not null && map.TryGetValue(id, out var name) ? name : id ?? "—";

    private static string StatusLabel(string status) => status switch
    {
        "PENDING_REVIEW" => "待审理",
        "APPROVED" => "待办结",
        "SETTLED" => "已办结",
        "REJECTED" => "已驳回",
        _ => status,
    };

    // ---- 详情构建辅助(纯代码布局,字段按需增减) ----

    private void AddField(string label, string value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 3) };
        grid.Children.Add(new TextBlock { Text = label + ":", Classes = { "secondary" }, FontSize = 12 });
        var text = new TextBlock
        {
            Text = value, FontSize = 13, TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        DetailPanel.Children.Add(grid);
    }

    private void AddLabel(string text)
    {
        DetailPanel.Children.Add(new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, FontSize = 13 });
    }

    private void AddDivider()
    {
        DetailPanel.Children.Add(new Border
        {
            Height = 1, Background = (IBrush)this.FindResource("RectaDividerBrush")!, Margin = new Thickness(0, 4),
        });
    }

    private void AddHeader(StackPanel panel, string title, string brushKey, string badge)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(new TextBlock
        {
            Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
        });
        var badgeBorder = new Border
        {
            Padding = new Thickness(10, 3),
            Background = (IBrush)this.FindResource(brushKey)!,
            VerticalAlignment = VerticalAlignment.Center,
        };
        badgeBorder.Child = new TextBlock
        {
            Text = badge, Foreground = Brushes.White, FontWeight = FontWeight.SemiBold, FontSize = 12,
        };
        Grid.SetColumn(badgeBorder, 1);
        grid.Children.Add(badgeBorder);
        panel.Children.Add(grid);
    }
}
