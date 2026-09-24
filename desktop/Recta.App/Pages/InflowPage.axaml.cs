using Avalonia.Controls;
using Avalonia.Interactivity;
using Recta.App.NativeInterop;

namespace Recta.App.Pages;

public sealed record InflowRowVm(
    string Time, string Source, string AmountText, string DestinationLabel, string Target,
    string OperatorName);

public partial class InflowPage : UserControl, IRefreshable
{
    private Dictionary<string, string> _userNameById = new();
    private Dictionary<string, string> _studentNameById = new();

    public InflowPage()
    {
        InitializeComponent();
        Loaded += (_, _) => _ = LoadAsync();
    }

    private async void OnRefresh(object? sender, RoutedEventArgs e) => await LoadAsync();

    public Task RefreshAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        RechargeCard.IsVisible = AppServices.Session?.Role == "LIFE_COMMITTEE";
        RechargeError.Text = "";

        if (!AppServices.NativeReady)
        {
            return;
        }

        try
        {
            var usersTask = ConnectionGate.RunAsync(() => AppServices.Client.ListUsers());
            var studentsTask = ConnectionGate.RunAsync(() => AppServices.Client.ListStudents());
            var inflowsTask = ConnectionGate.RunAsync(() => AppServices.Client.ListInflows());
            var users = await usersTask;
            var students = await studentsTask;
            var inflows = await inflowsTask;

            _userNameById = users.ToDictionary(u => u.Id, u => u.DisplayName);
            _studentNameById = students.ToDictionary(s => s.StudentId, s => s.Name);

            if (RechargeCard.IsVisible)
            {
                StudentBox.ItemsSource = students
                    .Select(s => new StudentOptionVm(s.StudentId, $"{s.Name}({s.StudentId})"))
                    .ToList();
                if (StudentBox.ItemCount > 0)
                {
                    StudentBox.SelectedIndex = 0;
                }
            }

            Rows.ItemsSource = inflows.Select(i => new InflowRowVm(
                i.CreatedAt is { Length: >= 16 } t ? t[..16].Replace('T', ' ').Replace("Z", "") : "—",
                i.SourceTitle,
                RectaClient.FormatMoney(i.AmountCents),
                DestinationLabel(i.DestinationType),
                i.TargetStudentId is { } sid
                    ? _studentNameById.TryGetValue(sid, out var n) ? n : sid
                    : (i.RelatedRequestId is { } rid ? $"关联单 #{rid:D3}" : "—"),
                _userNameById.TryGetValue(i.OperatorId, out var op) ? op : i.OperatorId)).ToList();
        }
        catch (RectaException)
        {
            Rows.ItemsSource = Array.Empty<InflowRowVm>();
        }
    }

    private sealed record StudentOptionVm(string StudentId, string Label);

    private static string DestinationLabel(string destination) => destination switch
    {
        "TO_FLEXIBLE_ACCOUNT" => "灵活账户增资",
        "TO_FACULTY_REIMBURSE" => "系报销核销",
        "TO_STUDENT_SUB_ACCOUNT" => "同学分户充值",
        _ => destination,
    };

    private async void OnRecharge(object? sender, RoutedEventArgs e)
    {
        RechargeError.Text = "";
        var session = AppServices.Session;
        if (session is null)
        {
            return;
        }
        if (StudentBox.SelectedItem is not StudentOptionVm target)
        {
            RechargeError.Text = "请选择目标同学。";
            return;
        }

        long cents;
        try
        {
            cents = RectaClient.ParseMoney(AmountBox.Text ?? "");
        }
        catch (RectaException ex)
        {
            RechargeError.Text = $"金额格式错误:{ConnectionGate.Friendly(ex)}";
            return;
        }
        var source = (SourceBox.Text ?? "").Trim();
        if (source.Length == 0)
        {
            RechargeError.Text = "请填写来源。";
            return;
        }

        try
        {
            await ConnectionGate.RunAsync(() => AppServices.Client.RecordInflow(
                session.UserId, "TO_STUDENT_SUB_ACCOUNT", cents, source, target.StudentId));
            AmountBox.Text = "";
            SourceBox.Text = "";
            await LoadAsync();
        }
        catch (RectaException ex)
        {
            RechargeError.Text = ConnectionGate.Friendly(ex);
        }
    }
}
