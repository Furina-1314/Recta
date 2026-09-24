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

} // namespace recta::storage
