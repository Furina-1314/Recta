// 两阶段流转/办结原子闭环/入账引擎 集成测试(Neon recta-test 分支)。
// 标准环境:团支书 sec / 生活委员 life / 职能班委 tech / 同学 s1~s3(s1 预存 15.00 元)。
#include "recta/Money.hpp"
#include "recta/Roles.hpp"
#include "recta/core/AuthService.hpp"
#include "recta/core/WorkflowService.hpp"
#include "recta/storage/EntityAccountsRepo.hpp"
#include "recta/storage/EnvConfig.hpp"
#include "recta/storage/LedgerRepo.hpp"
#include "recta/storage/NeonContext.hpp"
#include "recta/storage/RequestRepo.hpp"
#include "recta/storage/StudentAccountsRepo.hpp"

#include <pqxx/pqxx>

#include <gtest/gtest.h>

#include <memory>
#include <string>

namespace {

class WorkflowTest : public ::testing::Test {
protected:
    static void SetUpTestSuite() {
        const auto cs = recta::storage::TryLoadTestConnectionString();
        if (!cs) {
            GTEST_SKIP() << "未配置 RECTA_TEST_DATABASE_URL / .env.test.local,跳过工作流集成测试";
        }
        context_ = std::make_unique<recta::storage::NeonContext>(*cs);
        auth_ = std::make_unique<recta::core::AuthService>(*context_);
        workflow_ = std::make_unique<recta::core::WorkflowService>(*context_);
        roster_ = std::make_unique<recta::core::RosterService>(*context_);
    }

    static void TearDownTestSuite() {
        roster_.reset();
        workflow_.reset();
        auth_.reset();
        context_.reset();
    }

    void SetUp() override {
        context_->ExecuteTransaction([](pqxx::work& tx) {
            tx.exec("TRUNCATE account_ledger_entries, inflow_records, expense_splits, "
                    "expense_requests, accounts, student_personal_accounts, users "
                    "RESTART IDENTITY CASCADE");
            return 0;
        });
        const std::string sec_temp = auth_->BootstrapFirstSecretary("sec", "团支书");
        const std::string life_temp =
            auth_->CreateUser("u_sec_01", "u_life_01", "life", "生活委员", "LIFE_COMMITTEE");
        const std::string tech_temp =
            auth_->CreateUser("u_sec_01", "u_tech_01", "tech", "科技委员", "CLASS_COMMITTEE");
        auth_->ChangePassword("u_tech_01", tech_temp, "TechPass123");
        (void)sec_temp;
        (void)life_temp;

        roster_->AddStudent("u_sec_01", "s1", "张三");
        roster_->AddStudent("u_sec_01", "s2", "李四");
        roster_->AddStudent("u_sec_01", "s3", "王五");
        // s1 预存 15.00 元(生活委员确认入账)。
        workflow_->RecordInflow("u_life_01",
                                {recta::InflowDestination::ToStudentSubAccount, recta::Money(1500),
                                 "张三班费预存", std::string("s1"), std::nullopt, std::nullopt});
    }

