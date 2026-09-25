// 存储层集成冒烟:直连真 Neon(production 分支)。
// 纪律:所有写路径测试一律 tx.abort() 回滚——生产分支不留测试残留;
// 提交路径由只读事务(ConnectCommitPath)验证。
#include "recta/Conservation.hpp"
#include "recta/Money.hpp"
#include "recta/storage/EnvConfig.hpp"
#include "recta/storage/EntityAccountsRepo.hpp"
#include "recta/storage/LedgerRepo.hpp"
#include "recta/storage/NeonContext.hpp"
#include "recta/storage/RequestRepo.hpp"
#include "recta/storage/StudentAccountsRepo.hpp"
#include "recta/storage/UsersRepo.hpp"

#include <pqxx/pqxx>

#include <gtest/gtest.h>

#ifdef _WIN32
#include <windows.h>
#endif

#include <filesystem>
#include <fstream>
#include <stdexcept>
#include <string>

namespace {

class StorageTest : public ::testing::Test {
protected:
    static void SetUpTestSuite() {
        try {
            conn_str = recta::storage::LoadConnectionString();
        } catch (const std::runtime_error&) {
            conn_str.clear();
        }
        if (conn_str.empty()) GTEST_SKIP() << "未配置 DATABASE_URL / .env.local,跳过真库冒烟";
    }

    // 手动事务夹具:测试体内任意 return/throw 都会在析构时回滚。
    struct RollingTx {
        pqxx::connection conn;
        pqxx::work tx;
        explicit RollingTx(const std::string& cs)
            : conn(cs), tx(conn) {}
        ~RollingTx() {
            try { tx.abort(); } catch (...) {}
        }
    };

