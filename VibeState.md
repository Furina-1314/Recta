# Recta（矩衡）开发状态档

本档是 [Vibe.md](Vibe.md) 的执行进度与工程决策记录。规范以 Vibe.md 为唯一真理基准；本档记录"做到哪了、怎么落的"。

- **最后更新**：2026-09-24
- **当前阶段**：P1 完成 / P2 完成，下一步 P3（存储层与 Neon 抗休眠执行器）
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
| 依赖管理 | vcpkg manifest（`E:\code\vcpkg`） | 已验证端口：`gtest`、`libpq`、`libpqxx` |
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
| **P3** | 存储层：`NeonContext` 抗休眠重试执行器（§7.2）、.env.local 连接装载、各表仓储（含 `FOR UPDATE` 行锁封装） | ⬜ 未开始 | C++ 冒烟测试对 Neon 读写往返成功 |
| **P4** | 身份认证：Argon2id 口令哈希、登录、首登强制改密、团支书账号管理（开立/停用/重置临时密码） | ⬜ 未开始 | 服务层单测 + 真库冒烟 |
| **P5** | 业务服务层：两阶段流转（审批/核减/驳回/办结原子事务闭环 §8.4）、入账引擎三通道、流水与 change_events 写入、守恒断言入库前强校验 | ⬜ 未开始 | 集成测试：平摊扣款后 Σb=C−A 恒等 |
| **P6** | C ABI 导出层 `recta_capi.dll` + C# NativeInterop（P/Invoke + DTO） | ⬜ 未开始 | C# 侧往返调用领域函数成功 |
| **P7** | Avalonia 壳与主题：App/MainWindow/SplitView 导航、RectaTheme.xaml（§1 令牌全量）、登录窗、9 页骨架（大盘/审批/分户/走账/系报/入账/预算/审计/设置） | ⬜ 未开始 | 程序启动可导航、明暗切换正确、全方角 |
| **P8** | 审批台账页（三栏 Master-Detail）：状态筛选/搜索/高密度表格/Inspector 滑入、审批（全额/核减）/驳回/确认办结扣款、底栏统计 | ⬜ 未开始 | 对照 §3 ASCII 布局走查 |
| **P9** | 提单与走账页：新提单（渠道选择、班费平摊名单+尾差承担人+预览）、灵活公款走账、系报销挂账 | ⬜ 未开始 | 提单→审批→办结全链路 GUI 可走 |
| **P10** | 分户与入账页：同学名单管理（团支书）、充值补缴入账、个人流水、垫资披露视图 | ⬜ 未开始 | 入账三通道 GUI 可走 |
| **P11** | 大盘/审计/预算看板：团支书全员统计（驳回率、核减差、响应时效、渠道分布） | ⬜ 未开始 | 数据与真库一致 |
| **P12** | 增量同步与连接状态：`global_change_seq` 增量拉取 + Signal Push 轻通知、状态栏（连接正常/重试中）、断线退避重连 | ⬜ 未开始 | 断网恢复后数据收敛一致 |
| **P13** | 打包收尾：发布配置、README 截图与构建说明、版本 tag | ⬜ 未开始 | 可交付 |

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
   ├─ Recta.Domain/         # 纯计算内核：Money/平摊/垫资/守恒/RBAC
   └─ Recta.Domain.Tests/   # gtest
（desktop/ 自 P6/P7 起建立：Recta.App / Recta.App.NativeInterop / Recta.App.ViewModels / Recta.slnx）
```

## 4. 变更日志

- **2026-09-24 · P0** 清理 Neon 脚手架残留（hello.ts / neon.ts / package*.json / node_modules）；建立目录结构与 .gitignore；初始化 git 并推送 GitHub。
- **2026-09-24 · P1** `Recta.Domain` 落地：`Money`（溢出检查、禁乘法）、`DistributeExpense` 尾差平摊、`ComputeAdvanceDelta` 三段垫资判定、`VerifyConservation` 守恒校验、`PermissionGuard` 两阶段 RBAC 硬约束、全量枚举；gtest 27 用例全绿（MSVC x64 Release）。
- **2026-09-24 · P2** Neon `production` 分支执行 §6 全套 DDL：8 表创建成功（users / accounts / student_personal_accounts / expense_requests / expense_splits / inflow_records / account_ledger_entries / change_events），字段与 Vibe.md 一致。
