# Project Blueprint & Vibe Coding Prompt: Recta (矩衡) 班委财务审批与分户账管理系统

你是一名精通现代 C++ 与高性能桌面客户端架构的系统架构师。你需要以此文档作为最高技术规范与上下文约束，以“Vibe Coding”的沉浸式结对编程模式，协同开发高校班委专属的强一致性财务审批与分户账管理系统——**Recta（矩衡）**。

---

## 1. 业务世界观与系统核心定位

Recta 是一款面向高校班级治理的**高确定性、强一致性、严禁模糊账目**的财务审批与代管系统。系统彻底解耦通用财务逻辑，专为班级三类异构资金通道、独立分户与受托垫资模型而设计：

```
                              ┌── 1. 班级灵活走账公款 (灵活账户)
                              │      • 存管/审批/办结：团支书 (唯一)
                              │      • 资金属性：班级共有自主资金，用于小额应急与内部补贴
                              │
全班资金核算系统 ────┼── 2. 系级报销往来暂挂 (外部应收款)
(Recta 矩衡)                 │      • 存管/审批/办结：生活委员 (唯一)
                              │      • 资金属性：班委垫付，挂账待系财务打款核销
                              │
                              └── 3. 班费纯个人独立分户 (无公共资金池，受托代管)
                                     • 审批：团支书 或 生活委员；办结：生活委员 (唯一)
                                     • 资金属性：每人拥有独立虚拟子账户，绝对禁止交叉垫资
                                     • 透支规则：允许余额 < 0，实质由生活委员自掏私房钱垫付

```

### 核心会计原则

1. **班费绝对隔离与无公款原则**：系统不存在“班费总资产池”。同学 $A$ 预存的资金仅由生活委员代管，**严禁在系统逻辑上用于垫付同学 $B$ 的开销**。
2. **点对点借贷实质**：同学透支记为负数，在法律与账务实质上构成**该同学向生活委员个人的无息借贷**。办结时系统自动在批复中披露垫资明细；后续同学补缴，直接充入该同学账户平账，解除对应债权。
3. **两阶段流转（审批与办结分离）**：
* **审批（Approval）**：资质认定与额度审核，不发生任何账面资金变动。若无法全额出账，可选择“直接驳回”或“核减金额审批”。
* **办结（Settlement）**：资金物理到位确认。触发数据库底层事务，扣除账户余额、生成不可变明细流水、追加垫资说明并锁定单据。


4. **全渠道入账追踪**：建立资金流入台账，清晰记录每一笔款项的来源、金额、操作人及确切去向（灵活公款增资 / 系经费回款平账 / 同学个人还款补缴）。

---

## 2. 技术栈架构与工程约束

全系统采用纯原生架构，**绝对禁止使用 WebView、浏览器内核或任何 Web DOM 技术（严禁使用 Electron、Tauri 或 WebView2）**。

