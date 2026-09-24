#pragma once
#include "recta/storage/Rows.hpp"

#include <pqxx/pqxx>

#include <cstdint>
#include <optional>
#include <string>
#include <vector>

namespace recta::storage {

// 入账台账 + 不可变流水 + 变更事件序列。
class LedgerRepo {
public:
    [[nodiscard]] static int64_t InsertInflow(pqxx::work& tx, int64_t amount_cents,
                                              const std::string& source_title,
                                              const std::string& destination_type,
                                              const std::optional<std::string>& target_student_id,
                                              const std::optional<int>& related_request_id,
                                              const std::string& operator_id,
                                              const std::optional<std::string>& voucher_url);

    static void AppendLedger(pqxx::work& tx, const LedgerEntry& entry);

    static void AppendChangeEvent(pqxx::work& tx, const std::string& entity_type,
                                  const std::string& entity_id, const std::string& event_type,
                                  const std::string& payload_json);

    // 增量同步拉取:seq 严格递增(P12 客户端消费)。
    [[nodiscard]] static std::vector<ChangeEventRow> FetchChangeEventsSince(pqxx::work& tx,
                                                                            int64_t after_seq, int limit);
};

} // namespace recta::storage
