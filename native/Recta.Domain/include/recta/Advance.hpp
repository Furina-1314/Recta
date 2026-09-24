#pragma once
// 动账垫资判定算法(Vibe.md §4.2)。
// 办结扣除同学金额 w>0,扣除前余额 b_old,扣除后 b_new = b_old − w:
//   b_new ≥ 0                     → Δadvance = 0   (扣减原有存款)
//   b_old ≥ 0 且 b_new < 0        → Δadvance = |b_new| (存款扣尽,新产生透支)
//   b_old < 0                     → Δadvance = w   (已透支,全额由生委垫付)
#include "recta/Money.hpp"

namespace recta {

[[nodiscard]] Money ComputeAdvanceDelta(Money balance_before, Money deduction);

} // namespace recta
