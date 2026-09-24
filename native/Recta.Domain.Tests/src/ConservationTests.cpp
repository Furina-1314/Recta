#include "recta/Conservation.hpp"

#include <gtest/gtest.h>

namespace {

TEST(ConservationTest, MixedBalancesConserve) {
    // {+100, -40, +60}:Σb=120,C_cash=160,A=40 ⇒ 160−40=120 ✅
    const auto totals = recta::ComputeCustodyTotals(
        {recta::Money(10000), recta::Money(-4000), recta::Money(6000)});
    EXPECT_EQ(totals.balances_sum.to_cents(), 12000);
    EXPECT_EQ(totals.custodian_cash.to_cents(), 16000);
    EXPECT_EQ(totals.advance_total.to_cents(), 4000);
    EXPECT_NO_THROW(recta::VerifyConservation({recta::Money(10000), recta::Money(-4000), recta::Money(6000)}));
}

TEST(ConservationTest, EdgeShapes) {
    EXPECT_NO_THROW(recta::VerifyConservation({}));                                     // 空名单
    EXPECT_NO_THROW(recta::VerifyConservation({recta::Money(0), recta::Money(0)}));     // 全零
    EXPECT_NO_THROW(recta::VerifyConservation({recta::Money(50), recta::Money(25)}));   // 全正
    EXPECT_NO_THROW(recta::VerifyConservation({recta::Money(-50), recta::Money(-25)})); // 全负(全额垫资)

    const auto all_pos = recta::ComputeCustodyTotals({recta::Money(50), recta::Money(25)});
    EXPECT_EQ(all_pos.advance_total.to_cents(), 0);
    const auto all_neg = recta::ComputeCustodyTotals({recta::Money(-50), recta::Money(-25)});
    EXPECT_EQ(all_neg.custodian_cash.to_cents(), 0);
}

TEST(ConservationTest, IsConservedTripleCheck) {
    EXPECT_TRUE(recta::IsConserved(recta::Money(12000), recta::Money(16000), recta::Money(4000)));
    EXPECT_FALSE(recta::IsConserved(recta::Money(12000), recta::Money(16000), recta::Money(4001)));
    EXPECT_TRUE(recta::IsConserved(recta::Money(-1000), recta::Money(0), recta::Money(1000)));
}

} // namespace
