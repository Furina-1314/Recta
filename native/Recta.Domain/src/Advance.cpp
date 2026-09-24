#include "recta/Advance.hpp"

namespace recta {

Money ComputeAdvanceDelta(Money balance_before, Money deduction) {
    if (deduction.to_cents() <= 0) {
        throw std::invalid_argument("办结扣款金额必须为正数");
    }

    const Money balance_after = balance_before - deduction; // Money 自带溢出检查

    if (balance_after.to_cents() >= 0) {
        // 扣减原有存款,未动用生委垫资。
        return Money(0);
    }
    if (balance_before.to_cents() >= 0) {
        // 存款扣尽,新产生透支:垫资 = |b_new|。
        return balance_after.abs();
    }
    // 原本已透支,本次扣款全额由生委垫资。
    return deduction;
}

} // namespace recta
