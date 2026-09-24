#include "recta/Split.hpp"

#include <gtest/gtest.h>
#include <numeric>

namespace {

// Vibe.md §3 示例:140.00 元 / 7 人 = 人均 20.00 元,无尾差。
TEST(SplitTest, ExactDivisionNoRemainder) {
    const auto allocs = recta::DistributeExpense(
        recta::Money(14000), {"s1", "s2", "s3", "s4", "s5", "s6", "s7"}, "s1");
    ASSERT_EQ(allocs.size(), 7U);
    for (const auto& a : allocs) {
        EXPECT_EQ(a.amount.to_cents(), 2000);
    }
    EXPECT_TRUE(allocs[0].is_tail_bearer); // 即使无尾差,承担人标记仍保留
}

// 100.00 元 / 3 人:q=33.33 r=1 → 承担人 33.34,其余各 33.33;合计 100.00。
TEST(SplitTest, RemainderGoesToTailBearer) {
    const auto allocs = recta::DistributeExpense(recta::Money(10000), {"A", "B", "C"}, "B");
    ASSERT_EQ(allocs.size(), 3U);

    int64_t sum = 0;
    int bearers = 0;
    for (const auto& a : allocs) {
        sum += a.amount.to_cents();
        if (a.is_tail_bearer) {
            ++bearers;
            EXPECT_EQ(a.student_id, "B");
            EXPECT_EQ(a.amount.to_cents(), 3334);
        } else {
            EXPECT_EQ(a.amount.to_cents(), 3333);
        }
    }
    EXPECT_EQ(bearers, 1);
    EXPECT_EQ(sum, 10000);
}

// 尾差为 k-1 的极端情形:10.01 元 / 3 人 → q=3.33 r=2 → 承担人 3.35。
TEST(SplitTest, LargeRemainder) {
    const auto allocs = recta::DistributeExpense(recta::Money(1001), {"A", "B", "C"}, "C");
    EXPECT_EQ(allocs[2].amount.to_cents(), 335);
    EXPECT_EQ(allocs[0].amount.to_cents(), 333);
    EXPECT_EQ(allocs[1].amount.to_cents(), 333);
}

TEST(SplitTest, SinglePersonBearsEverything) {
    const auto allocs = recta::DistributeExpense(recta::Money(777), {"only"}, "only");
    ASSERT_EQ(allocs.size(), 1U);
    EXPECT_EQ(allocs[0].amount.to_cents(), 777);
    EXPECT_TRUE(allocs[0].is_tail_bearer);
}

TEST(SplitTest, ZeroTotalSplitsZero) {
    const auto allocs = recta::DistributeExpense(recta::Money(0), {"A", "B"}, "A");
    for (const auto& a : allocs) EXPECT_EQ(a.amount.to_cents(), 0);
}

TEST(SplitTest, InvalidInputsThrow) {
    EXPECT_THROW((void)recta::DistributeExpense(recta::Money(100), {}, "X"), std::invalid_argument);
    EXPECT_THROW((void)recta::DistributeExpense(recta::Money(100), {"A"}, "X"), std::invalid_argument); // 承担人不在名单
    EXPECT_THROW((void)recta::DistributeExpense(recta::Money(100), {"A", "A"}, "A"), std::invalid_argument); // 重复学号
    EXPECT_THROW((void)recta::DistributeExpense(recta::Money(100), {"A", ""}, "A"), std::invalid_argument);  // 空学号
    EXPECT_THROW((void)recta::DistributeExpense(recta::Money(-1), {"A"}, "A"), std::invalid_argument);       // 负总额
}

// 随机参数化不变式:(q+r) + (k-1)·q ≡ M 恒成立。
TEST(SplitTest, InvariantHoldsAcrossSweep) {
    for (int64_t cents = 1; cents <= 5000; ++cents) {
        for (const std::size_t k : {std::size_t{1}, std::size_t{2}, std::size_t{3}, std::size_t{7}, std::size_t{11}}) {
            std::vector<std::string> ids;
            ids.reserve(k);
            for (std::size_t i = 0; i < k; ++i) ids.push_back("s" + std::to_string(i));
            const auto allocs = recta::DistributeExpense(recta::Money(cents), ids, ids.back());
            const int64_t sum = std::accumulate(
                allocs.begin(), allocs.end(), int64_t{0},
                [](int64_t acc, const recta::SplitAllocation& a) { return acc + a.amount.to_cents(); });
            ASSERT_EQ(sum, cents);
        }
    }
}

} // namespace
