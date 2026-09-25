-- 重置测试数据库(recta-test 分支):清空全部业务数据,保留表结构。
-- 用法:Neon Console → recta-test 分支 → SQL Editor 执行;
--      或任何 psql 客户端连接测试分支后执行。
-- 生产分支(production)请勿执行本脚本。
TRUNCATE account_ledger_entries, inflow_records, expense_splits,
         expense_requests, accounts, student_personal_accounts, users
RESTART IDENTITY CASCADE;
