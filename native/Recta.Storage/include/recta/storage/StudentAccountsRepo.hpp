#pragma once
#include "recta/storage/Rows.hpp"

#include <pqxx/pqxx>

#include <cstdint>
#include <optional>
#include <string>
#include <vector>

namespace recta::storage {

class StudentAccountsRepo {
public:
    [[nodiscard]] static std::vector<StudentAccountRow> ListAll(pqxx::work& tx);
    [[nodiscard]] static std::optional<StudentAccountRow> Find(pqxx::work& tx, const std::string& student_id);

    // 批量行级排他锁(§2 存储规约):按 student_id 升序加锁,保证加锁顺序确定、避免死锁。
    [[nodiscard]] static std::vector<StudentAccountRow> LockMany(pqxx::work& tx,
                                                                 const std::vector<std::string>& student_ids);

    // 出账:balance -= amount,total_spent += amount;返回扣后余额。
    [[nodiscard]] static int64_t ApplyDebit(pqxx::work& tx, const std::string& student_id, int64_t amount_cents);
    // 入账:balance += amount,total_recharged += amount;返回充后余额。
    [[nodiscard]] static int64_t ApplyCredit(pqxx::work& tx, const std::string& student_id, int64_t amount_cents);

    // 守恒三元组聚合(§4.1):Σb、C_cash、A_advance 一次性由数据库算出。
    [[nodiscard]] static CustodyAggregate AggregateCustody(pqxx::work& tx);

    // 同学名单管理(团支书录入,§5.3)。
    static void Upsert(pqxx::work& tx, const std::string& student_id, const std::string& name);
    static void Rename(pqxx::work& tx, const std::string& student_id, const std::string& name);
};

} // namespace recta::storage
