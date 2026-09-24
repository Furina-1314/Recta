#pragma once
#include "recta/storage/Rows.hpp"

#include <pqxx/pqxx>

#include <cstdint>
#include <optional>
#include <string>
#include <vector>

namespace recta::storage {

// 动账审批单 + 班费分摊明细。
class RequestRepo {
public:
    // 提单入库,返回新单号(PENDING_REVIEW)。
    [[nodiscard]] static int Insert(pqxx::work& tx, const std::string& title, const std::string& category,
                                    int64_t applied_amount_cents, const std::string& applicant_id);

    [[nodiscard]] static std::optional<ExpenseRequestRow> Find(pqxx::work& tx, int request_id);
    // 行级锁:办结事务必须先锁主单再动账(§8.4)。
    [[nodiscard]] static std::optional<ExpenseRequestRow> Lock(pqxx::work& tx, int request_id);
    [[nodiscard]] static std::vector<ExpenseRequestRow> List(pqxx::work& tx, const RequestFilter& filter);

    // 两阶段状态机迁移(均置时间戳与经办人)。
    static void MarkApproved(pqxx::work& tx, int request_id, int64_t approved_amount_cents,
                             const std::string& reviewer_id, const std::optional<std::string>& notes);
    static void MarkRejected(pqxx::work& tx, int request_id,
                             const std::string& reviewer_id, const std::string& notes);
    static void MarkSettled(pqxx::work& tx, int request_id, int64_t settled_amount_cents,
                            const std::string& settler_id, const std::optional<std::string>& notes);

    static void InsertSplits(pqxx::work& tx, int request_id, const std::vector<SplitRow>& splits);
    // 核减后按新核准金额整单重算分摊(先清后写)。
    static void ReplaceSplits(pqxx::work& tx, int request_id, const std::vector<SplitRow>& splits);
    [[nodiscard]] static std::vector<SplitRow> ListSplits(pqxx::work& tx, int request_id);

    // 办结时回填每人 Δadvance(§4.2 计算结果)。
    static void SetSplitAdvance(pqxx::work& tx, int request_id, const std::string& student_id,
                                int64_t advance_cents);
};

} // namespace recta::storage
