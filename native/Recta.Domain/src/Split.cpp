#include "recta/Split.hpp"

#include <numeric>
#include <unordered_set>

namespace recta {

std::vector<SplitAllocation> DistributeExpense(
    Money total,
    const std::vector<std::string>& student_ids,
    const std::string& tail_bearer_id) {
    if (student_ids.empty()) throw std::invalid_argument("名单不能为空");
    if (total.to_cents() < 0) throw std::invalid_argument("平摊总额不能为负");

    std::unordered_set<std::string> seen;
    seen.reserve(student_ids.size());
    for (const auto& id : student_ids) {
        if (id.empty()) throw std::invalid_argument("名单存在空学号");
        if (!seen.insert(id).second) throw std::invalid_argument("名单存在重复学号: " + id);
    }
    if (!seen.contains(tail_bearer_id)) {
        throw std::invalid_argument("尾差承担人不在名单中: " + tail_bearer_id);
    }

    const int64_t total_cents = total.to_cents();
    const int64_t k = static_cast<int64_t>(student_ids.size());
    const int64_t q = total_cents / k; // 欧几里得除法:total ≥ 0, k > 0 ⇒ 商余非负
    const int64_t r = total_cents % k;

    std::vector<SplitAllocation> allocations;
    allocations.reserve(student_ids.size());

    int64_t verification_sum = 0;
    for (const auto& id : student_ids) {
        if (id == tail_bearer_id) {
            allocations.push_back({id, Money(q + r), true});
            verification_sum += (q + r);
        } else {
            allocations.push_back({id, Money(q), false});
            verification_sum += q;
        }
    }

    // 数学不变式断言(Vibe.md §4.3)
    if (verification_sum != total_cents) {
        throw std::logic_error("平摊校验失败：分项之和不等于总金额");
    }

    return allocations;
}

} // namespace recta
