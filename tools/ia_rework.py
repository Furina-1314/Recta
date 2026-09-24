# -*- coding: utf-8 -*-
import os, re
base = r'E:\code\Recta\desktop\Recta.App'

# ---- MainWindow.axaml.cs ----
p = base + r'\MainWindow.axaml.cs'
s = open(p, encoding='utf-8').read()

old_nav = '''    private static readonly (string Tag, string Label, string Glyph)[] NavSpec =
    [
        ("overview",  "大盘", "\\uE80F"),
        ("requests",  "审批", "\\uE8A5"),
        ("students",  "分户", "\\uE716"),
        ("flexible",  "走账", "\\uE8C7"),
        ("faculty",   "系报", "\\uE8F1"),
        ("inflow",    "入账", "\\uE896"),
        ("budget",    "预算", "\\uE8EF"),
        ("audit",     "审计", "\\uE81C"),
        ("settings",  "设置", "\\uE713"),
    ];'''
new_nav = '''    private static readonly (string Tag, string Label, string Glyph)[] NavSpec =
    [
        ("overview",  "大盘", "\\uE80F"),
        ("requests",  "审批", "\\uE8A5"),
        ("ledger",    "账目", "\\uE8C7"),
        ("students",  "分户", "\\uE716"),
        ("audit",     "审计", "\\uE81C"),
        ("users",     "用户", "\\uE77B"),
        ("settings",  "设置", "\\uE713"),
    ];

    // 按角色可见:账目/分户=团支书+生活委员;审计/用户=团支书;其余全员。
    private static readonly Dictionary<string, string[]> PageRoles = new()
    {
        ["ledger"] = ["BRANCH_SECRETARY", "LIFE_COMMITTEE"],
        ["students"] = ["BRANCH_SECRETARY", "LIFE_COMMITTEE"],
        ["audit"] = ["BRANCH_SECRETARY"],
        ["users"] = ["BRANCH_SECRETARY"],
    };'''
assert old_nav in s, 'nav spec not found'
s = s.replace(old_nav, new_nav)

s = s.replace('''    private readonly ObservableCollection<NavItem> _navItems = new(NavSpec.Select(n => new NavItem(n.Tag, n.Label, n.Glyph)));''',
'''    private readonly ObservableCollection<NavItem> _navItems;''')

s = s.replace('''        InitializeComponent();
        Branding.ApplyIcon(this);
        Nav.ItemsSource = _navItems;

        var session = AppServices.Session;''',
'''        InitializeComponent();
        Branding.ApplyIcon(this);

        var session = AppServices.Session;''')

s = s.replace('''        UserRoleText.Text = session is null ? "SMOKE" : RoleLabel(session.Role);''',
'''        UserRoleText.Text = session is null ? "SMOKE" : RoleLabel(session.Role);
        var role = session?.Role ?? "";
        _navItems = new ObservableCollection<NavItem>(
            NavSpec.Where(n => !PageRoles.TryGetValue(n.Tag, out var roles) || roles.Contains(role))
                   .Select(n => new NavItem(n.Tag, n.Label, n.Glyph)));
        Nav.ItemsSource = _navItems;''')

s = s.replace('''        "requests" => new RequestsPage(),
        "flexible" => new FlexiblePage(),
        "faculty" => new FacultyPage(),
        "students" => new StudentsPage(),
        "inflow" => new InflowPage(),
        "budget" => new BudgetPage(),
        "audit" => new AuditPage(),''',
'''        "requests" => new RequestsPage(),
        "ledger" => new LedgerPage(),
        "students" => new StudentsPage(),
        "audit" => new AuditPage(),
        "users" => new UsersPage(),''')
open(p, 'w', encoding='utf-8').write(s)
print('mainwindow ok')

# ---- RequestsPage.axaml ----
p = base + r'\Pages\RequestsPage.axaml'
s = open(p, encoding='utf-8').read()
s = re.sub(r'[ \t]*<!-- 新提单\(可折叠\) -->\n[ \t]*<Grid Grid.Row="1" x:Name="NewRequestHost".*?\n[ \t]*</Grid>\n\n',
           '', s, flags=re.S)
assert 'NewRequestHost' not in s, 'host still present'
s = s.replace('<TextBlock Text="编号" Margin="14,8,8,8" />', '<TextBlock Text="单号" Margin="14,8,8,8" />')
s = s.replace('<Grid ColumnDefinitions="76,*,112,132,92"', '<Grid ColumnDefinitions="118,*,96,132,86"')
s = s.replace('<Grid ColumnDefinitions="70,*,112,132,92" MinHeight="44">', '<Grid ColumnDefinitions="112,*,96,132,86" MinHeight="44">')
open(p, 'w', encoding='utf-8').write(s)
print('requests axaml ok')

