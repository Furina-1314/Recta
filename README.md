# Recta 矩衡

**高校班委财务审批与分户账管理系统** —— 高确定性、强一致性、严禁模糊账目。

- **三类异构资金通道**：班级灵活走账公款（团支书存管）· 系级报销往来暂挂（生活委员存管）· 班费纯个人独立分户（受托代管，无公共资金池）
- **两阶段流转**：审批（资质与额度认定，不动账）→ 办结（资金到位确认，原子事务扣账）
- **点对点借贷实质**：透支记负，构成向生活委员个人的无息借贷；办结批复自动披露垫资明细
- **全链路整数分**：`int64_t` 定点 `Money`，全局禁用浮点；尾差保全平摊 + 对账守恒不变式（Σb = C_cash − A_advance）

## 技术栈

| 层 | 技术 |
| --- | --- |
| 前端 | Avalonia UI 11（.NET 10，C#），Win10 UWP / MDL2 视觉：绝对方角、1px 实体细线、高信息密度、等宽表格数字 |
| 核心业务 | Modern C++20（MSVC），`Recta.Domain` → `Recta.Storage` → `Recta.Core` → `Recta.CApi`（C ABI） |
| 数据库 | Neon Serverless PostgreSQL（金额一律 `BIGINT` 分；`FOR UPDATE` 行级锁；抗休眠重试执行器） |
| 同步 | `global_change_seq` 单调递增增量拉取 + Signal Push 轻通知 |

**严禁** WebView / 浏览器内核 / Electron / Tauri / WebView2。

## 仓库导览

- [Vibe.md](Vibe.md) —— 最高技术规范与业务上下文（唯一真理基准）
- [VibeState.md](VibeState.md) —— Phase 规划、进度与工程决策记录
- `native/` —— C++20 核心（领域内核 / 存储 / 服务 / C ABI）
- `desktop/` —— Avalonia 前端（P6 起建立）

## 构建（Windows）

前置：Visual Studio 2022 C++ 工具集 · CMake 3.24+ · vcpkg（`VCPKG_ROOT`）· .NET SDK 10

```bash
# 原生核心
cmake --preset win-x64-release -S native -B native/build/win-x64-release
cmake --build native/build/win-x64-release --config Release
ctest --test-dir native/build/win-x64-release -C Release --output-on-failure
```

## 版本管理

GitHub：<https://github.com/Furina-1314/Recta>，以 Phase 为断点提交，进度见 VibeState.md。
