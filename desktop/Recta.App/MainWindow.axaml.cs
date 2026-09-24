using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Recta.App.NativeInterop;
using Recta.App.Pages;

namespace Recta.App;

internal sealed record NavItem(string Tag, string Label, string Glyph);

public partial class MainWindow : Window
{
    private static readonly (string Tag, string Label, string Glyph)[] NavSpec =
    [
        ("overview",  "大盘", "\uE80F"),
        ("requests",  "审批", "\uE8A5"),
        ("students",  "分户", "\uE716"),
        ("flexible",  "走账", "\uE8C7"),
        ("faculty",   "系报", "\uE8F1"),
        ("inflow",    "入账", "\uE896"),
        ("budget",    "预算", "\uE8EF"),
        ("audit",     "审计", "\uE81C"),
        ("settings",  "设置", "\uE713"),
    ];

    private readonly Dictionary<string, UserControl> _pages = new();
    private readonly ObservableCollection<NavItem> _navItems = new(NavSpec.Select(n => new NavItem(n.Tag, n.Label, n.Glyph)));

    public MainWindow()
    {
        InitializeComponent();
        Branding.ApplyIcon(this);
        Nav.ItemsSource = _navItems;

        var session = AppServices.Session;
        UserNameText.Text = session?.DisplayName ?? "演示模式";
        UserRoleText.Text = session is null ? "SMOKE" : RoleLabel(session.Role);

        // 冒烟模式支持直达指定页(RECTA_SMOKE_PAGE=requests 等)。
        var smokePage = AppServices.SmokePage;
        var initialIndex = 0;
        if (smokePage is not null)
        {
            for (var i = 0; i < _navItems.Count; i++)
            {
                if (_navItems[i].Tag == smokePage)
                {
                    initialIndex = i;
                    break;
                }
            }
        }
        Nav.SelectedIndex = initialIndex;

        // 增量同步:监听他端变更,自动刷新当前页与状态栏。
        SyncController.StatusChanged += OnSyncStatus;
        SyncController.EventsReceived += OnSyncEvents;
        ConnectionGate.BusyChanged += OnBusyChanged;
        Closed += (_, _) =>
        {
            SyncController.Stop();
            ConnectionGate.BusyChanged -= OnBusyChanged;
        };
        SyncController.Start();

        if (AppServices.SmokeMode)
        {
            // 冒烟模式:渲染截图(明/暗各一张)后自动退出,供构建管线与视觉验收。
            Loaded += async (_, _) => await CaptureSmokeScreensAsync();
        }
    }

    private void OnSyncStatus(object? sender, SyncStatusDto status)
    {
        if (status.Listening)
        {
            DbDot.Fill = Brushes.ForestGreen;
            DbText.Text = "连接正常 · 实时监听";
        }
        else if (status.Running)
        {
            DbDot.Fill = Brushes.Orange;
            DbText.Text = status.ReconnectAttempts == 0
                ? "正在建立实时监听…"
                : $"重连中(第 {status.ReconnectAttempts} 次退避)";
        }
        else
        {
            DbDot.Fill = Brushes.Firebrick;
            DbText.Text = "同步未运行";
        }
    }

    private async void OnSyncEvents(object? sender, IReadOnlyList<ChangeEventDto> events)
    {
        StatusLeft.Text = $"收到 {events.Count} 条服务端变更,已刷新视图";
        if (PageHost.Content is IRefreshable refreshable)
        {
            await refreshable.RefreshAsync();
        }
        await RefreshDbStatusAsync();
    }

    private async Task CaptureSmokeScreensAsync()
    {
        try
        {
            await Task.Delay(5000); // 等首帧渲染、大盘数据返回与同步监听建立(Neon 冷启动余量)

            var outDir = Environment.GetEnvironmentVariable("RECTA_SMOKE_OUT")
                         ?? Path.Combine(AppContext.BaseDirectory, "smoke");
            Directory.CreateDirectory(outDir);

            await CaptureAsync(Path.Combine(outDir, "smoke-light.png"));

            // 折叠态验证(RECTA_SMOKE_COMPACT=1):收起窗格后截第三张,检查标签隐藏与图标居中。
            if (Environment.GetEnvironmentVariable("RECTA_SMOKE_COMPACT") == "1")
            {
                Shell.IsPaneOpen = false;
                ApplyPaneMode();
                await Task.Delay(400);
                await CaptureAsync(Path.Combine(outDir, "smoke-compact.png"));
            }

            if (Avalonia.Application.Current is not null)
            {
                Avalonia.Application.Current.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
                await Task.Delay(500); // 等主题资源切换生效
                await CaptureAsync(Path.Combine(outDir, "smoke-dark.png"));
            }
        }
        catch
        {
            // 冒烟截图失败不阻断退出。
        }
        finally
        {
            Close();
        }
    }

