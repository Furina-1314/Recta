#include "recta/Money.hpp"

#include <gtest/gtest.h>
#include <limits>

namespace {

// 金额相乘/相除已被 delete(Vibe.md §7.1):正常代码中调用即编译错误,
// 无需运行期用例(MSVC 的 requires 探测对已删除函数报硬错误,故不写成 static_assert)。
static_assert(recta::Money(2500).to_cents() == 2500);

TEST(MoneyTest, FormatPlainString) {
    EXPECT_EQ(recta::Money(0).to_plain_string(), "0.00");
    EXPECT_EQ(recta::Money(5).to_plain_string(), "0.05");
    EXPECT_EQ(recta::Money(25).to_plain_string(), "0.25");
    EXPECT_EQ(recta::Money(2500).to_plain_string(), "25.00");
    EXPECT_EQ(recta::Money(14000).to_plain_string(), "140.00");
    EXPECT_EQ(recta::Money(4500001).to_plain_string(), "45000.01");
    EXPECT_EQ(recta::Money(-1).to_plain_string(), "-0.01");
    EXPECT_EQ(recta::Money(-2005).to_plain_string(), "-20.05");
}

TEST(MoneyTest, Arithmetic) {
    EXPECT_EQ((recta::Money(2500) + recta::Money(500)).to_cents(), 3000);
    EXPECT_EQ((recta::Money(2500) - recta::Money(3000)).to_cents(), -500);
    EXPECT_EQ((-recta::Money(2500)).to_cents(), -2500);
    EXPECT_EQ((recta::Money(20) * 7).to_cents(), 140);
    EXPECT_EQ((recta::Money(-20) * 3).to_cents(), -60);

    recta::Money acc(100);
    acc += recta::Money(50);
    acc -= recta::Money(200);
    EXPECT_EQ(acc.to_cents(), -50);
}

TEST(MoneyTest, Comparison) {
    EXPECT_TRUE(recta::Money(-1) < recta::Money(0));
    EXPECT_TRUE(recta::Money(0) < recta::Money(1));
    EXPECT_EQ(recta::Money(500), recta::Money(500));
    EXPECT_GT(recta::Money(2), recta::Money(1));
}

TEST(MoneyTest, OverflowGuards) {
    constexpr auto kMax = std::numeric_limits<int64_t>::max();
    EXPECT_THROW(recta::Money(kMax) + recta::Money(1), std::overflow_error);
    EXPECT_THROW(recta::Money(std::numeric_limits<int64_t>::min()) - recta::Money(1), std::overflow_error);
    EXPECT_THROW(recta::Money(kMax) * 2, std::overflow_error);
    EXPECT_THROW(recta::Money(std::numeric_limits<int64_t>::min()) + recta::Money(-1), std::overflow_error);
}

TEST(MoneyTest, Abs) {
    EXPECT_EQ(recta::Money(-450).abs().to_cents(), 450);
    EXPECT_EQ(recta::Money(450).abs().to_cents(), 450);
    EXPECT_EQ(recta::Money(0).abs().to_cents(), 0);
    EXPECT_EQ(recta::Money(-1).is_negative(), true);
    EXPECT_EQ(recta::Money(1).is_negative(), false);
}

TEST(MoneyTest, Parse) {
    EXPECT_EQ(recta::Money::parse("25.00").to_cents(), 2500);
    EXPECT_EQ(recta::Money::parse("25.5").to_cents(), 2550);
    EXPECT_EQ(recta::Money::parse("25").to_cents(), 2500);
    EXPECT_EQ(recta::Money::parse("-0.01").to_cents(), -1);
    EXPECT_EQ(recta::Money::parse("+140.00").to_cents(), 14000);
    EXPECT_EQ(recta::Money::parse("0").to_cents(), 0);

    EXPECT_THROW((void)recta::Money::parse(""), std::invalid_argument);
    EXPECT_THROW((void)recta::Money::parse("abc"), std::invalid_argument);
    EXPECT_THROW((void)recta::Money::parse("12.345"), std::invalid_argument);   // 3 位小数
    EXPECT_THROW((void)recta::Money::parse("1.2.3"), std::invalid_argument);
    EXPECT_THROW((void)recta::Money::parse("12."), std::invalid_argument);
    EXPECT_THROW((void)recta::Money::parse(".5"), std::invalid_argument);
    EXPECT_THROW((void)recta::Money::parse("1e3"), std::invalid_argument);      // 拒绝科学计数
    EXPECT_THROW((void)recta::Money::parse("--5"), std::invalid_argument);
}

TEST(MoneyTest, ParseRoundTripWithFormat) {
    for (const int64_t cents : {0LL, 1LL, 99LL, 100LL, 12345LL, -6789LL}) {
        const auto m = recta::Money(cents);
        EXPECT_EQ(recta::Money::parse(m.to_plain_string()).to_cents(), cents);
    }
}

} // namespace
