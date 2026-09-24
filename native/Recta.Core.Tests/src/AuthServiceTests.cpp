// 认证服务集成测试:跑在 Neon recta-test 分支(与 production 物理隔离,可任意写)。
// 每个用例开头 TRUNCATE 全表,保证顺序无关;未配置测试连接串则整组跳过。
#include "recta/Roles.hpp"
#include "recta/core/AuthService.hpp"
#include "recta/storage/EntityAccountsRepo.hpp"
#include "recta/storage/EnvConfig.hpp"
#include "recta/storage/NeonContext.hpp"
#include "recta/storage/UsersRepo.hpp"

#include <pqxx/pqxx>

#include <gtest/gtest.h>

#include <memory>
#include <string>

namespace {

class AuthServiceTest : public ::testing::Test {
protected:
    static void SetUpTestSuite() {
        const auto cs = recta::storage::TryLoadTestConnectionString();
        if (!cs) {
            GTEST_SKIP() << "未配置 RECTA_TEST_DATABASE_URL / .env.test.local,跳过认证集成测试";
        }
        context_ = std::make_unique<recta::storage::NeonContext>(*cs);
        service_ = std::make_unique<recta::core::AuthService>(*context_);
    }

    static void TearDownTestSuite() {
        service_.reset();
        context_.reset();
    }

    void SetUp() override {
        ResetTables();
        secretary_temp_ = service_->BootstrapFirstSecretary("secretary", "团支书");
    }

    static void ResetTables() {
        context_->ExecuteTransaction([](pqxx::work& tx) {
            tx.exec("TRUNCATE account_ledger_entries, inflow_records, expense_splits, "
                    "expense_requests, accounts, users RESTART IDENTITY CASCADE");
            return 0;
        });
    }

