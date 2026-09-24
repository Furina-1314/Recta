#pragma once
// 角色体系与两阶段权限硬约束(Vibe.md §5.1 / §8.3)。
//   灵活走账(FLEXIBLE): 审批/办结 → 团支书唯一
//   系报销(FACULTY):    审批/办结 → 生活委员唯一
//   班费(CLASS_FUND):   审批 → 团支书或生活委员;办结 → 生活委员唯一
#include <stdexcept>
#include <string>
#include <string_view>

namespace recta {

enum class Role {
    BranchSecretary,   // 团支书
    LifeCommittee,     // 生活委员
    ClassCommittee,    // 各职能班委
};

enum class AccountCategory {
    Flexible,   // 班级灵活走账公款
    Faculty,    // 系级报销往来暂挂
    ClassFund,  // 班费纯个人独立分户
};

enum class RequestStatus {
    PendingReview, // 待审理
    Approved,      // 已核准,待办结
    Settled,       // 已办结出账
    Rejected,      // 已驳回
};

enum class InflowDestination {
    ToFlexibleAccount,    // 灵活账户增资
    ToFacultyReimburse,   // 系报销回款核销
    ToStudentSubAccount,  // 同学班费补缴充值
};

// 领域层硬拦截:非法角色调用直接抛出,绝不返回错误码了事。
class PermissionDeniedException final : public std::runtime_error {
public:
    explicit PermissionDeniedException(const std::string& message)
        : std::runtime_error(message) {}
};

[[nodiscard]] bool CanReview(Role role, AccountCategory category);
[[nodiscard]] bool CanSettle(Role role, AccountCategory category);

void AssertCanReview(Role role, AccountCategory category);
void AssertCanSettle(Role role, AccountCategory category);

// ---- 数据库/传输字符串映射(与 Vibe.md §6 DDL 中的取值严格一致) ----
[[nodiscard]] std::string ToString(Role value);
[[nodiscard]] std::string ToString(AccountCategory value);
[[nodiscard]] std::string ToString(RequestStatus value);
[[nodiscard]] std::string ToString(InflowDestination value);

} // namespace recta
