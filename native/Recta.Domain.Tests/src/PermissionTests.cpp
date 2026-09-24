#include "recta/Roles.hpp"

#include <gtest/gtest.h>

namespace {

// §5.1 / §8.3 权限矩阵全枚举:3 角色 × 3 渠道 × 2 阶段 = 18 格逐格校验。
TEST(PermissionTest, ReviewMatrix) {
    using R = recta::Role;
    using C = recta::AccountCategory;

    // 灵活走账:团支书唯一
    EXPECT_TRUE (recta::CanReview(R::BranchSecretary, C::Flexible));
    EXPECT_FALSE(recta::CanReview(R::LifeCommittee,  C::Flexible));
    EXPECT_FALSE(recta::CanReview(R::ClassCommittee, C::Flexible));

    // 系报销:生活委员唯一
    EXPECT_FALSE(recta::CanReview(R::BranchSecretary, C::Faculty));
    EXPECT_TRUE (recta::CanReview(R::LifeCommittee,   C::Faculty));
    EXPECT_FALSE(recta::CanReview(R::ClassCommittee,  C::Faculty));

    // 班费:团支书或生活委员
    EXPECT_TRUE(recta::CanReview(R::BranchSecretary, C::ClassFund));
    EXPECT_TRUE(recta::CanReview(R::LifeCommittee,   C::ClassFund));
    EXPECT_FALSE(recta::CanReview(R::ClassCommittee, C::ClassFund));
}

TEST(PermissionTest, SettleMatrix) {
    using R = recta::Role;
    using C = recta::AccountCategory;

    EXPECT_TRUE (recta::CanSettle(R::BranchSecretary, C::Flexible));
    EXPECT_FALSE(recta::CanSettle(R::LifeCommittee,   C::Flexible));
    EXPECT_FALSE(recta::CanSettle(R::ClassCommittee,  C::Flexible));

    EXPECT_FALSE(recta::CanSettle(R::BranchSecretary, C::Faculty));
    EXPECT_TRUE (recta::CanSettle(R::LifeCommittee,   C::Faculty));
    EXPECT_FALSE(recta::CanSettle(R::ClassCommittee,  C::Faculty));

    // 班费办结强制生活委员——即使团支书已审批也不可办结。
    EXPECT_FALSE(recta::CanSettle(R::BranchSecretary, C::ClassFund));
    EXPECT_TRUE (recta::CanSettle(R::LifeCommittee,   C::ClassFund));
    EXPECT_FALSE(recta::CanSettle(R::ClassCommittee,  C::ClassFund));
}

TEST(PermissionTest, AssertThrowsPermissionDenied) {
    using R = recta::Role;
    using C = recta::AccountCategory;

    EXPECT_THROW(recta::AssertCanReview(R::LifeCommittee, C::Flexible), recta::PermissionDeniedException);
    EXPECT_THROW(recta::AssertCanSettle(R::BranchSecretary, C::ClassFund), recta::PermissionDeniedException);
    EXPECT_THROW(recta::AssertCanReview(R::ClassCommittee, C::ClassFund), recta::PermissionDeniedException);
    EXPECT_NO_THROW(recta::AssertCanSettle(R::LifeCommittee, C::ClassFund));
    EXPECT_NO_THROW(recta::AssertCanReview(R::BranchSecretary, C::ClassFund));

    try {
        recta::AssertCanReview(R::ClassCommittee, C::Flexible);
        FAIL() << "应当抛出 PermissionDeniedException";
    } catch (const recta::PermissionDeniedException& e) {
        EXPECT_NE(std::string(e.what()).find("权限拒绝"), std::string::npos);
    }
}

TEST(PermissionTest, EnumStringMappingMatchesDdl) {
    EXPECT_EQ(recta::ToString(recta::Role::BranchSecretary), "BRANCH_SECRETARY");
    EXPECT_EQ(recta::ToString(recta::Role::LifeCommittee), "LIFE_COMMITTEE");
    EXPECT_EQ(recta::ToString(recta::Role::ClassCommittee), "CLASS_COMMITTEE");

    EXPECT_EQ(recta::ToString(recta::AccountCategory::Flexible), "FLEXIBLE");
    EXPECT_EQ(recta::ToString(recta::AccountCategory::Faculty), "FACULTY");
    EXPECT_EQ(recta::ToString(recta::AccountCategory::ClassFund), "CLASS_FUND");

    EXPECT_EQ(recta::ToString(recta::RequestStatus::PendingReview), "PENDING_REVIEW");
    EXPECT_EQ(recta::ToString(recta::RequestStatus::Approved), "APPROVED");
    EXPECT_EQ(recta::ToString(recta::RequestStatus::Settled), "SETTLED");
    EXPECT_EQ(recta::ToString(recta::RequestStatus::Rejected), "REJECTED");

    EXPECT_EQ(recta::ToString(recta::InflowDestination::ToFlexibleAccount), "TO_FLEXIBLE_ACCOUNT");
    EXPECT_EQ(recta::ToString(recta::InflowDestination::ToFacultyReimburse), "TO_FACULTY_REIMBURSE");
    EXPECT_EQ(recta::ToString(recta::InflowDestination::ToStudentSubAccount), "TO_STUDENT_SUB_ACCOUNT");
}

} // namespace
