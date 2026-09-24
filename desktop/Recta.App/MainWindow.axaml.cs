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

        if (AppServices.SmokeMode)
        {
            // 冒烟模式:渲染截图(明/暗各一张)后自动退出,供构建管线与视觉验收。
            Loaded += async (_, _) => await CaptureSmokeScreensAsync();
        }
    }

    private async Task CaptureSmokeScreensAsync()
    {
        try
        {
            await Task.Delay(3200); // 等首帧渲染与大盘数据返回(Neon 冷启动余量)

            var outDir = Environment.GetEnvironmentVariable("RECTA_SMOKE_OUT")
                         ?? Path.Combine(AppContext.BaseDirectory, "smoke");
            Directory.CreateDirectory(outDir);

            await CaptureAsync(Path.Combine(outDir, "smoke-light.png"));

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
            DbText.Text = "原生核心未初始化";
            return;
        }

        try
        {
            var overview = await Task.Run(() => AppServices.Client.GetOverview());
            DbDot.Fill = overview.Custody.Conserved
                ? Brushes.ForestGreen
                : Brushes.OrangeRed; // 守恒被破坏属最高级异常
            DbText.Text = overview.Custody.Conserved ? "数据库连接正常 · 对账守恒" : "数据库连接正常 · 守恒异常!";
            StatusLeft.Text = $"单据 {overview.StatusCounts.Values.Sum()} 项 · 待审理 {CountOf(overview, "PENDING_REVIEW")} · 待办结 {CountOf(overview, "APPROVED")}";
        }
        catch (RectaException)
        {
            DbDot.Fill = Brushes.Firebrick;
            DbText.Text = "数据库连接失败";
        }
    }

    private static long CountOf(OverviewDto overview, string status) =>
        overview.StatusCounts.TryGetValue(status, out var count) ? count : 0;
}
