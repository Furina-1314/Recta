#pragma once
#include "recta/storage/Rows.hpp"

#include <pqxx/pqxx>

#include <optional>
#include <string>
#include <vector>

namespace recta::storage {

// 所有方法在调用方事务内执行(pqxx::work&),自身不提交不回滚。
class UsersRepo {
public:
    [[nodiscard]] static std::optional<UserRow> FindByUsername(pqxx::work& tx, const std::string& username);
    [[nodiscard]] static std::optional<UserRow> FindById(pqxx::work& tx, const std::string& id);
    [[nodiscard]] static std::vector<UserRow> ListAll(pqxx::work& tx);

    static void Insert(pqxx::work& tx, const std::string& id, const std::string& username,
                       const std::string& display_name, const std::string& role,
                       const std::string& password_hash);
    static void SetPassword(pqxx::work& tx, const std::string& id,
                            const std::string& password_hash, bool must_change);
    static void TouchLastLogin(pqxx::work& tx, const std::string& id);
    static void SetActive(pqxx::work& tx, const std::string& id, bool active);
};

} // namespace recta::storage