    private async Task CaptureAsync(string path)
    {
        var scale = VisualRoot is Avalonia.Rendering.IRenderRoot root ? root.RenderScaling : 1.0;
        var size = new Avalonia.PixelSize((int)(Bounds.Width * scale), (int)(Bounds.Height * scale));
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size,
            new Avalonia.Vector(96 * scale, 96 * scale));
        bitmap.Render(this);
        bitmap.Save(path);
        await Task.Delay(50);
    }

    private static string RoleLabel(string role) => role switch
    {
        "BRANCH_SECRETARY" => "团支书",
        "LIFE_COMMITTEE" => "生活委员",
        "CLASS_COMMITTEE" => "职能班委",
        _ => role,
    };

    private void OnTogglePane(object? sender, RoutedEventArgs e)
    {
        Shell.IsPaneOpen = !Shell.IsPaneOpen;
        ApplyPaneMode();
    }

    // 紧凑模式:收起时隐藏品牌文字与用户信息,导航仅留图标并居中。
    private void ApplyPaneMode()
    {
        var open = Shell.IsPaneOpen;
        BrandLogo.IsVisible = open;
        BrandText.IsVisible = open;
        UserNameText.IsVisible = open;
        UserRoleText.IsVisible = open;
        Nav.Classes.Set("compact", !open);
    }

    private void OnBusyChanged(object? sender, EventArgs e)
    {
        BusyText.IsVisible = ConnectionGate.IsBusy;
    }

    private void OnNavChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not NavItem item)
        {
            return;
        }

        if (!_pages.TryGetValue(item.Tag, out var page))
        {
            page = CreatePage(item.Tag);
            _pages[item.Tag] = page;
        }
        PageHost.Content = page;
        PageAnimation.SlideIn(page); // UWP 式滑入过渡
        StatusLeft.Text = $"{item.Label}";
        _ = RefreshDbStatusAsync();
    }

    private UserControl CreatePage(string tag) => tag switch
    {
        "overview" => new OverviewPage(),
        "requests" => new RequestsPage(),
        "flexible" => new FlexiblePage(),
        "faculty" => new FacultyPage(),
        "students" => new StudentsPage(),
        "inflow" => new InflowPage(),
        "budget" => new BudgetPage(),
        "audit" => new AuditPage(),
        "settings" => new SettingsPage(),
        _ => new SkeletonPage(tag, "", "\uE7BA"),
    };

    private async Task RefreshDbStatusAsync()
    {
        if (!AppServices.NativeReady)
        {
            DbDot.Fill = Brushes.Firebrick;
            DbText.Text = "服务未启动";
            return;
        }

        // 连接状态由同步监听(OnSyncStatus)统一呈现;此处仅刷新左下统计。
        try
        {
            var overview = await ConnectionGate.RunAsync(() => AppServices.Client.GetOverview());
            if (overview.Custody.Conserved && DbText.Text is { } dbText && dbText.StartsWith("连接正常"))
            {
                DbText.Text = "连接正常 · 实时同步 · 账目平衡";
            }
            else if (!overview.Custody.Conserved)
            {
                DbDot.Fill = Brushes.OrangeRed; // 账目异常优先呈现
                DbText.Text = "连接正常 · 账目异常，请联系管理员";
            }
            StatusLeft.Text = $"共 {overview.StatusCounts.Values.Sum()} 笔单据 · 待审理 {CountOf(overview, "PENDING_REVIEW")} · 待办结 {CountOf(overview, "APPROVED")}";
        }
        catch (RectaException)
        {
            // 查询失败交由同步状态线程呈现,不在此覆盖连接灯。
        }
    }

    private static long CountOf(OverviewDto overview, string status) =>
        overview.StatusCounts.TryGetValue(status, out var count) ? count : 0;
}
