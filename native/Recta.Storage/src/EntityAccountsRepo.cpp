#include "recta/storage/EntityAccountsRepo.hpp"

#include <pqxx/params>

#include <stdexcept>

namespace recta::storage {
namespace {

constexpr auto kColumns = "id, name, type, balance_cents, custodian_id";

template <typename RowT>
EntityAccountRow MapRow(const RowT& row) {
    EntityAccountRow account;
    account.id = row["id"].as<int>();
    account.name = row["name"].as<std::string>();
    account.type = row["type"].as<std::string>();
    account.balance_cents = row["balance_cents"].as<int64_t>();
    account.custodian_id = row["custodian_id"].as<std::optional<std::string>>();
    return account;
}

} // namespace

std::vector<EntityAccountRow> EntityAccountsRepo::ListAll(pqxx::work& tx) {
    const auto result = tx.exec("SELECT " + std::string(kColumns) + " FROM accounts ORDER BY id");
    std::vector<EntityAccountRow> accounts;
    accounts.reserve(static_cast<std::size_t>(result.size()));
    for (const auto& row : result) accounts.push_back(MapRow(row));
    return accounts;
}

std::optional<EntityAccountRow> EntityAccountsRepo::FindByType(pqxx::work& tx, const std::string& type) {
    const auto result = tx.exec(
        "SELECT " + std::string(kColumns) + " FROM accounts WHERE type = $1", pqxx::params(type));
    if (result.empty()) return std::nullopt;
    return MapRow(result.front());
}

std::optional<EntityAccountRow> EntityAccountsRepo::LockByType(pqxx::work& tx, const std::string& type) {
    const auto result = tx.exec(
        "SELECT " + std::string(kColumns) + " FROM accounts WHERE type = $1 FOR UPDATE",
        pqxx::params(type));
    if (result.empty()) return std::nullopt;
    return MapRow(result.front());
}

int64_t EntityAccountsRepo::AdjustBalance(pqxx::work& tx, int account_id, int64_t delta_cents) {
    const auto result = tx.exec(
        "UPDATE accounts SET balance_cents = balance_cents + $2 WHERE id = $1 RETURNING balance_cents",
        pqxx::params(account_id, delta_cents));
    if (result.empty()) throw std::runtime_error("实体账户不存在: id=" + std::to_string(account_id));
    return result.front()[0].as<int64_t>();
}

void EntityAccountsRepo::Insert(pqxx::work& tx, const std::string& name, const std::string& type,
                                const std::optional<std::string>& custodian_id) {
    tx.exec(
        "INSERT INTO accounts (name, type, balance_cents, custodian_id) VALUES ($1, $2, 0, $3)",
        pqxx::params(name, type, custodian_id));
}

void EntityAccountsRepo::SetCustodian(pqxx::work& tx, const std::string& type,
                                      const std::string& custodian_id) {
    tx.exec("UPDATE accounts SET custodian_id = $2 WHERE type = $1",
            pqxx::params(type, custodian_id));
}

} // namespace recta::storage
