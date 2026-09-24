-- Recta 矩衡 —— 数据库初始化 DDL(Vibe.md §6 原文)
-- 已于 2026-09-24 在 Neon 项目 misty-bar-36297499 / production 分支执行。
-- 金额一律 BIGINT(分);审批与扣款并发依赖 FOR UPDATE 行级锁(见存储层)。

-- 1. 用户与鉴权表 (用户名由团支书统一定义，不可篡改)
CREATE TABLE users (
    id VARCHAR(32) PRIMARY KEY,                    -- 如 'u_sec_01', 'u_life_01'
    username VARCHAR(64) UNIQUE NOT NULL,          -- 登录用户名，唯一且只读
    display_name VARCHAR(64) NOT NULL,             -- 真实姓名/职务
    role VARCHAR(32) NOT NULL,                     -- 'BRANCH_SECRETARY', 'LIFE_COMMITTEE', 'CLASS_COMMITTEE'
    password_hash VARCHAR(255) NOT NULL,           -- Argon2id 密文
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
    approved_amount_cents BIGINT,                  -- 核准金额 (可核减)
    settled_amount_cents BIGINT,                   -- 实际出账金额

    applicant_id VARCHAR(32) NOT NULL REFERENCES users(id),
    reviewer_id VARCHAR(32) REFERENCES users(id),
    settler_id VARCHAR(32) REFERENCES users(id),

    status VARCHAR(32) NOT NULL DEFAULT 'PENDING_REVIEW',
    -- 'PENDING_REVIEW', 'APPROVED', 'SETTLED', 'REJECTED'

    review_notes TEXT,                             -- 审批核减理由
    settlement_notes TEXT,                         -- 办结批复与自动垫资说明
    voucher_url TEXT,                              -- 证明材料链接(增补列,见 002)

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
