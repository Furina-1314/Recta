#include "recta/Roles.hpp"

#include <format>

namespace recta {

bool CanReview(Role role, AccountCategory category) {
    switch (category) {
    case AccountCategory::Flexible:
        return role == Role::BranchSecretary;                    // 团支书唯一
    case AccountCategory::Faculty:
        return role == Role::LifeCommittee;                      // 生活委员唯一
    case AccountCategory::ClassFund:
        return role == Role::BranchSecretary || role == Role::LifeCommittee;
    }
    return false;
}

bool CanSettle(Role role, AccountCategory category) {
    switch (category) {
    case AccountCategory::Flexible:
        return role == Role::BranchSecretary;                    // 团支书唯一
    case AccountCategory::Faculty:
        return role == Role::LifeCommittee;                      // 生活委员唯一
    case AccountCategory::ClassFund:
        return role == Role::LifeCommittee;                      // 办结强制校验生活委员
    }
    return false;
}

namespace {
std::string Describe(Role role, AccountCategory category, std::string_view phase) {
    return std::format("权限拒绝：{} 无权{}{}单据",
                       ToString(role), phase, ToString(category));
}
} // namespace

void AssertCanReview(Role role, AccountCategory category) {
    if (!CanReview(role, category)) {
        throw PermissionDeniedException(Describe(role, category, "审批"));
    }
}

void AssertCanSettle(Role role, AccountCategory category) {
    if (!CanSettle(role, category)) {
        throw PermissionDeniedException(Describe(role, category, "办结"));
    }
}

std::string ToString(Role value) {
    switch (value) {
    case Role::BranchSecretary: return "BRANCH_SECRETARY";
    case Role::LifeCommittee: return "LIFE_COMMITTEE";
    case Role::ClassCommittee: return "CLASS_COMMITTEE";
    }
    return "UNKNOWN_ROLE";
}

std::string ToString(AccountCategory value) {
    switch (value) {
    case AccountCategory::Flexible: return "FLEXIBLE";
    case AccountCategory::Faculty: return "FACULTY";
    case AccountCategory::ClassFund: return "CLASS_FUND";
    }
    return "UNKNOWN_ACCOUNT_CATEGORY";
}

std::string ToString(RequestStatus value) {
    switch (value) {
    case RequestStatus::PendingReview: return "PENDING_REVIEW";
    case RequestStatus::Approved: return "APPROVED";
    case RequestStatus::Settled: return "SETTLED";
    case RequestStatus::Rejected: return "REJECTED";
    }
    return "UNKNOWN_REQUEST_STATUS";
}

std::string ToString(InflowDestination value) {
    switch (value) {
    case InflowDestination::ToFlexibleAccount: return "TO_FLEXIBLE_ACCOUNT";
    case InflowDestination::ToFacultyReimburse: return "TO_FACULTY_REIMBURSE";
    case InflowDestination::ToStudentSubAccount: return "TO_STUDENT_SUB_ACCOUNT";
    }
    return "UNKNOWN_INFLOW_DESTINATION";
}

} // namespace recta