# ---- RequestsPage.axaml.cs ----
p = base + r'\Pages\RequestsPage.axaml.cs'
s = open(p, encoding='utf-8').read()
s = s.replace('''        InitializeComponent();
        SubmitPanel.RequestSubmitted += (_, _) => _ = LoadAsync();
        SubmitPanel.Collapsed += (_, _) => NewRequestHost.IsVisible = false;
        Loaded += (_, _) => _ = LoadAsync();''',
'''        InitializeComponent();
        Loaded += (_, _) => _ = LoadAsync();''')
s = s.replace('''    private void OnToggleNewRequest(object? sender, RoutedEventArgs e)
    {
        NewRequestHost.IsVisible = !NewRequestHost.IsVisible;
    }''',
'''    private async void OnToggleNewRequest(object? sender, RoutedEventArgs e)
    {
        var dialog = new Windows.NewRequestDialog();
        await dialog.ShowDialog(TopLevel.GetTopLevel(this) as Window
                                ?? throw new InvalidOperationException());
        if (dialog.Submitted)
        {
            await LoadAsync();
        }
    }''')
s = s.replace('$"#{r.Id:D3}"', '$"REQ-{r.Id:D6}"')
s = s.replace('var year = r.CreatedAt is { Length: >= 4 } created ? created[..4] : "2026";',
              'var _ = 0;')
s = s.replace('ReqNoText.Text = $"申请编号 REQ-{year}-{r.Id:D3}";', 'ReqNoText.Text = $"单号 REQ-{r.Id:D6}";')
open(p, 'w', encoding='utf-8').write(s)
print('requests cs ok')

# ---- StudentsPage 两列化 ----
p = base + r'\Pages\StudentsPage.axaml'
s = open(p, encoding='utf-8').read()
s = s.replace('<Grid ColumnDefinitions="120,*,140,140,140">', '<Grid ColumnDefinitions="140,*,180">')
s = s.replace('''                        <Grid ColumnDefinitions="120,*,140,140,140">''', '''<Grid ColumnDefinitions="140,*,180">''')
s = s.replace('''                            <TextBlock Grid.Column="2" Text="余额(元)" Classes="money" Margin="8,8,14,8" />
                            <TextBlock Grid.Column="3" Text="累计充值(元)" Classes="money" Margin="8,8,8,8" />
                            <TextBlock Grid.Column="4" Text="累计支出(元)" Classes="money" Margin="8,8,8,8" />''',
'''                            <TextBlock Grid.Column="2" Text="余额(元)" Classes="money" Margin="8,8,14,8" />''')
s = s.replace('<Grid ColumnDefinitions="114,*,140,140,140" MinHeight="42">', '<Grid ColumnDefinitions="134,*,180" MinHeight="42">')
s = s.replace('''                                        <TextBlock Grid.Column="2" Text="{Binding BalanceText}" Classes="money"
                                                   Foreground="{Binding BalanceBrush}" VerticalAlignment="Center"
                                                   Margin="2,0,8,0" />
                                        <TextBlock Grid.Column="3" Text="{Binding RechargedText}" Classes="money"
                                                   VerticalAlignment="Center" Margin="2,0,8,0" />
                                        <TextBlock Grid.Column="4" Text="{Binding SpentText}" Classes="money"
                                                   VerticalAlignment="Center" Margin="2,0,8,0" />''',
'''                                        <TextBlock Grid.Column="2" Text="{Binding BalanceText}" Classes="money"
                                                   Foreground="{Binding BalanceBrush}" VerticalAlignment="Center"
                                                   Margin="2,0,14,0" />''')
open(p, 'w', encoding='utf-8').write(s)

p = base + r'\Pages\StudentsPage.axaml.cs'
s = open(p, encoding='utf-8').read()
s = s.replace('''    string StudentId, string Name, long BalanceCents, string BalanceText, IBrush BalanceBrush,
    string RechargedText, string SpentText);''',
'''    string StudentId, string Name, long BalanceCents, string BalanceText, IBrush BalanceBrush);''')
s = s.replace('''                : (IBrush)this.FindResource("RectaTextPrimaryBrush")!,
                RectaClient.FormatMoney(s.TotalRechargedCents),
                RectaClient.FormatMoney(s.TotalSpentCents))).ToList();''',
'''                : (IBrush)this.FindResource("RectaTextPrimaryBrush")!)).ToList();''')
open(p, 'w', encoding='utf-8').write(s)
print('students ok')

# ---- 删除被合并页面与控件 ----
for f in ['Pages/FlexiblePage.axaml', 'Pages/FlexiblePage.axaml.cs',
          'Pages/FacultyPage.axaml', 'Pages/FacultyPage.axaml.cs',
          'Pages/InflowPage.axaml', 'Pages/InflowPage.axaml.cs',
          'Pages/BudgetPage.axaml', 'Pages/BudgetPage.axaml.cs',
          'Controls/ChannelRequestList.axaml', 'Controls/ChannelRequestList.axaml.cs',
          'Controls/NewRequestPanel.axaml', 'Controls/NewRequestPanel.axaml.cs']:
    fp = os.path.join(base, f)
    if os.path.exists(fp):
        os.remove(fp)
        print('deleted', f)
print('ALL DONE')
