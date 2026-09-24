#include "recta/storage/StudentAccountsRepo.hpp"

#include <pqxx/params>

#include <stdexcept>

namespace recta::storage {
namespace {

constexpr auto kColumns =
    "student_id, name, balance_cents, total_recharged_cents, total_spent_cents";

template <typename RowT>
StudentAccountRow MapRow(const RowT& row) {
    StudentAccountRow student;
    student.student_id = row["student_id"].as<std::string>();
    student.name = row["name"].as<std::string>();
    student.balance_cents = row["balance_cents"].as<int64_t>();
    student.total_recharged_cents = row["total_recharged_cents"].as<int64_t>();
    student.total_spent_cents = row["total_spent_cents"].as<int64_t>();
    return student;
}

} // namespace

std::vector<StudentAccountRow> StudentAccountsRepo::ListAll(pqxx::work& tx) {
    const auto result = tx.exec(
        "SELECT " + std::string(kColumns) + " FROM student_personal_accounts ORDER BY student_id");
    std::vector<StudentAccountRow> students;
    students.reserve(static_cast<std::size_t>(result.size()));
    for (const auto& row : result) students.push_back(MapRow(row));
    return students;
}

std::optional<StudentAccountRow> StudentAccountsRepo::Find(pqxx::work& tx, const std::string& student_id) {
    const auto result = tx.exec(
        "SELECT " + std::string(kColumns) + " FROM student_personal_accounts WHERE student_id = $1",
        pqxx::params(student_id));
    if (result.empty()) return std::nullopt;
    return MapRow(result.front());
}

std::vector<StudentAccountRow> StudentAccountsRepo::LockMany(pqxx::work& tx,
                                                             const std::vector<std::string>& student_ids) {
    if (student_ids.empty()) return {};

    // 占位符逐个展开;ORDER BY student_id 保证多事务并发办结时加锁顺序一致,杜绝死锁。
    std::string sql = "SELECT " + std::string(kColumns) +
                      " FROM student_personal_accounts WHERE student_id IN (";
    pqxx::params params;
    for (std::size_t i = 0; i < student_ids.size(); ++i) {
        sql += (i == 0) ? "$1" : ", $" + std::to_string(i + 1);
        params.append(student_ids[i]);
    }
    sql += ") ORDER BY student_id FOR UPDATE";

    const auto result = tx.exec(sql, params);
    std::vector<StudentAccountRow> students;
    students.reserve(static_cast<std::size_t>(result.size()));
    for (const auto& row : result) students.push_back(MapRow(row));
    return students;
}

int64_t StudentAccountsRepo::ApplyDebit(pqxx::work& tx, const std::string& student_id, int64_t amount_cents) {
    if (amount_cents <= 0) throw std::invalid_argument("出账金额必须为正数(分)");
    const auto result = tx.exec(
        "UPDATE student_personal_accounts "
        "SET balance_cents = balance_cents - $2, "
        "    total_spent_cents = total_spent_cents + $2, "
        "    updated_at = NOW() "
        "WHERE student_id = $1 "
        "RETURNING balance_cents",
        pqxx::params(student_id, amount_cents));
    if (result.empty()) throw std::runtime_error("分户不存在，无法出账: " + student_id);
    return result.front()[0].as<int64_t>();
}

int64_t StudentAccountsRepo::ApplyCredit(pqxx::work& tx, const std::string& student_id, int64_t amount_cents) {
    if (amount_cents <= 0) throw std::invalid_argument("入账金额必须为正数(分)");
    const auto result = tx.exec(
        "UPDATE student_personal_accounts "
        "SET balance_cents = balance_cents + $2, "
        "    total_recharged_cents = total_recharged_cents + $2, "
        "    updated_at = NOW() "
        "WHERE student_id = $1 "
        "RETURNING balance_cents",
        pqxx::params(student_id, amount_cents));
    if (result.empty()) throw std::runtime_error("分户不存在，无法入账: " + student_id);
    return result.front()[0].as<int64_t>();
}

CustodyAggregate StudentAccountsRepo::AggregateCustody(pqxx::work& tx) {
    const auto result = tx.exec(
        "SELECT COALESCE(SUM(balance_cents), 0), "
        "       COALESCE(SUM(balance_cents) FILTER (WHERE balance_cents > 0), 0), "
        "       COALESCE(SUM(-balance_cents) FILTER (WHERE balance_cents < 0), 0) "
        "FROM student_personal_accounts");
    const auto& row = result.front();
    CustodyAggregate aggregate;
    aggregate.balances_sum = row[0].as<int64_t>();
    aggregate.custodian_cash = row[1].as<int64_t>();
    aggregate.advance_total = row[2].as<int64_t>();
    return aggregate;
}

void StudentAccountsRepo::Upsert(pqxx::work& tx, const std::string& student_id, const std::string& name) {
    tx.exec(
        "INSERT INTO student_personal_accounts (student_id, name) VALUES ($1, $2) "
        "ON CONFLICT (student_id) DO UPDATE SET name = EXCLUDED.name",
        pqxx::params(student_id, name));
}

void StudentAccountsRepo::Rename(pqxx::work& tx, const std::string& student_id, const std::string& name) {
    tx.exec(
        "UPDATE student_personal_accounts SET name = $2, updated_at = NOW() WHERE student_id = $1",
        pqxx::params(student_id, name));
}

} // namespace recta::storage
