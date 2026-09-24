# Recta（矩衡）开发状态档

本档是 [Vibe.md](Vibe.md) 的执行进度与工程决策记录。规范以 Vibe.md 为唯一真理基准；本档记录"做到哪了、怎么落的"。

- **最后更新**：2026-09-25
- **当前阶段**：**P13 完成——全部 14 个 Phase 收官，v0.1.0 可交付**
- **仓库**：https://github.com/Furina-1314/Recta

---

## 0. 技术选型决策（已定）

| 项 | 决策 | 说明 |
| --- | --- | --- |
| 前端 | **Avalonia UI 11（net10.0，C#）** | Vibe.md 推荐选型；Fluent 基座 + 自绘 RectaTheme 字典，深度对齐经典 UWP XAML 架构；Skia 渲染满足"纯原生 GPU 自绘" |
| 视觉参考 | **Equora（E:\code\Equora）** | 复用其"单一主题色向两侧展开 7 级色阶 + 明暗双主题 + 结构画刷（Canvas/Pane/Surface/Divider/Accent/OnAccent）"的组织方式；主题色改用 Vibe.md 指定的 `#0078D7`，不沿用 Equora 默认红 |
| 核心业务 | **C++20（MSVC 14.44，CMake 3.24+）** | `Recta.Domain`（纯计算内核）→ `Recta.Storage`（pqxx）→ `Recta.Core`（服务层）→ `Recta.CApi`（C ABI DLL），依赖方向禁止循环 |
| 数值 | `int64_t` 定点分，`Money` 强类型 | 全链路禁用浮点；前端只收定点字符串或整数分 |
| 数据库 | **Neon PostgreSQL**（项目 `misty-bar-36297499`，分支 `production`） | 连接串取自 `.env.local`（不入库）；金额一律 `BIGINT` 分 |
| 依赖管理 | vcpkg manifest（`E:\code\vcpkg`） | 自定义三元组 `native/triplets/x64-windows-static-md`（静态库 + 动态 CRT /MD）：pqxx 以 DLL 构建会导出 `zview` 基类 `std::string_view` 的内联成员，与消费方静态库产生 LNK2005，故 libpqxx/libpq/openssl 全静态链接 |
| 版本管理 | GitHub `Furina-1314/Recta`，阶段完成即提交 | gh 已认证（ssh 协议） |

**构建环境（本机已验证）**：dotnet SDK 10.0.401 · CMake 4.4.3 · MSVC 14.44.35207（BuildTools）· vcpkg · git 2.53。

## 1. 视觉设计令牌（P7 落地为 RectaTheme.xaml）

依据 Vibe.md §3 + Equora 的色阶组织法：**强调色以 `#0078D7` 为基准，向深/浅各推 3 级，共 7 级**；明暗主题分别取用（Light 用 Base，Dark 用 Light2 并反转 OnAccent，与 Equora 同法）。

### 强调色 7 级色阶（Recta Blue Ramp）

| 级别 | 色值 | 用途 |
| --- | --- | --- |
| `AccentDark3` | `#00437A` | 深层按压/强调底 |
| `AccentDark2` | `#005598` | 深层悬停 |
| `AccentDark1` | `#0067B7` | Light 主题按压态 |
| `AccentBase` | `#0078D7` | **Light 主题强调色**（Classic UWP Blue） |
| `AccentLight1` | `#1A8BE1` | Light 主题悬停态 |
| `AccentLight2` | `#4DA3E8` | **Dark 主题强调色** |
| `AccentLight3` | `#8FC7F1` | 弱强调/浅底 |

### 结构画刷与语义色（Light / Dark）