    static std::string conn_str;
};

std::string StorageTest::conn_str;

TEST_F(StorageTest, EnvConfigResolvesLocalFile) {
    const auto cs = recta::storage::LoadConnectionString();
    EXPECT_EQ(cs.substr(0, 13), "postgresql://");
    EXPECT_NE(cs.find("neon.tech"), std::string::npos);
}

// RECTA_ENV_FILE 显式指向测试文件时,连接串必须取自该文件(优先于 cwd 上溯的 .env.local)。
TEST_F(StorageTest, EnvConfigHonorsEnvFileOverride) {
    const auto path = std::filesystem::temp_directory_path() / "recta_env_override_test.env";
    {
        std::ofstream out(path, std::ios::binary | std::ios::trunc);
        out << "# comment\n";
        out << "DATABASE_URL=\"postgresql://override-host/testdb?sslmode=require\"\n";
    }
    _putenv(("RECTA_ENV_FILE=" + path.string()).c_str());
    const auto cs = recta::storage::LoadConnectionString();
    _putenv("RECTA_ENV_FILE=");
    std::filesystem::remove(path);
    EXPECT_EQ(cs, "postgresql://override-host/testdb?sslmode=require");
}

// 测试模式(RECTA_TEST_MODE=1)下禁止回退到 .env.local——测试实例永不可能静默连上生产。
// 注意:必须用 CRT 的 _putenv——getenv 读的是 CRT 环境副本,
// Win32 SetEnvironmentVariableA 的运行期改动 getenv 看不到。
TEST_F(StorageTest, TestModeRefusesEnvLocalFallback) {
    _putenv("RECTA_TEST_MODE=1");
    _putenv("DATABASE_URL=");
    _putenv("RECTA_ENV_FILE=");
    // 测试运行目录上溯能找到仓库根的 .env.local(生产串);测试模式下必须拒绝。
    EXPECT_THROW(recta::storage::LoadConnectionString(), std::runtime_error);
    _putenv("RECTA_TEST_MODE=");
}

TEST_F(StorageTest, ConnectCommitPath) {
    recta::storage::NeonContext context(conn_str);
    const auto value = context.ExecuteTransaction([](pqxx::work& tx) {
        return tx.exec("SELECT 20260924").front()[0].as<int64_t>();
    });
    EXPECT_EQ(value, 20260924);
}

TEST_F(StorageTest, UserRoundTripInsideRollback) {
    RollingTx env{conn_str};

    recta::storage::UsersRepo::Insert(env.tx, "u_smoke_01", "smoke_user", "冒烟测试员",
                                      "CLASS_COMMITTEE", "argon2id$placeholder");
    auto found = recta::storage::UsersRepo::FindByUsername(env.tx, "smoke_user");
    ASSERT_TRUE(found.has_value());
    EXPECT_EQ(found->id, "u_smoke_01");
    EXPECT_EQ(found->role, "CLASS_COMMITTEE");
    EXPECT_TRUE(found->must_change_password);
    EXPECT_TRUE(found->is_active);

    recta::storage::UsersRepo::SetPassword(env.tx, "u_smoke_01", "argon2id$rotated", false);
    recta::storage::UsersRepo::TouchLastLogin(env.tx, "u_smoke_01");
    recta::storage::UsersRepo::SetActive(env.tx, "u_smoke_01", false);

    const auto after = recta::storage::UsersRepo::FindById(env.tx, "u_smoke_01");
    ASSERT_TRUE(after.has_value());
    EXPECT_EQ(after->password_hash, "argon2id$rotated");
    EXPECT_FALSE(after->must_change_password);
    EXPECT_FALSE(after->is_active);
    ASSERT_TRUE(after->last_login_at.has_value());

    EXPECT_EQ(recta::storage::UsersRepo::FindByUsername(env.tx, "no_such_user"), std::nullopt);
}

TEST_F(StorageTest, StudentAccountsDebitCreditAndCustody) {
    RollingTx env{conn_str};

    for (const auto& [id, name] : {std::pair{"s_smoke_01", "张三"}, {"s_smoke_02", "李四"}}) {
        recta::storage::StudentAccountsRepo::Upsert(env.tx, id, name);
    }

    EXPECT_EQ(recta::storage::StudentAccountsRepo::ApplyCredit(env.tx, "s_smoke_01", 10000), 10000);
    // 扣 25.00 → 余 75.00;再扣 100.00 → 透支 -25.00(生委垫资场景)。
    EXPECT_EQ(recta::storage::StudentAccountsRepo::ApplyDebit(env.tx, "s_smoke_01", 2500), 7500);
    EXPECT_EQ(recta::storage::StudentAccountsRepo::ApplyDebit(env.tx, "s_smoke_01", 10000), -2500);

    const auto student = recta::storage::StudentAccountsRepo::Find(env.tx, "s_smoke_01");
    ASSERT_TRUE(student.has_value());
    EXPECT_EQ(student->balance_cents, -2500);
    EXPECT_EQ(student->total_recharged_cents, 10000);
    EXPECT_EQ(student->total_spent_cents, 12500);

    // 批量锁:入参乱序,返回必须按 student_id 升序(确定性加锁)。
    const auto locked = recta::storage::StudentAccountsRepo::LockMany(
        env.tx, {"s_smoke_02", "s_smoke_01"});
    ASSERT_EQ(locked.size(), 2U);
    EXPECT_EQ(locked[0].student_id, "s_smoke_01");
    EXPECT_EQ(locked[1].student_id, "s_smoke_02");

    // 守恒三元组与领域层复核:Σb = C_cash − A_advance。
    const auto aggregate = recta::storage::StudentAccountsRepo::AggregateCustody(env.tx);
    EXPECT_EQ(aggregate.balances_sum, -2500);
    EXPECT_EQ(aggregate.custodian_cash, 0);
    EXPECT_EQ(aggregate.advance_total, 2500);
    EXPECT_TRUE(recta::IsConserved(recta::Money(aggregate.balances_sum),
                                   recta::Money(aggregate.custodian_cash),
                                   recta::Money(aggregate.advance_total)));

    recta::storage::StudentAccountsRepo::Rename(env.tx, "s_smoke_02", "李四丰");
    EXPECT_EQ(recta::storage::StudentAccountsRepo::Find(env.tx, "s_smoke_02")->name, "李四丰");
}

TEST_F(StorageTest, ExpenseRequestLifecycleInsideRollback) {
    RollingTx env{conn_str};

    recta::storage::UsersRepo::Insert(env.tx, "u_smoke_app", "smoke_applicant", "提单人",
                                      "CLASS_COMMITTEE", "argon2id$placeholder");
    const int request_id = recta::storage::RequestRepo::Insert(
        env.tx, "自动化实验耗材采购", "CLASS_FUND", 14000, "u_smoke_app");
    EXPECT_GT(request_id, 0);

    const auto pending = recta::storage::RequestRepo::Find(env.tx, request_id);
    ASSERT_TRUE(pending.has_value());
    EXPECT_EQ(pending->status, "PENDING_REVIEW");
    EXPECT_EQ(pending->applied_amount_cents, 14000);
    EXPECT_FALSE(pending->approved_amount_cents.has_value());

    recta::storage::RequestRepo::MarkApproved(env.tx, request_id, 14000, "u_smoke_app",
                                              std::nullopt);
    const auto locked = recta::storage::RequestRepo::Lock(env.tx, request_id);
    ASSERT_TRUE(locked.has_value());
    EXPECT_EQ(locked->status, "APPROVED");
    EXPECT_EQ(locked->approved_amount_cents.value(), 14000);
    ASSERT_TRUE(locked->reviewed_at.has_value());

    // 140.00 / 7 人平摊写入(领域算法产物),并带垫资列;分户需先存在(外键强校验)。
    std::vector<std::string> split_ids;
    for (int i = 1; i <= 7; ++i) split_ids.push_back("s_smoke_split_" + std::to_string(i));
    for (const auto& id : split_ids) {
        recta::storage::StudentAccountsRepo::Upsert(env.tx, id, "参摊同学" + id.substr(id.size() - 2));
    }
    std::vector<recta::storage::SplitRow> splits;
    for (std::size_t i = 0; i < split_ids.size(); ++i) {
        splits.push_back({split_ids[i], 2000, i == 6, i == 6 ? 2000 : 0});
    }
    recta::storage::RequestRepo::InsertSplits(env.tx, request_id, splits);
    const auto read_back = recta::storage::RequestRepo::ListSplits(env.tx, request_id);
    ASSERT_EQ(read_back.size(), 7U);
    EXPECT_EQ(read_back[6].student_id, "s_smoke_split_7");
    EXPECT_TRUE(read_back[6].is_tail_bearer);
    EXPECT_EQ(read_back[6].advance_cents, 2000);

    recta::storage::RequestRepo::MarkSettled(env.tx, request_id, 14000, "u_smoke_app",
                                             "垫资批复:s_smoke_split_7 由生委垫付 20.00 元");
    const auto settled = recta::storage::RequestRepo::Find(env.tx, request_id);
    ASSERT_TRUE(settled.has_value());
    EXPECT_EQ(settled->status, "SETTLED");
    EXPECT_EQ(settled->settled_amount_cents.value(), 14000);
    ASSERT_TRUE(settled->settlement_notes.has_value());

    // 状态过滤。
    recta::storage::RequestFilter filter;
    filter.status = "SETTLED";
    const auto list = recta::storage::RequestRepo::List(env.tx, filter);
    ASSERT_FALSE(list.empty());
    EXPECT_EQ(list.front().id, request_id); // id DESC,最新在前
}

TEST_F(StorageTest, LedgerInflowAndChangeEventsInsideRollback) {
    RollingTx env{conn_str};

    recta::storage::UsersRepo::Insert(env.tx, "u_smoke_op", "smoke_operator", "经办人",
                                      "LIFE_COMMITTEE", "argon2id$placeholder");

    const int64_t inflow_id = recta::storage::LedgerRepo::InsertInflow(
        env.tx, 50000, "系财务 9 月经费打款", "TO_FACULTY_REIMBURSE",
        std::nullopt, std::nullopt, "u_smoke_op", std::nullopt);
    EXPECT_GT(inflow_id, 0);

    recta::storage::LedgerEntry entry;
    entry.inflow_record_id = inflow_id;
    entry.entry_type = "INFLOW";
    entry.change_cents = 50000;
    entry.balance_after_cents = 50000;
    entry.notes = "系报销暂挂账户入账";
    recta::storage::LedgerRepo::AppendLedger(env.tx, entry);

    recta::storage::LedgerRepo::AppendChangeEvent(env.tx, "inflow", std::to_string(inflow_id),
                                                  "CREATED", "{\"amount_cents\":50000}");
    const auto events = recta::storage::LedgerRepo::FetchChangeEventsSince(env.tx, 0, 50);
    ASSERT_FALSE(events.empty());
    EXPECT_EQ(events.back().entity_type, "inflow");
    EXPECT_EQ(events.back().event_type, "CREATED");
    EXPECT_NE(events.back().payload.find("50000"), std::string::npos);
    // seq 单调递增。
    for (std::size_t i = 1; i < events.size(); ++i) {
        EXPECT_GT(events[i].seq, events[i - 1].seq);
    }
}

TEST_F(StorageTest, EntityAccountLockAndAdjustInsideRollback) {
    RollingTx env{conn_str};

    // 种子账户(P4 落正式引导;此处仅在回滚事务内构造测试数据)。
    if (!recta::storage::EntityAccountsRepo::FindByType(env.tx, "FLEXIBLE_PUBLIC").has_value()) {
        recta::storage::EntityAccountsRepo::Insert(env.tx, "班级灵活公款", "FLEXIBLE_PUBLIC",
                                                   std::nullopt);
    }
    auto account = recta::storage::EntityAccountsRepo::LockByType(env.tx, "FLEXIBLE_PUBLIC");
    ASSERT_TRUE(account.has_value());

    EXPECT_EQ(recta::storage::EntityAccountsRepo::AdjustBalance(env.tx, account->id, 5000),
              account->balance_cents + 5000);
    EXPECT_EQ(recta::storage::EntityAccountsRepo::AdjustBalance(env.tx, account->id, -2000),
              account->balance_cents + 3000);
}

} // namespace