* **客户端前端（Windows 首选，兼顾跨平台）**：
* **选型推荐**：**Avalonia UI (.NET 8/9 C#)** 或 **Qt 6 (C++ / QML/Widgets)**。若采用 Avalonia UI，代码结构与样式字典深度对齐经典 UWP XAML 架构。


* **渲染引擎**：纯原生 GPU 自绘管线（DirectX / SkiaSharp / OpenGL），内存开销控制在 50MB 级，冷启动毫秒级。


* **服务端 / 核心业务逻辑层**：
* **选型**：**Modern C++ (C++20)**。
* **数值体系**：基于 `int64_t` 定点数（以“分”为最小物理单位）自主封装强类型 `Money` 领域类，**全局彻底禁用 IEEE-754 二进制浮点数（`float`, `double`）**。
* **运算保障**：强制执行欧几里得除法（商与余数分解）并指定尾差归宿；采用银行家舍入法（四舍六入五成双）；事务层植入数学不变式断言。


* **持久化数据库**：
* **选型**：**Neon Serverless PostgreSQL**。
* **存储规约**：金额统一映射为 `BIGINT`（分）；依赖行级排他锁（`FOR UPDATE`）保障审批与扣款并发强一致性。
* **Serverless 适配**：针对 Neon 的休眠挂起机制（Scale-to-Zero），C++ 端必须实现带退避的连接池重试机制（Retry Loop with Backoff），使用带 `-pooler` 的端点，并强制开启 SSL（`sslmode=require`）。


* **同步与网络通信**：
* 基于原生 HTTPS / WebSocket 长连接。
* 采用“全局单调递增变更序列（`global_change_seq`）增量拉取 + 事件广播轻量通知（Signal Push）”双轨模式，规避断线并发竞态。



---

## 3. 外观设计与视觉规范 (Windows 10 UWP / MDL2 风格)

全面继承 Windows 10 UWP 的经典视觉语言：**内容即界面（Content over Chrome）、绝对方角、高信息密度、硬朗细线网格**。

```
+-----------------------------------------------------------------------------------------+
| [≡] Recta 矩衡                          [Ctrl+K 快速记账]      [当前学期: 2026-秋] [_][□][X] |
+--------+---------------------------------------------------+----------------------------+
| [大盘] | 动账审批台账 (Master View)                         | 动账审查与办结 (Inspector)  |
| [审批] | +-----------------------------------------------+ | 申请编号: #REQ-2026-042    |
| [分户] | | 状态: [全部 v] [待审理] [待办结]   [ 搜索...  ] | | 事项: 自动化实验耗材采购    |
| [走账] | +----+----------+------------+--------+---------+ | 提单人: 科技委员           |
| [系报] | |ID  | 动账事项 | 出资渠道   | 金额   | 状态    | | 申请出资: 班费按人平摊     |
| [入账] | +----+----------+------------+--------+---------+ | 申报金额: 140.00 元        |
| [预算] | |042 | 实验耗材 | 班费平摊   | 140.00 | 待办结  | | 核准金额: 140.00 元        |
| [审计] | |041 | 团日文印 | 灵活公款   |  24.00 | 已办结  | |--------------------------|
|        | |040 | 实践车费 | 系里报销   | 450.00 | 系审核  | | 平摊名单 (7人, 人均20元)  |
| [设置] | +----+----------+------------+--------+---------+ | • 张三: -20.00 (生委垫付) |
|        |                                                   | • 李四:  15.00 (结余充足)  |
|        | (底栏: 42 项记录 | 待办 1 项 | 数据库连接正常)     | [ 驳回 ]   [ 确认办结扣款 ] |
+--------+---------------------------------------------------+----------------------------+

```

* **几何形变**：全系统所有容器、输入框、下拉框、磁贴、按钮、数据网格强制设置 `CornerRadius="0"`（或 QSS `border-radius: 0px`）。


* **阴影与线条**：彻底剔除漫反射模糊阴影，所有区域依靠 **1px 实体细线边框（Solid Border）** 与明度差进行硬分割。


* **财务数字排印**：金额强制使用等宽表格数字（Tabular Figures: `font-feature-settings: "tnum" 1`），所有数字列一律**右对齐**，保证数位与小数点严格垂直对齐。
* **语义色盘（严肃低饱和灰阶体系）**：


* 画布底层：`#F3F3F3`（Light）/ `#1F1F1F`（Dark）；


* 表层容器：`#FFFFFF`（Light）/ `#2B2B2B`（Dark）；


* 实体边框：`#D2D2D2`（Light）/ `#3E3E3E`（Dark）；


* 强调色（Accent）：`#0078D7`（Classic UWP Blue）；
* 正常结余 / 收入：`#107C41`（Dark Green）；
* 透支 / 垫付 / 驳回：`#D13438`（Crimson Red）；
* 挂账 / 待审状态：`#797673`（Neutral Gray）。


* **三栏布局（Master-Detail）**：左侧紧凑导航栏 + 中间高密度表格 + 右侧平滑滑入式 Inspector 抽屉，绝不弹出覆盖全屏的重度模态窗。



---

## 4. 核心数学模型与算法不变式

### 4.1 全局对账守恒定律

系统内定义三类核心度量：

1. **全班同学虚拟账户余额代数和**：$\sum_{i=1}^N b_i$（其中 $b_i > 0$ 为预存结余，$b_i < 0$ 为透支欠款）。
2. **生活委员代管在手实存零钱**：$C_{\text{cash}} = \sum_{b_i > 0} b_i$（受托代管现金，恒 $\ge 0$）。
3. **生活委员个人未收回垫资总额**：$A_{\text{advance}} = \sum_{b_i < 0} \vert{}b_i\vert{}$（生委私人债权，恒 $\ge 0$）。

在任何动账、充值发生后，数据库必须满足恒等式断言：


$$\sum_{i=1}^N b_i = C_{\text{cash}} - A_{\text{advance}}$$

### 4.2 动账垫资判定算法

办结扣除同学 $i$ 的金额 $w > 0$ 时，设扣除前余额为 $b_{\text{old}}$，扣除后余额为 $b_{\text{new}} = b_{\text{old}} - w$。该同学由生活委员新增的垫资 $\Delta \text{advance}_i$ 严格计算如下：


$$\Delta \text{advance}_i = \begin{cases}  0 & \text{若 } b_{\text{new}} \ge 0 \quad (\text{扣减原有存款，未动用生委垫资}) \\  \vert{}b_{\text{new}}\vert{} & \text{若 } b_{\text{old}} \ge 0 \text{ 且 } b_{\text{new}} < 0 \quad (\text{存款扣尽，新产生透支}) \\  w & \text{若 } b_{\text{old}} < 0 \quad (\text{原本已透支，本次扣款全额由生委垫资})  \end{cases}$$

### 4.3 尾差保全平摊算法

一笔经核准出账的金额 $M$ 分平摊给 $k$ 位同学，强制指定同学 $X$ 承担尾差：


$$M = q \cdot k + r \quad (0 \le r < k)$$

* 尾差承担人 $X$ 应扣金额：$q + r$；
* 其余 $k-1$ 人每人应扣金额：$q$；
* 数学不变式强校验：$(q + r) + (k - 1) \cdot q \equiv M$。

---

## 5. 权限角色体系与业务流转规格

### 5.1 角色与权限划分 (RBAC)

| 角色 (`Role`) | 账号管理权限 | 密码控制 | 提单权限 | 审批权限 | 办结出账权限 | 全员审计看板 |
| --- | --- | --- | --- | --- | --- | --- |
| **团支书** (`BRANCH_SECRETARY`) | **最高管理**：开立账号、分配角色、指定固定用户名 | 可修改本人；可重置他人临时密码 | 允许 | 灵活走账（唯一）、班费分摊 | 灵活走账（唯一） | **专属完全访问** |
| **生活委员** (`LIFE_COMMITTEE`) | 无 | 仅修改本人 | 允许 | 系报销（唯一）、班费分摊 | 系报销（唯一）、班费分摊（唯一） | 仅班费与代管看板 |
| **各职能班委** (`CLASS_COMMITTEE`) | 无 | 仅修改本人（初次登录强制改密） | 允许 | 无 | 无 | 仅本人历史提单 |

### 5.2 审批与办结的两阶段流转

```
                      [班委提单: applied_amount_cents]
                                     │
                                     ▼
                          [对应管理员进入审理阶段]
                                     │
               ┌─────────────────────┴─────────────────────┐
               ▼                                           ▼
     【分支 A：直接驳回】                        【分支 B：准予出账审批】
• 状态置为 REJECTED                          • 全额批准: approved = applied
• 必填：驳回原因说明                           • 核减批准: approved < applied
• 单据归档终止，需重新提单                     • 必填：核减批复原因
                                             • 班费平摊场景按 approved 金额自动重算
                                                           │
                                                           ▼
                                               [进入待办结状态 APPROVED]
                                                           │ (由对应出纳人操作)
                                                           ▼
                                               [执行办结并出账 SETTLED]
                                               • 扣减对应账户 / 个人分户余额
                                               • 自动汇总并追加生委垫资批复
                                               • 写入不可变审计流水

```

### 5.3 入账流转引擎 (Inflow Engine)

支持录入资金流入，强制包含来源凭据、金额（正整数分）、操作人与确切去向：

1. **灵活账户增资 (`TO_FLEXIBLE_ACCOUNT`)**：校系评优奖励或班级赞助，由团支书确认入账。
2. **系报销回款核销 (`TO_FACULTY_REIMBURSE`)**：系里经费打款到账，关联具体报销单号，由生活委员确认入账并平账。
3. **同学班费补缴充值 (`TO_STUDENT_SUB_ACCOUNT`)**：欠费同学转账还款或新收班费，由生活委员确认入账，定向增加该同学个人账户余额（平复负数透支）。同学名单由团支书录入，并支持增删改同学姓名，其余账号只可查看。

### 5.4 团支书全员统计看板

团支书专属工作台，按班委账号维度穿透分析：

* 提单频率与各资金渠道分布；
* 申请总额 vs 实际核准出账总额（核减差额统计）；
* 提单驳回率（`rejection_rate`）与常见驳回成因；
* 平均审批与办结响应时效。

---

## 6. 数据库物理模型设计 (Neon PostgreSQL DDL)

在 Neon Console 执行以下初始化 DDL：

```sql
-- 1. 用户与鉴权表 (用户名由团支书统一定义，不可篡改)
CREATE TABLE users (
    id VARCHAR(32) PRIMARY KEY,                    -- 如 'u_sec_01', 'u_life_01'
    username VARCHAR(64) UNIQUE NOT NULL,          -- 登录用户名，唯一且只读
    display_name VARCHAR(64) NOT NULL,             -- 真实姓名/职务
    role VARCHAR(32) NOT NULL,                     -- 'BRANCH_SECRETARY', 'LIFE_COMMITTEE', 'CLASS_COMMITTEE'
    password_hash VARCHAR(255) NOT NULL,          -- Argon2id 密文
    must_change_password BOOLEAN DEFAULT TRUE,     -- 首次登录强制改密
    is_active BOOLEAN DEFAULT TRUE,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    last_login_at TIMESTAMPTZ
);

-- 2. 班级实体资金账户表 (灵活公款、系报销暂挂)
CREATE TABLE accounts (
    id SERIAL PRIMARY KEY,
    name VARCHAR(64) NOT NULL,
    type VARCHAR(32) NOT NULL,                     -- 'FLEXIBLE_PUBLIC', 'FACULTY_REIMBURSE'
    balance_cents BIGINT NOT NULL DEFAULT 0,       -- 单位：分
    custodian_id VARCHAR(32) REFERENCES users(id)
);

-- 3. 同学个人独立虚拟账户表 (无公共总池，允许透支为负)
CREATE TABLE student_personal_accounts (
    student_id VARCHAR(32) PRIMARY KEY,            -- 学号
    name VARCHAR(64) NOT NULL,
    balance_cents BIGINT NOT NULL DEFAULT 0,       -- 允许负数
    total_recharged_cents BIGINT NOT NULL DEFAULT 0,
    total_spent_cents BIGINT NOT NULL DEFAULT 0,
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

-- 4. 动账审批单主表
CREATE TABLE expense_requests (
    id SERIAL PRIMARY KEY,
    title VARCHAR(128) NOT NULL,
    account_category VARCHAR(32) NOT NULL,         -- 'FLEXIBLE', 'FACULTY', 'CLASS_FUND'
    
    -- 金额三态严格解耦 (单位：分)
    applied_amount_cents BIGINT NOT NULL,          -- 申报金额
    approved_amount_cents BIGINT,                 -- 核准金额 (可核减)
    settled_amount_cents BIGINT,                  -- 实际出账金额
    
    applicant_id VARCHAR(32) NOT NULL REFERENCES users(id),
    reviewer_id VARCHAR(32) REFERENCES users(id),
    settler_id VARCHAR(32) REFERENCES users(id),
    
    status VARCHAR(32) NOT NULL DEFAULT 'PENDING_REVIEW', 
    -- 'PENDING_REVIEW', 'APPROVED', 'SETTLED', 'REJECTED'
    
    review_notes TEXT,                            -- 审批核减理由
    settlement_notes TEXT,                        -- 办结批复与自动垫资说明
    
    created_at TIMESTAMPTZ DEFAULT NOW(),
    reviewed_at TIMESTAMPTZ,
    settled_at TIMESTAMPTZ
);

-- 5. 班费分摊明细表
CREATE TABLE expense_splits (
    id SERIAL PRIMARY KEY,
    request_id INT REFERENCES expense_requests(id) ON DELETE CASCADE,
    student_id VARCHAR(32) REFERENCES student_personal_accounts(student_id),
    amount_cents BIGINT NOT NULL,                  -- 应扣金额
    is_tail_bearer BOOLEAN DEFAULT FALSE,          -- 是否承担尾差
    advance_cents BIGINT NOT NULL DEFAULT 0        -- 本次生委垫资金额 (Δadvance)
);

-- 6. 全局入账记录表
CREATE TABLE inflow_records (
    id BIGSERIAL PRIMARY KEY,
    amount_cents BIGINT NOT NULL CHECK (amount_cents > 0),
    source_title VARCHAR(128) NOT NULL,
    destination_type VARCHAR(32) NOT NULL,         -- 'TO_FLEXIBLE_ACCOUNT', 'TO_FACULTY_REIMBURSE', 'TO_STUDENT_SUB_ACCOUNT'
    target_student_id VARCHAR(32) REFERENCES student_personal_accounts(student_id),
    related_request_id INT REFERENCES expense_requests(id),
    operator_id VARCHAR(32) NOT NULL REFERENCES users(id),
    voucher_file_url TEXT,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- 7. 账户不可变历史流水明细
CREATE TABLE account_ledger_entries (
    id BIGSERIAL PRIMARY KEY,
    student_id VARCHAR(32) REFERENCES student_personal_accounts(student_id),
    account_id INT REFERENCES accounts(id),
    expense_request_id INT REFERENCES expense_requests(id),
    inflow_record_id BIGINT REFERENCES inflow_records(id),
    entry_type VARCHAR(32) NOT NULL,               -- 'EXPENSE_SPLIT', 'RECHARGE', 'DISBURSEMENT', 'INFLOW'
    change_cents BIGINT NOT NULL,                  -- 扣减为负，入账为正
    balance_after_cents BIGINT NOT NULL,
    notes TEXT,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- 8. 全局单调递增变更同步序列 (用于客户端离线增量同步)
CREATE TABLE change_events (
    seq BIGSERIAL PRIMARY KEY,
    entity_type VARCHAR(32) NOT NULL,
    entity_id VARCHAR(64) NOT NULL,
    event_type VARCHAR(32) NOT NULL,
    payload JSONB NOT NULL,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

```

---

## 7. C++ 核心领域实现范式

编写核心业务时，必须严格遵守以下 C++20 强类型定义与算法实现：

### 7.1 `Money` 领域对象与尾差平摊算法

```cpp
#pragma once
#include <cstdint>
#include <string>
#include <vector>
#include <stdexcept>
#include <format>
#include <compare>

class Money {
private:
    int64_t cents_{0}; // 唯一内部状态：整数分，支持负数

public:
    constexpr Money() noexcept = default;
    constexpr explicit Money(int64_t cents) noexcept : cents_(cents) {}

    [[nodiscard]] constexpr int64_t to_cents() const noexcept { return cents_; }

    [[nodiscard]] std::string to_plain_string() const {
        int64_t abs_val = std::abs(cents_);
        std::string sign = (cents_ < 0) ? "-" : "";
        return std::format("{}{}.{:02d}", sign, abs_val / 100, abs_val % 100);
    }

    constexpr Money operator+(Money rhs) const noexcept { return Money(cents_ + rhs.cents_); }
    constexpr Money operator-(Money rhs) const noexcept { return Money(cents_ - rhs.cents_); }
    constexpr Money operator-() const noexcept { return Money(-cents_); }
    constexpr Money operator*(int64_t scalar) const noexcept { return Money(cents_ * scalar); }
    Money operator*(Money rhs) const = delete; // 严禁金额相乘

    constexpr auto operator<=>(const Money& rhs) const noexcept = default;
};

struct SplitAllocation {
    std::string student_id;
    Money amount;
    bool is_tail_bearer;
};

// 欧几里得除法保全尾差平摊
inline std::vector<SplitAllocation> DistributeExpense(
    Money total, 
    const std::vector<std::string>& student_ids, 
    const std::string& tail_bearer_id
) {
    if (student_ids.empty()) throw std::invalid_argument("名单不能为空");

    int64_t total_cents = total.to_cents();
    int64_t k = static_cast<int64_t>(student_ids.size());
    int64_t q = total_cents / k;
    int64_t r = total_cents % k;

    std::vector<SplitAllocation> allocations;
    allocations.reserve(student_ids.size());

    int64_t verification_sum = 0;
    for (const auto& id : student_ids) {
        if (id == tail_bearer_id) {
            allocations.push_back({id, Money(q + r), true});
            verification_sum += (q + r);
        } else {
            allocations.push_back({id, Money(q), false});
            verification_sum += q;
        }
    }

    // 数学不变式断言
    if (verification_sum != total_cents) {
        throw std::logic_error("平摊校验失败：分项之和不等于总金额");
    }

    return allocations;
}

```

### 7.2 Neon 抗休眠执行器框架

```cpp
#pragma once
#include <pqxx/pqxx>
#include <chrono>
#include <thread>
#include <iostream>

class NeonContext {
private:
    std::string conn_str_;

public:
    explicit NeonContext(std::string conn_str) : conn_str_(std::move(conn_str)) {}

    template <typename TxFunc>
    auto ExecuteTransaction(TxFunc&& func, int max_retries = 3) {
        for (int attempt = 1; attempt <= max_retries; ++attempt) {
            try {
                pqxx::connection conn(conn_str_);
                pqxx::work tx(conn);
                auto result = func(tx);
                tx.commit();
                return result;
            } catch (const pqxx::broken_connection& e) {
                if (attempt == max_retries) throw;
                std::cout << "[Neon] 实例冷启动唤醒中，等待重试 (" << attempt << "/" << max_retries << ")...\n";
                std::this_thread::sleep_for(std::chrono::milliseconds(600 * attempt));
            }
        }
        throw std::runtime_error("Neon 连接重试耗尽");
    }
};

```

---

## 8. Vibe Coding 结对协作准则

在后续的具体编码实现与迭代中，AI 必须作为主力软件工程师严格恪守以下开发指令：

1. **拒绝浮点数妥协**：任何接口传输、业务运算、数据库读写中，**严禁将货币转换为 `float` 或 `double**`。前端展示层仅接收格式化后的定点字符串（如 `"25.00"`）或整数分（`2500`）。
2. **严守 UWP 视觉规范**：编写前端 XAML / QSS 时，必须坚守 `CornerRadius="0"` / `border-radius: 0px`、1px 细线边框、无扩散阴影及等宽表格数字。


3. **两阶段权限硬约束**：
* 灵活走账单据：非团支书发起审批/办结调用，直接在领域层抛出 `PermissionDeniedException`；
* 报销挂账单据：非生活委员发起审批/办结调用，直接拦截；
* 班费单据：团支书与生活委员均可审批，但**办结必须强制校验生活委员身份**。


4. **单据办结原子事务闭环**：办结操作必须在一个数据库事务内依次完成：`行级锁检查 -> 扣除个人账户 -> 计算生委垫资增量 -> 组装垫资说明 -> 更新主表状态为 SETTLED -> 写入 change_events`。任何异常立即回滚（ROLLBACK）。

系统规范已全量确立，请以此规范为唯一真理基准，开始第一阶段具体模块的代码构建。

## 9.其他
### 版本管理
利用GitHub仓库进行版本管理。仓库是https://github.com/Furina-1314/Recta
### 开发要求
根据提示词要求，设计工作Phase，并将其写入VibeState.md。以Phase为工作断点，避免单个Phase输出过多代码，同时每个Phase完成后都要及时更新进度文档。