| 令牌 | Light | Dark | 对应 Equora 键位 |
| --- | --- | --- | --- |
| `RectaCanvasBrush`（画布底层） | `#F3F3F3` | `#1F1F1F` | EquoraCanvasBrush |
| `RectaSurfaceBrush`（表层容器） | `#FFFFFF` | `#2B2B2B` | EquoraSurfaceBrush |
| `RectaPaneBrush`（导航窗格） | `#F9F9F9` | `#232323` | EquoraPaneBrush |
| `RectaDividerBrush`（1px 实体边框） | `#D2D2D2` | `#3E3E3E` | EquoraDividerBrush |
| `RectaAccentBrush` / `RectaOnAccentBrush` | `#0078D7` / `#FFFFFF` | `#4DA3E8` / `#1F1F1F` | EquoraAccentBrush / OnAccent |
| `RectaPositiveBrush`（结余/收入） | `#107C41` | `#4CC38A` | — |
| `RectaDangerBrush`（透支/垫付/驳回） | `#D13438` | `#FF6B70` | — |
| `RectaPendingBrush`（挂账/待审） | `#797673` | `#A0A0A0` | — |

### 几何与排印铁律

- 全控件 `CornerRadius=0`（含 ComboBox、ListView、Overlay、ToolTip 全系键位）；
- 1px 实体细线分割，禁扩散阴影；
- 金额列：`Cascadia Mono`（回退 `Consolas`）+ 右对齐，保证数位垂直对齐（等宽表格数字的 Avalonia 落地法）；
- 图标：Segoe MDL2 Assets 字形；
- 三栏 Master-Detail：左紧凑导航（Compact 56 / Open 208，仿 Equora 壳）+ 中间高密度表格 + 右侧滑入式 Inspector，不弹全屏模态。

## 2. Phase 全景规划

> 原则（Vibe.md §9）：以 Phase 为工作断点，单 Phase 不贪多；每 Phase 完成即更新本档并提交 GitHub。

| Phase | 内容 | 状态 | 验收标准 |
| --- | --- | --- | --- |
| **P0** | 仓库基线：目录结构、.gitignore、README、清理脚手架残留、GitHub 推送 | ✅ 完成 | 远端可见首提 |
| **P1** | C++ 领域层：`Money`、尾差平摊 `DistributeExpense`、垫资增量 Δadvance、对账守恒 Σb=C−A、RBAC 硬约束、状态/渠道枚举；gtest 单测 | ✅ 完成 | MSVC+CMake 构建，ctest 全绿 |
| **P2** | 数据库 DDL 落地：Vibe.md §6 全部 8 表于 Neon `production` 分支 | ✅ 完成 | MCP describe 确认表结构与列类型 |
| **P3** | 存储层：`NeonContext` 抗休眠重试执行器（§7.2）、.env.local 连接装载、各表仓储（含 `FOR UPDATE` 行锁封装） | ✅ 完成 | C++ 冒烟测试对 Neon 读写往返成功 |
| **P4** | 身份认证：Argon2id 口令哈希、登录、首登强制改密、团支书账号管理（开立/停用/重置临时密码） | ✅ 完成 | 服务层单测 + 真库冒烟（Neon `recta-test` 分支） |
| **P5** | 业务服务层：两阶段流转（审批/核减/驳回/办结原子事务闭环 §8.4）、入账引擎三通道、流水与 change_events 写入、守恒断言入库前强校验 | ✅ 完成 | 集成测试：平摊扣款后 Σb=C−A 恒等 |
| **P6** | C ABI 导出层 `recta_capi.dll` + C# NativeInterop（P/Invoke + DTO） | ✅ 完成 | C# 侧往返调用领域函数成功 |
| **P7** | Avalonia 壳与主题：App/MainWindow/SplitView 导航、RectaTheme.xaml（§1 令牌全量）、登录窗、9 页骨架（大盘/审批/分户/走账/系报/入账/预算/审计/设置） | ✅ 完成 | 程序启动可导航、明暗切换正确、全方角（冒烟截图像素级验证） |
| **P8** | 审批台账页（三栏 Master-Detail）：状态筛选/搜索/高密度表格/Inspector 滑入、审批（全额/核减）/驳回/确认办结扣款、底栏统计 | ✅ 完成 | 对照 §3 ASCII 布局走查（测试库多状态演示数据截图验证） |
| **P9** | 提单与走账页：新提单（渠道选择、班费平摊名单+尾差承担人+预览）、灵活公款走账、系报销挂账 | ✅ 完成 | 提单→审批→办结全链路 GUI 可走 |
| **P10** | 分户与入账页：同学名单管理（团支书）、充值补缴入账、个人流水、垫资披露视图 | ✅ 完成 | 入账三通道 GUI 可走 |
| **P11** | 大盘/审计/预算看板：团支书全员统计（驳回率、核减差、响应时效、渠道分布） | ✅ 完成 | 数据与真库一致 |
| **P12** | 增量同步与连接状态：`global_change_seq` 增量拉取 + Signal Push 轻通知、状态栏（连接正常/重试中）、断线退避重连 | ✅ 完成 | 断网恢复后数据收敛一致（xunit 真库 NOTIFY 收敛测试） |
| **P13** | 打包收尾：发布配置、README 截图与构建说明、版本 tag | ✅ 完成 | 自包含发布产物冒烟通过，v0.1.0 tag |