    static std::unique_ptr<recta::storage::NeonContext> context_;
    static std::unique_ptr<recta::core::AuthService> service_;
    std::string secretary_temp_;
};

std::unique_ptr<recta::storage::NeonContext> AuthServiceTest::context_;
std::unique_ptr<recta::core::AuthService> AuthServiceTest::service_;

TEST_F(AuthServiceTest, BootstrapAndLoginLifecycle) {
    // 引导仅一次。
    EXPECT_THROW((void)service_->BootstrapFirstSecretary("again", "冒名者"), std::runtime_error);

    EXPECT_THROW((void)service_->Login("secretary", "not-the-password"),
                 recta::core::AuthenticationException);
    EXPECT_THROW((void)service_->Login("ghost", secretary_temp_),
                 recta::core::AuthenticationException);

    const auto session = service_->Login("secretary", secretary_temp_);
    EXPECT_EQ(session.user_id, "u_sec_01");
    EXPECT_EQ(session.role, "BRANCH_SECRETARY");
    EXPECT_EQ(session.display_name, "团支书");
    EXPECT_TRUE(session.must_change_password); // 首登强制改密

    // 改密:旧密码错、弱口令均被拒;正确改密后标志解除。
    EXPECT_THROW((void)service_->ChangePassword("u_sec_01", "wrong-old", "NewPassw0rd!"),
                 recta::core::AuthenticationException);
    EXPECT_THROW((void)service_->ChangePassword("u_sec_01", secretary_temp_, "short"),
                 recta::core::WeakPasswordException);
    service_->ChangePassword("u_sec_01", secretary_temp_, "NewPassw0rd!");

    const auto again = service_->Login("secretary", "NewPassw0rd!");
    EXPECT_FALSE(again.must_change_password);
    EXPECT_THROW((void)service_->Login("secretary", secretary_temp_), // 旧临时密码已作废
                 recta::core::AuthenticationException);

    // last_login 已触达。
    const auto row = context_->ExecuteTransaction(
        [](pqxx::work& tx) { return recta::storage::UsersRepo::FindById(tx, "u_sec_01"); });
    ASSERT_TRUE(row.has_value());
    ASSERT_TRUE(row->last_login_at.has_value());
}

TEST_F(AuthServiceTest, SecretaryCreatesCommitteeUsers) {
    const std::string temp = service_->CreateUser("u_sec_01", "u_tech_01", "tech", "科技委员",
                                                  "CLASS_COMMITTEE");
    EXPECT_EQ(temp.size(), 12U);

    const auto session = service_->Login("tech", temp);
    EXPECT_EQ(session.role, "CLASS_COMMITTEE");
    EXPECT_TRUE(session.must_change_password);

    EXPECT_THROW((void)service_->CreateUser("u_sec_01", "u_tech_02", "tech", "重名者", "CLASS_COMMITTEE"),
                 std::invalid_argument); // 用户名唯一
    EXPECT_THROW((void)service_->CreateUser("u_sec_01", "u_sec_02", "sec2", "第二团支书",
                                            "BRANCH_SECRETARY"),
                 std::invalid_argument); // 席位唯一
    EXPECT_THROW((void)service_->CreateUser("u_sec_01", "u_bad_01", "bad", "非法角色", "PRESIDENT"),
                 std::invalid_argument);
    EXPECT_THROW((void)service_->CreateUser("ghost_actor", "u_x_01", "x", "幽灵操作者",
                                            "CLASS_COMMITTEE"),
                 recta::core::AuthenticationException);
    EXPECT_THROW((void)service_->CreateUser("u_sec_01", "", "", "", "CLASS_COMMITTEE"),
                 std::invalid_argument);
}

TEST_F(AuthServiceTest, LifeCommitteeBindsFacultyAccount) {
    const std::string temp = service_->CreateUser("u_sec_01", "u_life_01", "life", "生活委员",
                                                  "LIFE_COMMITTEE");
    EXPECT_NO_THROW((void)service_->Login("life", temp));

    const auto accounts = context_->ExecuteTransaction(
        [](pqxx::work& tx) { return recta::storage::EntityAccountsRepo::ListAll(tx); });
    ASSERT_EQ(accounts.size(), 2U);

    bool flexible_ok = false;
    bool faculty_ok = false;
    for (const auto& account : accounts) {
        if (account.type == "FLEXIBLE_PUBLIC") {
            flexible_ok = account.custodian_id == "u_sec_01" && account.balance_cents == 0;
        } else if (account.type == "FACULTY_REIMBURSE") {
            faculty_ok = account.custodian_id == "u_life_01" && account.balance_cents == 0;
        }
    }
    EXPECT_TRUE(flexible_ok) << "灵活公款应存管于团支书";
    EXPECT_TRUE(faculty_ok) << "系报销暂挂应存管于生活委员";

    // 生活委员席位唯一。
    EXPECT_THROW((void)service_->CreateUser("u_sec_01", "u_life_02", "life2", "第二生活委员",
                                            "LIFE_COMMITTEE"),
                 std::invalid_argument);
}

TEST_F(AuthServiceTest, PermissionAndMaintenance) {
    const std::string tech_temp = service_->CreateUser("u_sec_01", "u_tech_01", "tech", "科技委员",
                                                       "CLASS_COMMITTEE");
    service_->ChangePassword("u_tech_01", tech_temp, "TechPass123");

    // 账号管理仅团支书(§5.1)。
    EXPECT_THROW((void)service_->CreateUser("u_tech_01", "u_x_01", "x", "越权开号", "CLASS_COMMITTEE"),
                 recta::PermissionDeniedException);
    EXPECT_THROW((void)service_->ResetPassword("u_tech_01", "u_sec_01"),
                 recta::PermissionDeniedException);
    EXPECT_THROW((void)service_->DeactivateUser("u_tech_01", "u_sec_01"),
                 recta::PermissionDeniedException);

    // 重置他人临时密码。
    const std::string reset_temp = service_->ResetPassword("u_sec_01", "u_tech_01");
    const auto session = service_->Login("tech", reset_temp);
    EXPECT_TRUE(session.must_change_password);
    EXPECT_THROW((void)service_->ResetPassword("u_sec_01", "u_sec_01"), std::invalid_argument);

    // 停用后登录被拒。
    service_->DeactivateUser("u_sec_01", "u_tech_01");
    EXPECT_THROW((void)service_->Login("tech", reset_temp), recta::core::AuthenticationException);
    EXPECT_THROW((void)service_->DeactivateUser("u_sec_01", "u_sec_01"), std::invalid_argument);

    // 改名(用户名不可变,仅 display_name)。
    service_->UpdateDisplayName("u_sec_01", "u_tech_01", "科技委员(改名)");
    const auto renamed = context_->ExecuteTransaction(
        [](pqxx::work& tx) { return recta::storage::UsersRepo::FindById(tx, "u_tech_01"); });
    ASSERT_TRUE(renamed.has_value());
    EXPECT_EQ(renamed->display_name, "科技委员(改名)");
    EXPECT_EQ(renamed->username, "tech"); // 固定用户名不受改名影响
}

} // namespace