    static std::unique_ptr<recta::storage::NeonContext> context_;
    static std::unique_ptr<recta::core::AuthService> auth_;
    static std::unique_ptr<recta::core::WorkflowService> workflow_;
    static std::unique_ptr<recta::core::RosterService> roster_;
};

std::unique_ptr<recta::storage::NeonContext> WorkflowTest::context_;
std::unique_ptr<recta::core::AuthService> WorkflowTest::auth_;
std::unique_ptr<recta::core::WorkflowService> WorkflowTest::workflow_;
std::unique_ptr<recta::core::RosterService> WorkflowTest::roster_;

recta::core::SplitPlanInput Plan(std::vector<std::string> ids, std::string tail) {
    return {std::move(ids), std::move(tail)};
}

TEST_F(WorkflowTest, FlexibleChannelFullLifecycle) {
    // 增资 50.00 元(团支书)。
    workflow_->RecordInflow("u_sec_01",
                            {recta::InflowDestination::ToFlexibleAccount, recta::Money(5000),
                             "校运会评优奖励", std::nullopt, std::nullopt, std::nullopt});

    const int id = workflow_->SubmitRequest("u_tech_01", "团日文印费", recta::AccountCategory::Flexible,
                                            recta::Money(2400), std::nullopt);
    // 权限硬约束:生活委员不可审灵活走账。
    EXPECT_THROW((void)workflow_->ApproveRequest("u_life_01", id, recta::Money(2400), std::nullopt),
                 recta::PermissionDeniedException);

    workflow_->ApproveRequest("u_sec_01", id, recta::Money(2400), std::nullopt);
    // 团支书外不可办结。
    EXPECT_THROW((void)workflow_->SettleRequest("u_tech_01", id, std::nullopt),
                 recta::PermissionDeniedException);

    const auto result = workflow_->SettleRequest("u_sec_01", id, std::nullopt);
    EXPECT_EQ(result.settled_cents, 2400);
    EXPECT_EQ(result.category, "FLEXIBLE");
    EXPECT_TRUE(result.advances.empty());

    // 账户余额 50.00 − 24.00 = 26.00;单据状态与批复。
    const auto balance = context_->ExecuteTransaction([](pqxx::work& tx) {
        return recta::storage::EntityAccountsRepo::FindByType(tx, "FLEXIBLE_PUBLIC")
            ->balance_cents;
    });
    EXPECT_EQ(balance, 2600);

    const auto request = context_->ExecuteTransaction(
        [&](pqxx::work& tx) { return recta::storage::RequestRepo::Find(tx, id); });
    ASSERT_TRUE(request.has_value());
    EXPECT_EQ(request->status, "SETTLED");
    ASSERT_TRUE(request->settlement_notes.has_value());
    EXPECT_NE(request->settlement_notes->find("灵活公款"), std::string::npos);
    ASSERT_TRUE(request->settler_id.has_value());
    EXPECT_EQ(*request->settler_id, "u_sec_01");

    // 重复办结被状态机拦截。
    EXPECT_THROW((void)workflow_->SettleRequest("u_sec_01", id, std::nullopt),
                 recta::core::InvalidRequestStateException);
    // 重复审批同样拦截。
    EXPECT_THROW((void)workflow_->ApproveRequest("u_sec_01", id, recta::Money(1000), std::nullopt),
                 recta::core::InvalidRequestStateException);
}

TEST_F(WorkflowTest, FlexibleInsufficientBalanceRejected) {
    const int id = workflow_->SubmitRequest("u_tech_01", "大额采购", recta::AccountCategory::Flexible,
                                            recta::Money(9900), std::nullopt);
    workflow_->ApproveRequest("u_sec_01", id, recta::Money(9900), std::nullopt);
    EXPECT_THROW((void)workflow_->SettleRequest("u_sec_01", id, std::nullopt), std::runtime_error);

    // 失败后单据仍处 APPROVED,可待增资后再办结。
    const auto request = context_->ExecuteTransaction(
        [&](pqxx::work& tx) { return recta::storage::RequestRepo::Find(tx, id); });
    EXPECT_EQ(request->status, "APPROVED");
}

TEST_F(WorkflowTest, ClassFundSplitSettlementAdvancesAndConservation) {
    // 100.00 元 / 3 人,尾差 s3:q=33.33 r=1 → s3 扣 33.34。
    const int id = workflow_->SubmitRequest("u_tech_01", "实验耗材", recta::AccountCategory::ClassFund,
                                            recta::Money(10000), Plan({"s1", "s2", "s3"}, "s3"));
    // 班费两角色均可审;办结强制生活委员。
    EXPECT_THROW((void)workflow_->SettleRequest("u_sec_01", id, std::nullopt),
                 recta::PermissionDeniedException);

    workflow_->ApproveRequest("u_life_01", id, recta::Money(10000), std::nullopt);
    const auto result = workflow_->SettleRequest("u_life_01", id, std::nullopt);

    EXPECT_EQ(result.settled_cents, 10000);
    // 预期垫资:s1 15−33.33=−18.33→18.33;s2 0−33.33→33.33;s3 0−33.34→33.34;合计 85.00。
    ASSERT_EQ(result.advances.size(), 3U);
    EXPECT_EQ(result.total_advance_cents, 8500);
    EXPECT_EQ(result.balances_sum_after, -8500);       // 15.00 − 100.00
    EXPECT_EQ(result.custodian_cash_after, 0);
    EXPECT_EQ(result.advance_total_after, 8500);       // 100 − 15
    EXPECT_EQ(result.balances_sum_after,
              result.custodian_cash_after - result.advance_total_after); // Σb = C − A

    // 分户余额与回填的 Δadvance。
    const auto state = context_->ExecuteTransaction([](pqxx::work& tx) {
        struct State {
            std::vector<recta::storage::StudentAccountRow> students;
            std::vector<recta::storage::SplitRow> splits;
        } state;
        state.students = recta::storage::StudentAccountsRepo::ListAll(tx);
        state.splits = recta::storage::RequestRepo::ListSplits(tx, 1);
        return state;
    });
    ASSERT_EQ(state.students.size(), 3U);
    EXPECT_EQ(state.students[0].student_id, "s1");
    EXPECT_EQ(state.students[0].balance_cents, -1833);
    EXPECT_EQ(state.students[1].balance_cents, -3333);
    EXPECT_EQ(state.students[2].balance_cents, -3334);
    EXPECT_EQ(state.students[0].total_spent_cents, 3333);
    EXPECT_EQ(state.students[2].total_recharged_cents, 0);

    ASSERT_EQ(state.splits.size(), 3U);
    EXPECT_EQ(state.splits[0].advance_cents, 1833);
    EXPECT_EQ(state.splits[1].advance_cents, 3333);
    EXPECT_EQ(state.splits[2].advance_cents, 3334);
    EXPECT_TRUE(state.splits[2].is_tail_bearer);

    // 批复披露垫资明细(§1 原则 2)。
    const auto request = context_->ExecuteTransaction(
        [&](pqxx::work& tx) { return recta::storage::RequestRepo::Find(tx, id); });
    ASSERT_TRUE(request->settlement_notes.has_value());
    EXPECT_NE(request->settlement_notes->find("生委垫资 3 笔共 85.00 元"), std::string::npos);
    EXPECT_NE(request->settlement_notes->find("张三 18.33"), std::string::npos);
    EXPECT_NE(request->settlement_notes->find("王五 33.34"), std::string::npos);
}

TEST_F(WorkflowTest, ClassFundReductionRecomputesSplits) {
    // 申报 100.00/2 人尾差 s1 → 提单时 50.00/50.00。
    const int id = workflow_->SubmitRequest("u_tech_01", "实践车费", recta::AccountCategory::ClassFund,
                                            recta::Money(10000), Plan({"s1", "s2"}, "s1"));
    auto splits_before = context_->ExecuteTransaction(
        [&](pqxx::work& tx) { return recta::storage::RequestRepo::ListSplits(tx, id); });
    ASSERT_EQ(splits_before.size(), 2U);
    EXPECT_EQ(splits_before[0].amount_cents, 5000);
    EXPECT_EQ(splits_before[1].amount_cents, 5000);

    // 核减到 99.99 且未填核减理由 → 拒绝。
    EXPECT_THROW((void)workflow_->ApproveRequest("u_sec_01", id, recta::Money(9999), std::nullopt),
                 std::invalid_argument);
    // 核减金额超过申报 → 拒绝。
    EXPECT_THROW((void)workflow_->ApproveRequest("u_sec_01", id, recta::Money(10001), std::nullopt),
                 std::invalid_argument);

    workflow_->ApproveRequest("u_sec_01", id, recta::Money(9999), "核减 0.01 元尾差");
    auto splits_after = context_->ExecuteTransaction(
        [&](pqxx::work& tx) { return recta::storage::RequestRepo::ListSplits(tx, id); });
    // 99.99/2:q=49.99 r=1 → 尾差承担人 s1 扣 50.00。
    ASSERT_EQ(splits_after.size(), 2U);
    EXPECT_EQ(splits_after[0].student_id, "s1");
    EXPECT_EQ(splits_after[0].amount_cents, 5000);
    EXPECT_TRUE(splits_after[0].is_tail_bearer);
    EXPECT_EQ(splits_after[1].amount_cents, 4999);

    // 生活委员按核减后金额办结。
    const auto result = workflow_->SettleRequest("u_life_01", id, std::nullopt);
    EXPECT_EQ(result.settled_cents, 9999);
    // s1: 15−50=−35; s2: 0−49.99=−49.99 → 合计 −84.99 = 9999−1500。
    EXPECT_EQ(result.balances_sum_after, -8499);
    EXPECT_EQ(result.advance_total_after, 8499);
}

TEST_F(WorkflowTest, FacultyChannelHangingAndReimburse) {
    const int id = workflow_->SubmitRequest("u_tech_01", "实践打车费", recta::AccountCategory::Faculty,
                                            recta::Money(45000), std::nullopt);
    // 系报销仅生活委员可审。
    EXPECT_THROW((void)workflow_->ApproveRequest("u_sec_01", id, recta::Money(45000), std::nullopt),
                 recta::PermissionDeniedException);

    workflow_->ApproveRequest("u_life_01", id, recta::Money(45000), std::nullopt);
    const auto result = workflow_->SettleRequest("u_life_01", id, std::nullopt);
    EXPECT_EQ(result.settled_cents, 45000);

    // 挂账 +450.00。
    const auto hanging = context_->ExecuteTransaction([](pqxx::work& tx) {
        return recta::storage::EntityAccountsRepo::FindByType(tx, "FACULTY_REIMBURSE")
            ->balance_cents;
    });
    EXPECT_EQ(hanging, 45000);

    // 系财务打款 450.00 核销平账。
    workflow_->RecordInflow("u_life_01",
                            {recta::InflowDestination::ToFacultyReimburse, recta::Money(45000),
                             "系财务 9 月经费打款", std::nullopt, id, std::nullopt});
    const auto settled_balance = context_->ExecuteTransaction([](pqxx::work& tx) {
        return recta::storage::EntityAccountsRepo::FindByType(tx, "FACULTY_REIMBURSE")
            ->balance_cents;
    });
    EXPECT_EQ(settled_balance, 0);

    // 挂账已清零,再核销即拒;非生活委员亦不可核销。
    EXPECT_THROW(
        workflow_->RecordInflow("u_life_01",
                                {recta::InflowDestination::ToFacultyReimburse, recta::Money(100),
                                 "超额核销", std::nullopt, id, std::nullopt}),
        std::invalid_argument);
    EXPECT_THROW(
        workflow_->RecordInflow("u_sec_01",
                                {recta::InflowDestination::ToStudentSubAccount, recta::Money(100),
                                 "越权充值", std::string("s1"), std::nullopt, std::nullopt}),
        recta::PermissionDeniedException);
    EXPECT_THROW(
        workflow_->RecordInflow("u_life_01",
                                {recta::InflowDestination::ToFlexibleAccount, recta::Money(100),
                                 "越权增资", std::nullopt, std::nullopt, std::nullopt}),
        recta::PermissionDeniedException);
}

TEST_F(WorkflowTest, SubmitAndReviewValidations) {
    EXPECT_THROW((void)workflow_->SubmitRequest("u_tech_01", "", recta::AccountCategory::Flexible,
                                                recta::Money(100), std::nullopt),
                 std::invalid_argument);
    EXPECT_THROW((void)workflow_->SubmitRequest("u_tech_01", "零金额",
                                                recta::AccountCategory::Flexible, recta::Money(0),
                                                std::nullopt),
                 std::invalid_argument);
    EXPECT_THROW((void)workflow_->SubmitRequest("u_tech_01", "无名单班费",
                                                recta::AccountCategory::ClassFund,
                                                recta::Money(100), std::nullopt),
                 std::invalid_argument);
    EXPECT_THROW((void)workflow_->SubmitRequest("ghost", "幽灵提单",
                                                recta::AccountCategory::Flexible, recta::Money(100),
                                                std::nullopt),
                 recta::core::AuthenticationException);

    const int id = workflow_->SubmitRequest("u_tech_01", "待驳回事项",
                                            recta::AccountCategory::Flexible, recta::Money(1000),
                                            std::nullopt);
    // 预置原因必选;非法键拒绝;"其他"必须附补充说明;正常驳回落库键与说明。
    EXPECT_THROW((void)workflow_->RejectRequest("u_sec_01", id, "", ""), std::invalid_argument);
    EXPECT_THROW((void)workflow_->RejectRequest("u_sec_01", id, "MADE_UP_KEY", "x"),
                 std::invalid_argument);
    EXPECT_THROW((void)workflow_->RejectRequest("u_sec_01", id, "OTHER", ""),
                 std::invalid_argument);
    workflow_->RejectRequest("u_sec_01", id, "VOUCHER_INCOMPLETE", "补票后重新提单");
    const auto request = context_->ExecuteTransaction(
        [&](pqxx::work& tx) { return recta::storage::RequestRepo::Find(tx, id); });
    EXPECT_EQ(request->status, "REJECTED");
    ASSERT_TRUE(request->reject_category.has_value());
    EXPECT_EQ(*request->reject_category, "VOUCHER_INCOMPLETE");
    ASSERT_TRUE(request->review_notes.has_value());
    EXPECT_EQ(*request->review_notes, "补票后重新提单");

    // 驳回后不可再审批/办结(归档终止,需重新提单)。
    EXPECT_THROW((void)workflow_->ApproveRequest("u_sec_01", id, recta::Money(500), std::nullopt),
                 recta::core::InvalidRequestStateException);
}

TEST_F(WorkflowTest, RosterPermissionsAndRechargeRecovery) {
    EXPECT_THROW(roster_->AddStudent("u_life_01", "s9", "越权录入"),
                 recta::PermissionDeniedException);
    EXPECT_THROW(roster_->RenameStudent("u_life_01", "s1", "越权改名"),
                 recta::PermissionDeniedException);
    EXPECT_THROW(roster_->AddStudent("u_sec_01", "s1", "重复学号"), std::invalid_argument);

    // 透支同学补缴:直接平账、解除债权。
    const int id = workflow_->SubmitRequest("u_tech_01", "小额垫付",
                                            recta::AccountCategory::ClassFund, recta::Money(2000),
                                            Plan({"s1"}, "s1"));
    workflow_->ApproveRequest("u_life_01", id, recta::Money(2000), std::nullopt);
    const auto settled = workflow_->SettleRequest("u_life_01", id, std::nullopt);
    (void)settled;
    // s1: 15.00 − 20.00 = −5.00。
    const auto overdrawn = context_->ExecuteTransaction([](pqxx::work& tx) {
        return recta::storage::StudentAccountsRepo::Find(tx, "s1");
    });
    EXPECT_EQ(overdrawn->balance_cents, -500);

    workflow_->RecordInflow("u_life_01",
                            {recta::InflowDestination::ToStudentSubAccount, recta::Money(500),
                             "张三补缴欠款", std::string("s1"), std::nullopt, std::nullopt});
    const auto recovered = context_->ExecuteTransaction([](pqxx::work& tx) {
        return recta::storage::StudentAccountsRepo::Find(tx, "s1");
    });
    EXPECT_EQ(recovered->balance_cents, 0);
    EXPECT_EQ(recovered->total_recharged_cents, 2000); // 15.00 + 5.00

    // 不存在的分户不可充值。
    EXPECT_THROW(
        workflow_->RecordInflow("u_life_01",
                                {recta::InflowDestination::ToStudentSubAccount, recta::Money(100),
                                 "幽灵充值", std::string("ghost"), std::nullopt, std::nullopt}),
        std::invalid_argument);
}

} // namespace
