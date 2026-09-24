#include "recta/storage/LedgerRepo.hpp"

#include <pqxx/params>

#include <utility>

namespace recta::storage {

int64_t LedgerRepo::InsertInflow(pqxx::work& tx, int64_t amount_cents,
                                 const std::string& source_title,
                                 const std::string& destination_type,
                                 const std::optional<std::string>& target_student_id,
                                 const std::optional<int>& related_request_id,
                                 const std::string& operator_id,
                                 const std::optional<std::string>& voucher_url) {
    const auto result = tx.exec(
        "INSERT INTO inflow_records "
        "(amount_cents, source_title, destination_type, target_student_id, related_request_id, "
        " operator_id, voucher_file_url) "
        "VALUES ($1, $2, $3, $4, $5, $6, $7) RETURNING id",
        pqxx::params(amount_cents, source_title, destination_type, target_student_id,
                     related_request_id, operator_id, voucher_url));
    return result.front()[0].as<int64_t>();
}

void LedgerRepo::AppendLedger(pqxx::work& tx, const LedgerEntry& entry) {
    tx.exec(
        "INSERT INTO account_ledger_entries "
        "(student_id, account_id, expense_request_id, inflow_record_id, entry_type, "
        " change_cents, balance_after_cents, notes) "
        "VALUES ($1, $2, $3, $4, $5, $6, $7, $8)",
        pqxx::params(entry.student_id, entry.account_id, entry.expense_request_id,
                     entry.inflow_record_id, entry.entry_type, entry.change_cents,
                     entry.balance_after_cents, entry.notes));
}

void LedgerRepo::AppendChangeEvent(pqxx::work& tx, const std::string& entity_type,
                                   const std::string& entity_id, const std::string& event_type,
                                   const std::string& payload_json) {
    tx.exec(
        "INSERT INTO change_events (entity_type, entity_id, event_type, payload) "
        "VALUES ($1, $2, $3, $4::jsonb)",
        pqxx::params(entity_type, entity_id, event_type, payload_json));
}

std::vector<ChangeEventRow> LedgerRepo::FetchChangeEventsSince(pqxx::work& tx,
                                                               int64_t after_seq, int limit) {
    const auto result = tx.exec(
        "SELECT seq, entity_type, entity_id, event_type, payload::text, created_at "
        "FROM change_events WHERE seq > $1 ORDER BY seq LIMIT $2",
        pqxx::params(after_seq, limit));
    std::vector<ChangeEventRow> events;
    events.reserve(static_cast<std::size_t>(result.size()));
    for (const auto& row : result) {
        ChangeEventRow event;
        event.seq = row["seq"].as<int64_t>();
        event.entity_type = row["entity_type"].as<std::string>();
        event.entity_id = row["entity_id"].as<std::string>();
        event.event_type = row["event_type"].as<std::string>();
        event.payload = row["payload"].as<std::string>();
        event.created_at = row["created_at"].as<std::optional<std::string>>();
        events.push_back(std::move(event));
    }
    return events;
}

std::vector<LedgerEntryRow> LedgerRepo::ListStudentLedger(pqxx::work& tx,
                                                          const std::string& student_id, int limit) {
    const auto result = tx.exec(
        "SELECT id, student_id, account_id, expense_request_id, inflow_record_id, entry_type, "
        "       change_cents, balance_after_cents, notes, created_at "
        "FROM account_ledger_entries WHERE student_id = $1 ORDER BY id DESC LIMIT $2",
        pqxx::params(student_id, limit));
    std::vector<LedgerEntryRow> entries;
    entries.reserve(static_cast<std::size_t>(result.size()));
    for (const auto& row : result) {
        LedgerEntryRow entry;
        entry.id = row["id"].as<int64_t>();
        entry.student_id = row["student_id"].as<std::optional<std::string>>();
        entry.account_id = row["account_id"].as<std::optional<int>>();
        entry.expense_request_id = row["expense_request_id"].as<std::optional<int>>();
        entry.inflow_record_id = row["inflow_record_id"].as<std::optional<int64_t>>();
        entry.entry_type = row["entry_type"].as<std::string>();
        entry.change_cents = row["change_cents"].as<int64_t>();
        entry.balance_after_cents = row["balance_after_cents"].as<int64_t>();
        entry.notes = row["notes"].as<std::optional<std::string>>();
        entry.created_at = row["created_at"].as<std::optional<std::string>>();
        entries.push_back(std::move(entry));
    }
    return entries;
}

std::vector<InflowRow> LedgerRepo::ListInflows(pqxx::work& tx, int limit) {
    const auto result = tx.exec(
        "SELECT id, amount_cents, source_title, destination_type, target_student_id, "
        "       related_request_id, operator_id, voucher_file_url, created_at "
        "FROM inflow_records ORDER BY id DESC LIMIT $1",
        pqxx::params(limit));
    std::vector<InflowRow> inflows;
    inflows.reserve(static_cast<std::size_t>(result.size()));
    for (const auto& row : result) {
        InflowRow inflow;
        inflow.id = row["id"].as<int64_t>();
        inflow.amount_cents = row["amount_cents"].as<int64_t>();
        inflow.source_title = row["source_title"].as<std::string>();
        inflow.destination_type = row["destination_type"].as<std::string>();
        inflow.target_student_id = row["target_student_id"].as<std::optional<std::string>>();
        inflow.related_request_id = row["related_request_id"].as<std::optional<int>>();
        inflow.operator_id = row["operator_id"].as<std::string>();
        inflow.voucher_file_url = row["voucher_file_url"].as<std::optional<std::string>>();
        inflow.created_at = row["created_at"].as<std::optional<std::string>>();
        inflows.push_back(std::move(inflow));
    }
    return inflows;
}

} // namespace recta::storage
