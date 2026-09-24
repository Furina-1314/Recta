#pragma once
// 尾差保全平摊算法(Vibe.md §4.3)。
// M = q·k + r (0 ≤ r < k);尾差承担人扣 q+r,其余 k-1 人各扣 q;
// 强校验不变式:(q+r) + (k-1)·q ≡ M。
#include "recta/Money.hpp"

#include <string>
#include <vector>

namespace recta {

struct SplitAllocation {
    std::string student_id;
    Money amount;
    bool is_tail_bearer;
};

// 欧几里得除法保全尾差平摊。
// total:核准出账金额(≥ 0);student_ids:参摊名单(非空且无重复);
// tail_bearer_id:指定尾差承担人(必须存在于名单)。
[[nodiscard]] std::vector<SplitAllocation> DistributeExpense(
    Money total,
    const std::vector<std::string>& student_ids,
    const std::string& tail_bearer_id);

} // namespace recta
