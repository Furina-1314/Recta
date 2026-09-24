-- 增补:支出单据证明材料链接(2026-09-25)。
-- 已通过 ALTER TABLE 应用到 production 与 recta-test 分支。
ALTER TABLE expense_requests ADD COLUMN IF NOT EXISTS voucher_url TEXT;