注：导航含"预算"页，但 Vibe.md 未给预算模块规格——P11 中实现为只读概览（各渠道余额+月度动账走势），不引入写入路径；若后续补充规格再扩展。

## 3. 工程结构（P0 落地）

```
Recta/
├─ Vibe.md              # 最高规范（只读基准）
├─ VibeState.md         # 本档
├─ README.md
├─ .env.local           # Neon 连接串（gitignored，不入库）
└─ native/              # C++20 核心（P1 起）
   ├─ CMakeLists.txt
   ├─ CMakePresets.json
   ├─ vcpkg.json
   ├─ triplets/x64-windows-static-md.cmake   # 静态库+动态 CRT 三元组
├─ Recta.Domain/         # 纯计算内核：Money/平摊/垫资/守恒/RBAC
├─ Recta.Domain.Tests/   # gtest
├─ Recta.Storage/        # pqxx 存储：NeonContext/EnvConfig/六组仓储
├─ Recta.Storage.Tests/  # 回滚式真库集成冒烟（production 分支）
├─ Recta.Core/           # 服务层：PasswordHasher/AuthService/WorkflowService/RosterService
└─ Recta.Core.Tests/     # 单测 + recta-test 分支集成（认证 4 + 工作流 7）
（desktop/：Recta.App〔Avalonia 壳、登录窗、大盘/设置功能页、七页骨架、Recta 主题〕/ Recta.App.NativeInterop / Recta.App.NativeInterop.Tests / Recta.slnx；冒烟截图输出至 desktop/.smoke，已 gitignore）
```

## 4. 变更日志

