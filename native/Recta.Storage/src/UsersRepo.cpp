#include "recta/storage/UsersRepo.hpp"

namespace recta::storage {
namespace {

constexpr auto kColumns =
    "id, username, display_name, role, password_hash, must_change_password, is_active, last_login_at";

template <typename RowT>
UserRow MapRow(const RowT& row) {
    UserRow user;
    user.id = row["id"].as<std::string>();
    user.username = row["username"].as<std::string>();
    user.display_name = row["display_name"].as<std::string>();
    user.role = row["role"].as<std::string>();
    user.password_hash = row["password_hash"].as<std::string>();
    user.must_change_password = row["must_change_password"].as<bool>();
    user.is_active = row["is_active"].as<bool>();
    user.last_login_at = row["last_login_at"].as<std::optional<std::string>>();
    return user;
}

} // namespace

std::optional<UserRow> UsersRepo::FindByUsername(pqxx::work& tx, const std::string& username) {
    const auto result = tx.exec(
        "SELECT " + std::string(kColumns) + " FROM users WHERE username = $1",
        pqxx::params(username));
    if (result.empty()) return std::nullopt;
    return MapRow(result.front());
}

std::optional<UserRow> UsersRepo::FindById(pqxx::work& tx, const std::string& id) {
    const auto result = tx.exec(
        "SELECT " + std::string(kColumns) + " FROM users WHERE id = $1", pqxx::params(id));
    if (result.empty()) return std::nullopt;
    return MapRow(result.front());
}

std::vector<UserRow> UsersRepo::ListAll(pqxx::work& tx) {
    const auto result = tx.exec("SELECT " + std::string(kColumns) + " FROM users ORDER BY id");
    std::vector<UserRow> users;
    users.reserve(static_cast<std::size_t>(result.size()));
    for (const auto& row : result) users.push_back(MapRow(row));
    return users;
}

void UsersRepo::Insert(pqxx::work& tx, const std::string& id, const std::string& username,
                       const std::string& display_name, const std::string& role,
                       const std::string& password_hash) {
    tx.exec(
        "INSERT INTO users (id, username, display_name, role, password_hash) "
        "VALUES ($1, $2, $3, $4, $5)",
        pqxx::params(id, username, display_name, role, password_hash));
}

void UsersRepo::SetPassword(pqxx::work& tx, const std::string& id,
                            const std::string& password_hash, bool must_change) {
    tx.exec(
        "UPDATE users SET password_hash = $2, must_change_password = $3 WHERE id = $1",
        pqxx::params(id, password_hash, must_change));
}

void UsersRepo::TouchLastLogin(pqxx::work& tx, const std::string& id) {
    tx.exec("UPDATE users SET last_login_at = NOW() WHERE id = $1", pqxx::params(id));
}

void UsersRepo::SetActive(pqxx::work& tx, const std::string& id, bool active) {
    tx.exec("UPDATE users SET is_active = $2 WHERE id = $1", pqxx::params(id, active));
}

} // namespace recta::storage
