#pragma once
// 全局对账守恒定律(Vibe.md §4.1)。
//   Σb_i = C_cash − A_advance
// 其中 C_cash = Σ_{b>0} b(生委代管实存),A_advance = Σ_{b<0}|b|(生委私人债权)。
#include "recta/Money.hpp"

#include <vector>

namespace recta {

struct CustodyTotals {
    Money balances_sum;    // Σb_i(全班虚拟账户余额代数和)
    Money custodian_cash;  // C_cash(恒 ≥ 0)
    Money advance_total;   // A_advance(恒 ≥ 0)
};

[[nodiscard]] CustodyTotals ComputeCustodyTotals(const std::vector<Money>& balances);

// 直接对三元组校验守恒(供存储层从数据库聚合值复核)。
[[nodiscard]] bool IsConserved(Money balances_sum, Money custodian_cash, Money advance_total);

// 守恒断言:violation 抛 std::logic_error(事务层植入的数学不变式)。
void VerifyConservation(const std::vector<Money>& balances);

} // namespace recta
