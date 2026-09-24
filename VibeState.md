# Recta（矩衡）开发状态档

本档是 [Vibe.md](Vibe.md) 的执行进度与工程决策记录。规范以 Vibe.md 为唯一真理基准；本档记录"做到哪了、怎么落的"。

- **最后更新**：2026-09-24
- **当前阶段**：P9 完成，下一步 P10（分户与入账页）
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
- **2026-09-24 · P9** 提单与走账页落地。共享 `NewRequestPanel`（渠道/事项/金额；班费时展开**参摊同学复选名单 + 尾差承担人下拉 + 领域 `DistributeExpense` 实时预演**——"N 人参摊、人均 x.xx、尾差承担人扣 y.yy、分项合计恒等"——P/Invoke 纯领域调用零延迟；`FixedChannel` 支持渠道页固定、提交成功 `RequestSubmitted` 事件驱动宿主刷新）；`ChannelRequestList`（渠道过滤紧凑单据列表，复用语义着色）。**走账页**（灵活公款）：余额卡 + 通道规则卡 + 增资表单（仅团支书可见，`TO_FLEXIBLE_ACCOUNT`）+ 固定渠道提单 + 渠道单据；**系报页**（系报销）：挂账(应收)卡 + **核销表单**（仅生活委员，金额/来源/关联单下拉只列已办结系报销单，`TO_FACULTY_REIMBURSE`）+ 固定渠道提单 + 渠道单据。审批页头部新增 [＋新提单]（折叠面板，自由选渠道）与刷新按钮。冒烟以团支书/生活委员双角色分别截图走账/系报页通过。**附带修复**：用户反馈"选中栏目蓝框包绿"——根因 FluentTheme 采纳 Windows 系统强调色（用户偏绿）渗入 Fluent 内部 Accent 资源：(1) 自绘 ListBoxItem 模板的 ContentPresenter 改名 `ContentHost`，避开 Fluent `:selected/:focus` 按 `PART_ContentPresenter` 名匹配的样式；(2) `SystemAccentColor` 七成员在 XAML 资源与 `App.Initialize` 代码双路钉死为 Recta 蓝阶，截图扫描选中行 60 行蓝 0 行绿验证。
- **2026-09-24 · P8** 审批台账页（`RequestsPage`）落地，§3 ASCII 图主界面成形：台账主视图（状态/渠道双下拉筛选 + 事项/编号搜索 + 1px 硬线高密度表格——编号/事项/渠道/金额右对齐等宽/状态语义着色，复用自绘方角 ListBox 模板作行选中）+ 右侧 Inspector（REQ-YYYY-NNN 编号、事项、提单人（id→姓名联查）、申报/核准/出账三金额解耦展示、审批意见与办结批复全文、班费平摊名单（人均/尾差标记）、**按角色与状态门控的操作区**：待审理→核准金额+核减理由+驳回/核准（客户端预校验核减必填理由，服务端仍硬校验）、待办结→确认办结扣款、办结后垫资披露面板）+ 页内底栏统计（共 N 项 | 待审理 X | 待办结 Y）。冒烟框架增强：`RECTA_SMOKE_PAGE` 直达页、`RECTA_SMOKE_TESTDB` 连测试分支、`RECTA_SMOKE_ROLE/USER/UID` 演示会话；在 recta-test 补多状态演示单据（待审理/待办结/已驳回/班费带平摊）后截图走查通过，暗色主题选中行像素核验 #4DA3E8。
- **2026-09-24 · P7** `desktop/Recta.App`（Avalonia 11.3，net10.0）落地。**主题双文件制**（Avalonia 学习成本结论）：`RectaResources.axaml`（ResourceDictionary：明暗 ThemeDictionaries 十四画刷 + 7 级蓝色阶 Color + 等宽/图标字体键，经 `ResourceInclude` 合入）与 `RectaTheme.axaml`（`<Styles>` 根：全控件 `CornerRadius=0` 铁律、1px 实线输入面、`accent/danger` 按钮、自绘方角 `ListBoxItem` 模板、`card/page-title/money/money-value/stat-value/secondary/danger-text/icon` 类选择器体系，经 `StyleInclude` 合入）——Avalonia 无控件级 Style 属性与 Style.BasedOn，一律 Classes。壳：MainWindow（SplitView CompactInline 56/208、MDL2 图标导航九项、底部状态栏连接灯+守恒态+单量统计、用户席位卡）；LoginWindow（登录→首登强制改密面板→入主窗）；大盘页实时四通道卡片+守恒恒等式徽章+状态分布；设置页明暗切换+本人改密+版本；其余七页 SkeletonPage 骨架（标注落地 Phase）。**验收方式**：`RECTA_SMOKE=1` 冒烟模式跳过登录直入主窗，渲染明/暗两张 PNG（RenderTargetBitmap）后自动退出——像素级核验：选中导航实心 `#0078D7`、暗色主题 `#4DA3E8` 强调、画布 `#F3F3F3/#1F1F1F`、卡片边线 `#D2D2D2/#3E3E3E` 全部与令牌表一致；卡片数值随查询完成显示 0.00（生产库当前为空）。视觉走查修正：Fluent 半透明选中层不可穿透→改自绘模板；统计瓦片改左对齐；冒烟等待 3.2s 留 Neon 冷启动余量。
- **2026-09-24 · P6** `Recta.CApi`（`recta_capi.dll`，6.2MB 全静态链接）与 `desktop/Recta.App.NativeInterop` 落地：C ABI 约定——成功返回写入字符数、失败返回负错误码（十档：参数/权限/认证/状态机/弱口令/数据库/逻辑/未就绪/缓冲/未知），异常绝不越界（统一 `DispatchError` 分派 + 线程局部 `recta_last_error`），金额一律 int64 分、复杂结构一律 nlohmann JSON。导出面覆盖：生命周期（init/init_test/shutdown/version）、领域纯函数（money format/parse、distribute 预览）、认证与账号管理全套、工作流全套（提单/审批/驳回/办结/入账/名单）、查询全套（台账/单据+分摊联查姓名/同学/实体账户/用户/总览守恒/变更事件增量）。C# 侧 `LibraryImport`（UTF-8 + byte[] 缓冲）+ `RectaClient` 高层封装（负码→`RectaException` 带原生错误消息）+ 全量 DTO。**决策**：`recta_dev_truncate_all` 仅在 `RECTA_DEV_TOOLS=ON` 编译（默认开发构建开，发布打包必须关），供跨语言测试复位 recta-test 分支。测试：xunit 5 项全绿——4 项领域往返 + 1 项全栈（C# 引导团支书→改密→开生活委员→录名单→预存→班费提单→核减 99.99→办结→守恒 Σb=C−A=−69.99→总览/台账/详情/事件流校验），叠加既有 C++ 40 项全绿。
- **2026-09-24 · P5** `WorkflowService`/`RosterService` 落地，业务大脑成形：提单（班费必带平摊名单+尾差承担人，领域 `DistributeExpense` 当场算分摊入库）；审批两分支（全额/核减——核减理由必填、班费按核准额 `ReplaceSplits` 整单重算；驳回理由必填归档终止）；办结单事务原子闭环（权限硬前置 → 行级锁主单 → 状态机门槛 → 按渠道锁账户/锁全部分户(升序防死锁) → 扣账+Δadvance 回填 → 分摊合计==核准额防脏数据 → **守恒恒等式强校验(失败即回滚)** → 组装含垫资明细的办结批复 → SETTLED → change_events）；入账三通道（灵活增资=团支书、系核销=生活委员且必关联已办结系报销单+核销不得超挂账、同学补缴=生活委员定向平负，充值后同样守恒校验）；名单管理仅团支书。**渠道账务语义决策**：FACULTY 账户余额=挂账应收（办结+、核销−、余额不足拒核销）。权限先于状态机检查（未授权者无论单据状态一律 PermissionDeniedException，§8.3）。测试 +7（灵活全流程+权限矩阵、余额不足拒付、班费平摊垫资+守恒、核减重算、系报销挂账核销闭环、提单/审批校验、名单权限+补缴平账）全绿；全项目 47 用例绿。
- **2026-09-24 · P4** `Recta.Core` 落地：`PasswordHasher`（libsodium Argon2id，`crypto_pwhash_str` 交互级参数，哈希串 <128 字符合 VARCHAR(255)；临时口令 CSPRNG + 57 字符无歧义字母表 + 拒绝采样消模偏）、`AuthService`（登录统一失败语义不泄露失败原因、首登强制改密闭环、改密含弱口令策略 8~64 位、团支书专属开号/重置/停用/改名且仅可改 display_name、团支书与生活委员活动席位唯一强校验、`BootstrapFirstSecretary` 一次性引导首任团支书并绑定灵活公款存管人、生活委员开立即绑定系报销账户存管人）。基础设施：Neon 新增 `recta-test` 分支（从 production 复制 schema）作集成测试沙箱，`TryLoadTestConnectionString` 读 `RECTA_TEST_DATABASE_URL` / `.env.test.local`（均 gitignored）。测试：Domain 26 + Storage 7 + Core 7（哈希单测 3 + 认证集成 4）全绿；pqxx 改为 Storage 的 PUBLIC 依赖（仓储签名暴露 pqxx::work&）。
- **2026-09-24 · P3** `Recta.Storage` 落地：`NeonContext`（§7.2 带退避重试，每次尝试全新短连接）、`LoadConnectionString`（DATABASE_URL > RECTA_ENV_FILE > 自 cwd 向上寻 `.env.local`，解析引号/CRLF）、六组仓储（Users / StudentAccounts / EntityAccounts / Request / Ledger）——全部方法接收调用方 `pqxx::work&`，自身不提交，供 P5 组装原子事务；`LockMany`/`Lock`/`LockByType` 封装 `FOR UPDATE` 且按 id 升序确定性加锁。集成冒烟 7 用例对真 Neon 全绿（连接/提交路径、用户全生命周期、分户出入账+守恒三元组+乱序入参的有序锁定、审批单两阶段状态机+分摊明细、入账/流水/change_events、实体账户锁与调额），写路径一律 `tx.abort()` 回滚，库中零残留。工程决策：引入自定义三元组 `x64-windows-static-md` 静态链接 pqxx（规避其 DLL 导出 `std::string_view` 内联成员的 LNK2005）；pqxx 新版 API 全面采用 `tx.exec(sql, pqxx::params{...})` 与模板化行映射。
