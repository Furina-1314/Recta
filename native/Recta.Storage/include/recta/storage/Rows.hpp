#pragma once
// 表行 DTO:一律携带原始 int64 分与数据库字符串枚举值,Money/Role 换算归服务层。
#include <cstdint>
#include <optional>
#include <string>
#include <vector>

namespace recta::storage {

struct UserRow {
    std::string id;
    std::string username;
    std::string display_name;
    std::string role; // 'BRANCH_SECRETARY' | 'LIFE_COMMITTEE' | 'CLASS_COMMITTEE'
    std::string password_hash;
    bool must_change_password = true;
    bool is_active = true;
    std::optional<std::string> last_login_at;
};

struct StudentAccountRow {
    std::string student_id;
    std::string name;
    int64_t balance_cents = 0; // 允许负数(透支)
    int64_t total_recharged_cents = 0;
    int64_t total_spent_cents = 0;
};

struct EntityAccountRow {
    int id = 0;
    std::string name;
    std::string type; // 'FLEXIBLE_PUBLIC' | 'FACULTY_REIMBURSE'
    int64_t balance_cents = 0;
    std::optional<std::string> custodian_id;
};

struct ExpenseRequestRow {
    int id = 0;
    std::string title;
    std::string account_category; // 'FLEXIBLE' | 'FACULTY' | 'CLASS_FUND'
    int64_t applied_amount_cents = 0;
    std::optional<int64_t> approved_amount_cents;
    std::optional<int64_t> settled_amount_cents;
    std::string applicant_id;
    std::optional<std::string> reviewer_id;
    std::optional<std::string> settler_id;
    std::string status; // 'PENDING_REVIEW' | 'APPROVED' | 'SETTLED' | 'REJECTED'
    std::optional<std::string> review_notes;
    std::optional<std::string> settlement_notes;
    std::optional<std::string> created_at;
    std::optional<std::string> reviewed_at;
    std::optional<std::string> settled_at;
};

struct SplitRow {
    std::string student_id;
    int64_t amount_cents = 0;
    bool is_tail_bearer = false;
    int64_t advance_cents = 0; // Δadvance:本次生委垫资
    std::optional<std::string> student_name; // 联查姓名(Inspector 展示),仓储读取时填充
};

struct RequestFilter {
    std::optional<std::string> status;
    std::optional<std::string> category;
    std::optional<std::string> applicant_id;
};

// 对账守恒三元组(§4.1):Σb = custodian_cash − advance_total。
struct CustodyAggregate {
    int64_t balances_sum = 0;
    int64_t custodian_cash = 0;
    int64_t advance_total = 0;
};

struct LedgerEntry {
    std::optional<std::string> student_id;
    std::optional<int> account_id;
    std::optional<int> expense_request_id;
    std::optional<int64_t> inflow_record_id;
    std::string entry_type; // 'EXPENSE_SPLIT' | 'RECHARGE' | 'DISBURSEMENT' | 'INFLOW'
    int64_t change_cents = 0;
    int64_t balance_after_cents = 0;
    std::optional<std::string> notes;
};

struct ChangeEventRow {
    int64_t seq = 0;
    std::string entity_type;
    std::string entity_id;
    std::string event_type;
    std::string payload; // JSONB 文本
    std::optional<std::string> created_at;
};

} // namespace recta::storage
