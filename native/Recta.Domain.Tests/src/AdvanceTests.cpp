#include "recta/Advance.hpp"

#include <gtest/gtest.h>

namespace {

// §4.2 三段判定逐例覆盖。
TEST(AdvanceTest, DeductFromPositiveBalance_NoAdvance) {
    // 结余充足:扣 30.00,余 70.00 → 垫资 0(Vibe.md §3 Inspector 示例中的"李四"情形)。
    EXPECT_EQ(recta::ComputeAdvanceDelta(recta::Money(10000), recta::Money(3000)).to_cents(), 0);
    EXPECT_EQ(recta::ComputeAdvanceDelta(recta::Money(3000), recta::Money(3000)).to_cents(), 0); // 恰好扣尽,b_new=0
}

TEST(AdvanceTest, ExhaustPositiveBalance_NewOverdraft) {
    // 存款扣尽:15.00 扣 20.00 → b_new=-5.00 → 垫资 5.00。
    EXPECT_EQ(recta::ComputeAdvanceDelta(recta::Money(1500), recta::Money(2000)).to_cents(), 500);
    EXPECT_EQ(recta::ComputeAdvanceDelta(recta::Money(0), recta::Money(700)).to_cents(), 700);
}

TEST(AdvanceTest, AlreadyOverdrawn_FullDeductionAdvanced) {
    // 原本透支:再扣全额由生委垫付("张三 -20.00"情形)。
    EXPECT_EQ(recta::ComputeAdvanceDelta(recta::Money(-2000), recta::Money(1000)).to_cents(), 1000);
    EXPECT_EQ(recta::ComputeAdvanceDelta(recta::Money(-2000), recta::Money(2000)).to_cents(), 2000);
}

TEST(AdvanceTest, InvalidDeductionThrows) {
    EXPECT_THROW((void)recta::ComputeAdvanceDelta(recta::Money(100), recta::Money(0)), std::invalid_argument);
    EXPECT_THROW((void)recta::ComputeAdvanceDelta(recta::Money(100), recta::Money(-1)), std::invalid_argument);
}

} // namespace
