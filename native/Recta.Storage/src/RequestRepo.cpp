#include "recta/storage/RequestRepo.hpp"

#include <pqxx/params>

#include <utility>

namespace recta::storage {
namespace {

constexpr auto kColumns =
    "id, title, account_category, applied_amount_cents, approved_amount_cents, settled_amount_cents, "
    "applicant_id, reviewer_id, settler_id, status, review_notes, settlement_notes, "
    "created_at, reviewed_at, settled_at";

template <typename RowT>
ExpenseRequestRow MapRow(const RowT& row) {
    ExpenseRequestRow request;
    request.id = row["id"].as<int>();
    request.title = row["title"].as<std::string>();
    request.account_category = row["account_category"].as<std::string>();
    request.applied_amount_cents = row["applied_amount_cents"].as<int64_t>();
    request.approved_amount_cents = row["approved_amount_cents"].as<std::optional<int64_t>>();
    request.settled_amount_cents = row["settled_amount_cents"].as<std::optional<int64_t>>();
    request.applicant_id = row["applicant_id"].as<std::string>();
    request.reviewer_id = row["reviewer_id"].as<std::optional<std::string>>();
    request.settler_id = row["settler_id"].as<std::optional<std::string>>();
    request.status = row["status"].as<std::string>();
    request.review_notes = row["review_notes"].as<std::optional<std::string>>();
    request.settlement_notes = row["settlement_notes"].as<std::optional<std::string>>();
    request.created_at = row["created_at"].as<std::optional<std::string>>();
    request.reviewed_at = row["reviewed_at"].as<std::optional<std::string>>();
    request.settled_at = row["settled_at"].as<std::optional<std::string>>();
    return request;
}

} // namespace

int RequestRepo::Insert(pqxx::work& tx, const std::string& title, const std::string& category,
                        int64_t applied_amount_cents, const std::string& applicant_id) {
    const auto result = tx.exec(
        "INSERT INTO expense_requests (title, account_category, applied_amount_cents, applicant_id) "
        "VALUES ($1, $2, $3, $4) RETURNING id",
        pqxx::params(title, category, applied_amount_cents, applicant_id));
    return result.front()[0].as<int>();
}

std::optional<ExpenseRequestRow> RequestRepo::Find(pqxx::work& tx, int request_id) {
    const auto result = tx.exec(
        "SELECT " + std::string(kColumns) + " FROM expense_requests WHERE id = $1",
        pqxx::params(request_id));
    if (result.empty()) return std::nullopt;
    return MapRow(result.front());
}

std::optional<ExpenseRequestRow> RequestRepo::Lock(pqxx::work& tx, int request_id) {
    const auto result = tx.exec(
        "SELECT " + std::string(kColumns) + " FROM expense_requests WHERE id = $1 FOR UPDATE",
        pqxx::params(request_id));
    if (result.empty()) return std::nullopt;
    return MapRow(result.front());
}

std::vector<ExpenseRequestRow> RequestRepo::List(pqxx::work& tx, const RequestFilter& filter) {
    std::string sql = "SELECT " + std::string(kColumns) + " FROM expense_requests WHERE 1=1";
    pqxx::params params;
    int next = 1;
    if (filter.status) {
        sql += " AND status = $" + std::to_string(next++);
        params.append(*filter.status);
    }
    if (filter.category) {
        sql += " AND account_category = $" + std::to_string(next++);
        params.append(*filter.category);
    }
    if (filter.applicant_id) {
        sql += " AND applicant_id = $" + std::to_string(next++);
        params.append(*filter.applicant_id);
    }
    sql += " ORDER BY id DESC";

    const auto result = tx.exec(sql, params);
    std::vector<ExpenseRequestRow> requests;
    requests.reserve(static_cast<std::size_t>(result.size()));
    for (const auto& row : result) requests.push_back(MapRow(row));
    return requests;
}

void RequestRepo::MarkApproved(pqxx::work& tx, int request_id, int64_t approved_amount_cents,
                               const std::string& reviewer_id, const std::optional<std::string>& notes) {
    tx.exec(
        "UPDATE expense_requests "
        "SET status = 'APPROVED', approved_amount_cents = $2, reviewer_id = $3, "
        "    review_notes = $4, reviewed_at = NOW() "
        "WHERE id = $1",
        pqxx::params(request_id, approved_amount_cents, reviewer_id, notes));
}

void RequestRepo::MarkRejected(pqxx::work& tx, int request_id,
                               const std::string& reviewer_id, const std::string& notes) {
    tx.exec(
        "UPDATE expense_requests "
        "SET status = 'REJECTED', reviewer_id = $2, review_notes = $3, reviewed_at = NOW() "
        "WHERE id = $1",
        pqxx::params(request_id, reviewer_id, notes));
}

void RequestRepo::MarkSettled(pqxx::work& tx, int request_id, int64_t settled_amount_cents,
                              const std::string& settler_id, const std::optional<std::string>& notes) {
    tx.exec(
        "UPDATE expense_requests "
        "SET status = 'SETTLED', settled_amount_cents = $2, settler_id = $3, "
        "    settlement_notes = $4, settled_at = NOW() "
        "WHERE id = $1",
        pqxx::params(request_id, settled_amount_cents, settler_id, notes));
}

void RequestRepo::InsertSplits(pqxx::work& tx, int request_id, const std::vector<SplitRow>& splits) {
    for (const auto& split : splits) {
        tx.exec(
            "INSERT INTO expense_splits (request_id, student_id, amount_cents, is_tail_bearer, advance_cents) "
            "VALUES ($1, $2, $3, $4, $5)",
            pqxx::params(request_id, split.student_id, split.amount_cents, split.is_tail_bearer,
                         split.advance_cents));
    }
}

void RequestRepo::ReplaceSplits(pqxx::work& tx, int request_id, const std::vector<SplitRow>& splits) {
    tx.exec("DELETE FROM expense_splits WHERE request_id = $1", pqxx::params(request_id));
    InsertSplits(tx, request_id, splits);
}

std::vector<SplitRow> RequestRepo::ListSplits(pqxx::work& tx, int request_id) {
    const auto result = tx.exec(
        "SELECT student_id, amount_cents, is_tail_bearer, advance_cents "
        "FROM expense_splits WHERE request_id = $1 ORDER BY id",
        pqxx::params(request_id));
    std::vector<SplitRow> splits;
    splits.reserve(static_cast<std::size_t>(result.size()));
    for (const auto& row : result) {
        SplitRow split;
        split.student_id = row["student_id"].as<std::string>();
        split.amount_cents = row["amount_cents"].as<int64_t>();
        split.is_tail_bearer = row["is_tail_bearer"].as<bool>();
        split.advance_cents = row["advance_cents"].as<int64_t>();
        splits.push_back(std::move(split));
    }
    return splits;
}

void RequestRepo::SetSplitAdvance(pqxx::work& tx, int request_id, const std::string& student_id,
                                  int64_t advance_cents) {
    tx.exec(
        "UPDATE expense_splits SET advance_cents = $3 "
        "WHERE request_id = $1 AND student_id = $2",
        pqxx::params(request_id, student_id, advance_cents));
}

} // namespace recta::storage
