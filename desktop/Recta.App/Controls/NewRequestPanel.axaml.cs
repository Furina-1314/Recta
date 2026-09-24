using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Recta.App.NativeInterop;

namespace Recta.App.Controls;

public sealed class StudentOption : INotifyPropertyChanged
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

public partial class NewRequestPanel : UserControl
{
    private readonly ObservableCollection<StudentOption> _students = [];
    private bool _suppressEvents;

    /// <summary>提交成功后触发(宿主页面可借机刷新列表)。</summary>
    public event EventHandler<int>? RequestSubmitted;

    /// <summary>固定渠道(null 表示由用户选择)。</summary>
    public string? FixedChannel
    {
        get => _fixedChannel;
        set
        {
            _fixedChannel = value;
            if (value is null)
            {
                ChannelBox.IsEnabled = true;
                ChannelBox.SelectedIndex = 0;
            }
            else
            {
                ChannelBox.IsEnabled = false;
                for (var i = 0; i < ChannelBox.ItemCount; i++)
                {
                    if ((ChannelBox.Items[i] as ComboBoxItem)?.Tag as string == value)
                    {
                        ChannelBox.SelectedIndex = i;
                        break;
                    }
                }
            }
        }
    }

    private string? _fixedChannel;

    public NewRequestPanel()
    {
        InitializeComponent();
        StudentChecks.ItemsSource = _students;
        Loaded += async (_, _) => await LoadStudentsAsync();
    }

    private async Task LoadStudentsAsync()
    {
        if (_students.Count > 0 || !AppServices.NativeReady)
        {
            return;
        }
        try
        {
            var students = await Task.Run(() => AppServices.Client.ListStudents());
            foreach (var s in students)
            {
                var option = new StudentOption { StudentId = s.StudentId, Label = $"{s.Name}({s.StudentId})" };
                option.PropertyChanged += (_, _) =>
                {
                    RefreshTailBearers();
                    UpdatePreview();
                };
                _students.Add(option);
            }
        }
        catch (RectaException)
        {
            // 名单加载失败时提交会给出明确错误。
        }
    }

    private string SelectedChannel =>
        (ChannelBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    private void OnChannelChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }
        var isClassFund = SelectedChannel == "CLASS_FUND";
        SplitSection.IsVisible = isClassFund;
        UpdatePreview();
    }

    private void OnToggleCollapse(object? sender, RoutedEventArgs e)
    {
        IsVisible = false;
        Collapsed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Collapsed;

    private void RefreshTailBearers()
    {
        var previous = (TailBearerBox.SelectedItem as StudentOption)?.StudentId;
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
        var bearer = TailBearerBox.SelectedItem as StudentOption;
        if (ids.Count == 0 || bearer is null || !TryParseAmount(out var cents))
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

    private bool TryParseAmount(out long cents)
    {
        cents = 0;
        try
        {
            cents = RectaClient.ParseMoney(AmountBox.Text ?? "");
            return cents > 0;
        }
        catch (RectaException)
        {
            return false;
        }
    }

    private void ShowError(string message)
    {
        FormError.Text = message;
        FormError.IsVisible = true;
    }

    private async void OnSubmit(object? sender, RoutedEventArgs e)
    {
        FormError.IsVisible = false;
        var session = AppServices.Session;
        if (session is null)
        {
            ShowError("演示模式,不可提交。");
            return;
        }

        var channel = SelectedChannel;
        var title = (TitleBox.Text ?? "").Trim();
        if (title.Length == 0)
        {
            ShowError("动账事项必填。");
            return;
        }
        if (!TryParseAmount(out var cents))
        {
            ShowError("申报金额必须为正的定点金额(如 140.00)。");
            return;
        }

        List<string>? participants = null;
        string? bearerId = null;
        if (channel == "CLASS_FUND")
        {
            participants = _students.Where(s => s.IsChecked).Select(s => s.StudentId).ToList();
            bearerId = (TailBearerBox.SelectedItem as StudentOption)?.StudentId;
            if (participants.Count == 0 || bearerId is null)
            {
                ShowError("班费平摊须勾选参摊同学并指定尾差承担人。");
                return;
            }
        }

        try
        {
            var requestId = await Task.Run(() => AppServices.Client.SubmitRequest(
                session.UserId, title, channel, cents, participants, bearerId));
            TitleBox.Text = "";
            AmountBox.Text = "";
            foreach (var option in _students)
            {
                option.IsChecked = false;
            }
            RequestSubmitted?.Invoke(this, requestId);
        }
        catch (RectaException ex)
        {
            ShowError(ex.Message);
        }
    }
}
