using Avalonia.Controls;
using Avalonia.Media;
using Recta.App.NativeInterop;

namespace Recta.App.Controls;

public sealed record ChannelRow(
    string IdText, string Title, string AmountText, string StatusLabel, IBrush StatusBrush);

public partial class ChannelRequestList : UserControl
{
    private string? _category;

    /// <summary>渠道过滤(FLEXIBLE / FACULTY / CLASS_FUND);null=全部。</summary>
    public string? Category
    {
        get => _category;
        set
        {
            _category = value;
            if (_loadedOnce)
            {
                _ = ReloadAsync();
            }
        }
    }

    private bool _loadedOnce;

    public ChannelRequestList()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            _loadedOnce = true;
            await ReloadAsync();
        };
    }

    public async Task ReloadAsync()
    {
        if (!AppServices.NativeReady)
        {
            return;
        }
        try
        {
            var requests = await Task.Run(() => AppServices.Client.ListRequests(null, Category, null));
            Rows.ItemsSource = requests.Select(r => new ChannelRow(
                $"#{r.Id:D3}",
                r.Title,
                RectaClient.FormatMoney(r.AppliedAmountCents),
                StatusLabel(r.Status),
                StatusBrush(r.Status))).ToList();
        }
        catch (RectaException)
        {
            Rows.ItemsSource = Array.Empty<ChannelRow>();
        }
    }

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
        return (IBrush)this.FindResource(key)!;
    }
}
