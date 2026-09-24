#pragma once
#include "recta/storage/Rows.hpp"

#include <pqxx/pqxx>

#include <cstdint>
#include <optional>
#include <string>
#include <vector>

namespace recta::storage {

// 班级实体资金账户(灵活公款 / 系报销暂挂)。
class EntityAccountsRepo {
public:
    [[nodiscard]] static std::vector<EntityAccountRow> ListAll(pqxx::work& tx);
    [[nodiscard]] static std::optional<EntityAccountRow> FindByType(pqxx::work& tx, const std::string& type);
    [[nodiscard]] static std::optional<EntityAccountRow> LockByType(pqxx::work& tx, const std::string& type);

    // 余额调整(正增负减),返回调整后余额。
    [[nodiscard]] static int64_t AdjustBalance(pqxx::work& tx, int account_id, int64_t delta_cents);

    // 初始种子(P4 搭配首个用户创建后调用)。
    static void Insert(pqxx::work& tx, const std::string& name, const std::string& type,
                       const std::optional<std::string>& custodian_id);
};

} // namespace recta::storage