- **2026-09-24 · P0** 清理 Neon 脚手架残留（hello.ts / neon.ts / package*.json / node_modules）；建立目录结构与 .gitignore；初始化 git 并推送 GitHub。
- **2026-09-24 · P1** `Recta.Domain` 落地：`Money`（溢出检查、禁乘除、定点 parse/format）、`DistributeExpense` 尾差平摊（空名单/重复学号/承担人缺席防御）、`ComputeAdvanceDelta` 三段垫资判定、`VerifyConservation` 守恒校验、`AssertCanReview/AssertCanSettle` 两阶段 RBAC 硬约束、全量枚举字符串映射（与 DDL 取值一致）；gtest **25 用例全绿**（MSVC x64 Release，ctest 通过）。注：MSVC 对 requires 探测已删除函数报硬错误，金额禁乘除由 delete 直接保证，不写成 static_assert。
- **2026-09-24 · P2** Neon `production` 分支执行 §6 全套 DDL：8 表创建成功（users / accounts / student_personal_accounts / expense_requests / expense_splits / inflow_records / account_ledger_entries / change_events），逐表核验列名与类型（金额列均为 `BIGINT`）。DDL 同步落盘 `db/schema/001_init.sql` 供复现。账号种子数据（users / 两实体账户）延至 P4 身份认证阶段一并处理。
- **2026-09-24 · P12** 增量同步与连接状态落地，双轨制（§2）：**拉取轨**——原生同步工作线程按 `change_events.seq` 全局单调游标增量拉取（start 时对齐 MAX(seq) 仅上报新事件，队列封顶 500，~15s 兜底拉）；**通知轨**——`LedgerRepo::AppendChangeEvent` 统一随事务 `NOTIFY recta_changes`（提交原子生效，监听端只见已提交变更），监听连接 `await_notification(1s)` 唤醒即拉。**关键工程决策**：Neon `-pooler`（PgBouncer 事务模式）不支持 LISTEN/NOTIFY——监听连接自动派生非池化端点（优先 `RECTA_DB_UNPOOLED_URL`，否则剥主机名 `-pooler` 后缀），拉取仍走池化。断线退避 1s×n 封顶 15s，重连后立即补拉保证收敛；4 个导出（start/stop/status/drain）。C# 侧 `SyncController`（UI 线程 DispatcherTimer 3s drain+status，事件转发）+ MainWindow 状态灯三态（绿=实时监听/橙=建连或重连中(含次数)/红=未运行，连接态统一由同步呈现，守恒异常压过连接色）+ `IRefreshable` 九页接入（他端变更到达即自动刷新当前页与统计）。**验收**：新增 xunit `NotifyReachesDrainAndCursorAdvances` 真库测试——池化连接写入同学 → NOTIFY → 非池化监听唤醒 → drain 收到 `student` 事件且游标单调推进，一次通过（20s 内）；冒烟截图确认状态灯绿。
- **2026-09-24 · 体验迭代（卡死根治/过渡动画/连接门控/文案 Release 化/折叠修复）** ① **卡死根修**：CAPI 全局互斥曾持锁贯穿整个数据库事务——任一慢查询期间，UI 线程上的纯计算 P/Invoke（金额格式化/平摊预演）排队等锁造成界面冻结与悬停迟钝；改为服务快照（`shared_ptr<Services>`，持锁仅取指针，事务在锁外并发，init 换出旧实例凭引用存活至事务结束），纯函数导出走独立无服务骨架。真库 6 项测试回归全绿。② **连接门控 `ConnectionGate`**：全部事务/查询统一 `RunAsync`（离线立即报错、15s 超时、忙态灯"正在处理…"）；在线状态由同步状态持续校正（重连退避/拉取失败→离线，成功→在线）；`Friendly` 按错误码翻译为面向用户的表述（权限/状态竞态/超时/离线），参数类文案去除条款号并翻译枚举词。③ **UWP 式页面过渡**：切页右移 32px 滑入+淡入（220ms CubicEaseOut，`PageAnimation`）。④ **菜单折叠修复**：紧凑模式隐藏品牌字标/logo/用户信息与导航文字标签（`ListBox.compact` 类选择器），图标居中，杜绝半截汉字。⑤ **文案 Release 化**：去除全部 §条款号与"恒等式/守恒/Σb"术语（→"资金对账/账目平衡/结余合计"），子标题改面向用户表述；分户/入账/审计/预算页补刷新按钮；冒烟新增折叠态截图（`RECTA_SMOKE_COMPACT=1`）验证标签零残留。
- **2026-09-25 · 缺陷修复：用户页列表为空** 症状为"创建用户后刷新，用户列表为空且无报错"。根因与账目页同款——`ToRow` 中控制级 `this.FindResource` 在该时机返回 `UnsetValueType`，转 `IBrush` 抛 InvalidCastException；而各处 `LoadAsync`/刷新均以 `_ = LoadAsync()` 丢弃 Task，非 RectaException 变成未观察异常被静默吞掉，于是列表不更新、界面无任何提示。修复：UsersPage/LedgerPage 详情/SettingsPage 残留的控制级 FindResource 全部清除（统一 Application 级 `ThemeBrush` + 灰兜底，全仓已无该模式）；UsersPage 四个操作 handler 与 LoadAsync 增加通用异常兜底（界面呈现"操作异常/加载异常"）。冒烟复验用户页列表/状态色/操作按钮渲染正常。
- **2026-09-25 · 信息架构重构（七项）** ① **单号唯一标识全链路**：支出 `REQ-NNNNNN`/入账 `INF-NNNNNN` 前缀编号贯通台账/详情/关联下拉；`expense_requests` 增 `voucher_url` 证明材料列（`db/schema/002` 增补迁移，production 与 recta-test 均已应用），提单链路（服务层/CAPI/C#）贯通。② **统一提单弹窗** `NewRequestDialog`：支出/入账双 Tab——支出选渠道(班费带参摊名单+尾差承担人+实时预演)、金额、证明链接；入账选三通道(同学充值带对象下拉、系核销带已办结单关联下拉)、来源、证明链接。走账/系报/审批页的内嵌提单与增资/核销表单全部撤除，入口仅审批页与账目页的[新提单]按钮。③ **账目页**(新)合并走账/系报/入账/预算四页：余额四卡+类型/渠道/搜索高级筛选+统一流水列表(红支出/绿入账+日期倒序)+右侧详情面板——支出详情含单号/日期/申报核准出账三额/审批人与批复/办结人与批复(垫资披露)/证明链接/班费分摊明细(逐人+尾差+垫付标注)；入账详情含单号/来源/类型/经办人/充值对象或关联单/证明链接。④ **分户页精简**：保留对账三卡、名单管理、姓名+余额两列表与个人流水。⑤ **权限化导航**：账目/分户=团支书+生活委员(生活委员账目仅班费类流水)，审计/用户=团支书专属——导航项按会话角色过滤，页面内数据同权限。⑥ **用户管理页**(新,团支书)：开立账号(用户名/姓名/角色→一次性临时口令+复制)、逐行重置密码/改名/停用启用(`ActivateUser` 新增含席位唯一校验)。⑦ 英文变量名清扫收尾。修复：账目页行定义错误(筛选行占 \* 压扁列表)、控制级 FindResource 偶发返回 UnsetValue 导致列表空(统一改 Application 级 ThemeBrush+兜底)。删除 FlexiblePage/FacultyPage/InflowPage/BudgetPage/NewRequestPanel/ChannelRequestList。测试 6 项真库回归全绿；双角色冒烟截图验证(生活委员账目仅班费流水且无审计/用户导航)。
- **2026-09-24 · 品牌迭代（v0.1.0 后）** ① **字标排印修复**：用户反馈"RECTA 间距过宽、显示不全"——根因是 Avalonia `LetterSpacing` 单位为 DIU，而参考的 WinUI `CharacterSpacing` 为千分 em，P7 直接照搬 140 相当于 140px/字距；对齐 EQUORA 排印（18px、≈0.12em）改为 `FontSize=18, LetterSpacing=2`（登录窗 26/3），像素验证字标结束于 150 逻辑像素（窗格 208 内完整显示）。② **RECTA logo**：与 Equora.svg 同构（256×256 实底方 + 白色几何字块，同一字母网格 x56–200/y48–208、笔画 32）——蓝底 `#0078D7` + 几何 "R"（左竖笔/上横/右竖碗/中横/斜腿平行四边形），产物 PNG×4 尺寸 + 多尺寸 ICO + SVG 源（`tools/make_logo.ps1` 可再生成，注：PS1 须纯 ASCII，中文注释无 BOM 会被 PowerShell 5.1 按 ANSI 误读导致解析错乱）。③ **接入**：exe 图标（`ApplicationIcon`）、登录窗与主窗 `WindowIcon`（`Branding.ApplyIcon`，`AssetLoader.Open`——Avalonia 11.3 已移除 `AvaloniaLocator.Current`）、窗格头 20px logo + 字标、登录窗 40px logo；README 四截图与发布产物同步刷新。
- **2026-09-24 · P13（收官）** 交付闭环：全量回归（C++ 3 套 + C# 6 项全绿）→ 补齐**首次引导 UI**（登录窗检测 users 空表自动切换"系统未初始化"面板，调 `BootstrapFirstSecretary` 一次性开立首任团支书并展示临时口令——空系统可进入的关键缺口；`RECTA_SMOKE_LOGIN=1` 冒烟截屏验证）→ **发布构建**（`RECTA_DEV_TOOLS=OFF` 重建 recta_capi.dll 去除开发工具导出 + `dotnet publish -r win-x64 --self-contained` 自包含产物 `artifacts/Recta-0.1.0-win-x64/`，产物对生产配置冒烟通过）→ README 重写（四截图、构建/测试/运行/发布全流程、首次引导说明、冒烟自检）→ 清理发布告警（死字段/空引用）→ tag **v0.1.0**。**项目总结**：14 Phase 全部完成——C++20 强类型定点内核（Money/平摊/垫资/守恒/RBAC）、pqxx 存储与行级锁、Argon2id 认证、两阶段原子流转、C ABI + P/Invoke 互操作、Avalonia UWP 风格九页 GUI（明暗双主题七级蓝阶）、审计统计、双轨增量同步；测试合计 46 项 C++ / 6 项 C# 全绿，生产分支干净、演示与测试数据隔离于 recta-test 分支。
- **2026-09-24 · P11** 审计/预算看板落地，九个导航页全部实体化。原生新增 `recta_get_audit_statistics`（**服务端硬校验团支书角色**；单事务四段聚合 SQL：全局指标（提单量/驳回率/申报vs核准/审批·办结平均响应分钟）、逐人 LEFT JOIN 指标（含 `EXTRACT(EPOCH FROM (reviewed_at-created_at))` 时效）、渠道分布、驳回成因 Top5——时长与百分比为展示浮点，金额仍整数分）与 `recta_get_budget_overview`（近月已办结出账按渠道 + 入账合计，`date_trunc` 月度聚合）。**审计页**：五指标卡（驳回率红/核减差额）+ 常见驳回成因 + 逐人十列指标表（渠道分布缩写"灵活2 系报1"）；非团支书显示专属提示卡（§5.1 可见性分级）。**预算页**：三通道余额卡 + 近六月走势表（出账三渠道/合计/入账合计绿），纯只读。至此 §5.4 全部指标 GUI 化，双页截图走查通过。
- **2026-09-24 · P10** 分户与入账页落地。原生层新增两条查询链路：`LedgerRepo::ListStudentLedger/ListInflows` → CAPI `recta_list_student_ledger/recta_list_inflows` → C# `ListStudentLedger/ListInflows` + DTO。**分户页**：守恒三元组四卡片（Σb/C_cash/A_advance/恒等徽示）、同学名单硬线表（余额负数红色）、**名单管理**（团支书录入/选中改名）、右侧选中详情 + **个人不可变流水**（类型语义着色：充值绿/分摊红，变动额与余额后值右对齐）+ **垫资披露**面板（当前透支同学逐人欠款与合计）；**入账页**：充值补缴表单（生活委员，目标同学下拉定向 `TO_STUDENT_SUB_ACCOUNT`）+ 全渠道入账台账（时间/来源/金额/去向/定向目标(姓名或关联单号)/经办人姓名联查）。双角色截图走查通过；Storage 7 项 + C# 全栈往返回归全绿。九个导航页中仅剩 预算/审计 为骨架（P11）。
- **2026-09-24 · P9** 提单与走账页落地。共享 `NewRequestPanel`（渠道/事项/金额；班费时展开**参摊同学复选名单 + 尾差承担人下拉 + 领域 `DistributeExpense` 实时预演**——"N 人参摊、人均 x.xx、尾差承担人扣 y.yy、分项合计恒等"——P/Invoke 纯领域调用零延迟；`FixedChannel` 支持渠道页固定、提交成功 `RequestSubmitted` 事件驱动宿主刷新）；`ChannelRequestList`（渠道过滤紧凑单据列表，复用语义着色）。**走账页**（灵活公款）：余额卡 + 通道规则卡 + 增资表单（仅团支书可见，`TO_FLEXIBLE_ACCOUNT`）+ 固定渠道提单 + 渠道单据；**系报页**（系报销）：挂账(应收)卡 + **核销表单**（仅生活委员，金额/来源/关联单下拉只列已办结系报销单，`TO_FACULTY_REIMBURSE`）+ 固定渠道提单 + 渠道单据。审批页头部新增 [＋新提单]（折叠面板，自由选渠道）与刷新按钮。冒烟以团支书/生活委员双角色分别截图走账/系报页通过。**附带修复**：用户反馈"选中栏目蓝框包绿"——根因 FluentTheme 采纳 Windows 系统强调色（用户偏绿）渗入 Fluent 内部 Accent 资源：(1) 自绘 ListBoxItem 模板的 ContentPresenter 改名 `ContentHost`，避开 Fluent `:selected/:focus` 按 `PART_ContentPresenter` 名匹配的样式；(2) `SystemAccentColor` 七成员在 XAML 资源与 `App.Initialize` 代码双路钉死为 Recta 蓝阶，截图扫描选中行 60 行蓝 0 行绿验证。
- **2026-09-24 · P8** 审批台账页（`RequestsPage`）落地，§3 ASCII 图主界面成形：台账主视图（状态/渠道双下拉筛选 + 事项/编号搜索 + 1px 硬线高密度表格——编号/事项/渠道/金额右对齐等宽/状态语义着色，复用自绘方角 ListBox 模板作行选中）+ 右侧 Inspector（REQ-YYYY-NNN 编号、事项、提单人（id→姓名联查）、申报/核准/出账三金额解耦展示、审批意见与办结批复全文、班费平摊名单（人均/尾差标记）、**按角色与状态门控的操作区**：待审理→核准金额+核减理由+驳回/核准（客户端预校验核减必填理由，服务端仍硬校验）、待办结→确认办结扣款、办结后垫资披露面板）+ 页内底栏统计（共 N 项 | 待审理 X | 待办结 Y）。冒烟框架增强：`RECTA_SMOKE_PAGE` 直达页、`RECTA_SMOKE_TESTDB` 连测试分支、`RECTA_SMOKE_ROLE/USER/UID` 演示会话；在 recta-test 补多状态演示单据（待审理/待办结/已驳回/班费带平摊）后截图走查通过，暗色主题选中行像素核验 #4DA3E8。
- **2026-09-24 · P7** `desktop/Recta.App`（Avalonia 11.3，net10.0）落地。**主题双文件制**（Avalonia 学习成本结论）：`RectaResources.axaml`（ResourceDictionary：明暗 ThemeDictionaries 十四画刷 + 7 级蓝色阶 Color + 等宽/图标字体键，经 `ResourceInclude` 合入）与 `RectaTheme.axaml`（`<Styles>` 根：全控件 `CornerRadius=0` 铁律、1px 实线输入面、`accent/danger` 按钮、自绘方角 `ListBoxItem` 模板、`card/page-title/money/money-value/stat-value/secondary/danger-text/icon` 类选择器体系，经 `StyleInclude` 合入）——Avalonia 无控件级 Style 属性与 Style.BasedOn，一律 Classes。壳：MainWindow（SplitView CompactInline 56/208、MDL2 图标导航九项、底部状态栏连接灯+守恒态+单量统计、用户席位卡）；LoginWindow（登录→首登强制改密面板→入主窗）；大盘页实时四通道卡片+守恒恒等式徽章+状态分布；设置页明暗切换+本人改密+版本；其余七页 SkeletonPage 骨架（标注落地 Phase）。**验收方式**：`RECTA_SMOKE=1` 冒烟模式跳过登录直入主窗，渲染明/暗两张 PNG（RenderTargetBitmap）后自动退出——像素级核验：选中导航实心 `#0078D7`、暗色主题 `#4DA3E8` 强调、画布 `#F3F3F3/#1F1F1F`、卡片边线 `#D2D2D2/#3E3E3E` 全部与令牌表一致；卡片数值随查询完成显示 0.00（生产库当前为空）。视觉走查修正：Fluent 半透明选中层不可穿透→改自绘模板；统计瓦片改左对齐；冒烟等待 3.2s 留 Neon 冷启动余量。
- **2026-09-24 · P6** `Recta.CApi`（`recta_capi.dll`，6.2MB 全静态链接）与 `desktop/Recta.App.NativeInterop` 落地：C ABI 约定——成功返回写入字符数、失败返回负错误码（十档：参数/权限/认证/状态机/弱口令/数据库/逻辑/未就绪/缓冲/未知），异常绝不越界（统一 `DispatchError` 分派 + 线程局部 `recta_last_error`），金额一律 int64 分、复杂结构一律 nlohmann JSON。导出面覆盖：生命周期（init/init_test/shutdown/version）、领域纯函数（money format/parse、distribute 预览）、认证与账号管理全套、工作流全套（提单/审批/驳回/办结/入账/名单）、查询全套（台账/单据+分摊联查姓名/同学/实体账户/用户/总览守恒/变更事件增量）。C# 侧 `LibraryImport`（UTF-8 + byte[] 缓冲）+ `RectaClient` 高层封装（负码→`RectaException` 带原生错误消息）+ 全量 DTO。**决策**：`recta_dev_truncate_all` 仅在 `RECTA_DEV_TOOLS=ON` 编译（默认开发构建开，发布打包必须关），供跨语言测试复位 recta-test 分支。测试：xunit 5 项全绿——4 项领域往返 + 1 项全栈（C# 引导团支书→改密→开生活委员→录名单→预存→班费提单→核减 99.99→办结→守恒 Σb=C−A=−69.99→总览/台账/详情/事件流校验），叠加既有 C++ 40 项全绿。
- **2026-09-24 · P5** `WorkflowService`/`RosterService` 落地，业务大脑成形：提单（班费必带平摊名单+尾差承担人，领域 `DistributeExpense` 当场算分摊入库）；审批两分支（全额/核减——核减理由必填、班费按核准额 `ReplaceSplits` 整单重算；驳回理由必填归档终止）；办结单事务原子闭环（权限硬前置 → 行级锁主单 → 状态机门槛 → 按渠道锁账户/锁全部分户(升序防死锁) → 扣账+Δadvance 回填 → 分摊合计==核准额防脏数据 → **守恒恒等式强校验(失败即回滚)** → 组装含垫资明细的办结批复 → SETTLED → change_events）；入账三通道（灵活增资=团支书、系核销=生活委员且必关联已办结系报销单+核销不得超挂账、同学补缴=生活委员定向平负，充值后同样守恒校验）；名单管理仅团支书。**渠道账务语义决策**：FACULTY 账户余额=挂账应收（办结+、核销−、余额不足拒核销）。权限先于状态机检查（未授权者无论单据状态一律 PermissionDeniedException，§8.3）。测试 +7（灵活全流程+权限矩阵、余额不足拒付、班费平摊垫资+守恒、核减重算、系报销挂账核销闭环、提单/审批校验、名单权限+补缴平账）全绿；全项目 47 用例绿。
- **2026-09-24 · P4** `Recta.Core` 落地：`PasswordHasher`（libsodium Argon2id，`crypto_pwhash_str` 交互级参数，哈希串 <128 字符合 VARCHAR(255)；临时口令 CSPRNG + 57 字符无歧义字母表 + 拒绝采样消模偏）、`AuthService`（登录统一失败语义不泄露失败原因、首登强制改密闭环、改密含弱口令策略 8~64 位、团支书专属开号/重置/停用/改名且仅可改 display_name、团支书与生活委员活动席位唯一强校验、`BootstrapFirstSecretary` 一次性引导首任团支书并绑定灵活公款存管人、生活委员开立即绑定系报销账户存管人）。基础设施：Neon 新增 `recta-test` 分支（从 production 复制 schema）作集成测试沙箱，`TryLoadTestConnectionString` 读 `RECTA_TEST_DATABASE_URL` / `.env.test.local`（均 gitignored）。测试：Domain 26 + Storage 7 + Core 7（哈希单测 3 + 认证集成 4）全绿；pqxx 改为 Storage 的 PUBLIC 依赖（仓储签名暴露 pqxx::work&）。
- **2026-09-24 · P3** `Recta.Storage` 落地：`NeonContext`（§7.2 带退避重试，每次尝试全新短连接）、`LoadConnectionString`（DATABASE_URL > RECTA_ENV_FILE > 自 cwd 向上寻 `.env.local`，解析引号/CRLF）、六组仓储（Users / StudentAccounts / EntityAccounts / Request / Ledger）——全部方法接收调用方 `pqxx::work&`，自身不提交，供 P5 组装原子事务；`LockMany`/`Lock`/`LockByType` 封装 `FOR UPDATE` 且按 id 升序确定性加锁。集成冒烟 7 用例对真 Neon 全绿（连接/提交路径、用户全生命周期、分户出入账+守恒三元组+乱序入参的有序锁定、审批单两阶段状态机+分摊明细、入账/流水/change_events、实体账户锁与调额），写路径一律 `tx.abort()` 回滚，库中零残留。工程决策：引入自定义三元组 `x64-windows-static-md` 静态链接 pqxx（规避其 DLL 导出 `std::string_view` 内联成员的 LNK2005）；pqxx 新版 API 全面采用 `tx.exec(sql, pqxx::params{...})` 与模板化行映射。
