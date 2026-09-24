#pragma once
// Recta 领域内核 —— 强类型定点金额。
// 唯一内部状态:整数“分”(int64_t),支持负数(透支)。
// 全局禁止浮点:任何接口不得引入 float/double(Vibe.md §2/§8.1)。
#include <cstdint>
#include <cstdlib>
#include <format>
#include <limits>
#include <stdexcept>
#include <string>
#include <string_view>
#include <compare>

namespace recta {

class Money {
private:
    int64_t cents_{0}; // 唯一内部状态：整数分，支持负数

    [[nodiscard]] static constexpr int64_t CheckedAdd(int64_t a, int64_t b) {
        // constexpr 上下文中一旦触达 throw 即编译期报错——恰好把不变式检查延伸到编译期。
        if (b > 0 && a > std::numeric_limits<int64_t>::max() - b) {
            throw std::overflow_error("Money 加法溢出");
        }
        if (b < 0 && a < std::numeric_limits<int64_t>::min() - b) {
            throw std::overflow_error("Money 加法下溢");
        }
        return a + b;
    }

    [[nodiscard]] static constexpr int64_t CheckedSub(int64_t a, int64_t b) {
        if (b < 0 && a > std::numeric_limits<int64_t>::max() + b) {
            throw std::overflow_error("Money 减法溢出");
        }
        if (b > 0 && a < std::numeric_limits<int64_t>::min() + b) {
            throw std::overflow_error("Money 减法下溢");
        }
        return a - b;
    }

    [[nodiscard]] static constexpr int64_t CheckedMul(int64_t a, int64_t b) {
        if (a == 0 || b == 0) return 0;
        if (a > 0 && b > 0 && a > std::numeric_limits<int64_t>::max() / b) {
            throw std::overflow_error("Money 标量乘法溢出");
        }
        if (a > 0 && b < 0 && b < std::numeric_limits<int64_t>::min() / a) {
            throw std::overflow_error("Money 标量乘法下溢");
        }
        if (a < 0 && b > 0 && a < std::numeric_limits<int64_t>::min() / b) {
            throw std::overflow_error("Money 标量乘法下溢");
        }
        if (a < 0 && b < 0 && a < std::numeric_limits<int64_t>::max() / b) {
            throw std::overflow_error("Money 标量乘法溢出");
        }
        return a * b;
    }

public:
    constexpr Money() noexcept = default;
    constexpr explicit Money(int64_t cents) noexcept : cents_(cents) {}

    // 便捷工厂:从“元”字符串构造,仅接受形如 "-123.45" / "123.45" / "123" 的定点表示。
    [[nodiscard]] static Money parse(std::string_view text);

    [[nodiscard]] constexpr int64_t to_cents() const noexcept { return cents_; }
    [[nodiscard]] constexpr bool is_negative() const noexcept { return cents_ < 0; }
    [[nodiscard]] constexpr bool is_zero() const noexcept { return cents_ == 0; }

    [[nodiscard]] Money abs() const {
        if (cents_ == std::numeric_limits<int64_t>::min()) {
            throw std::overflow_error("Money 绝对值溢出");
        }
        return Money(cents_ < 0 ? -cents_ : cents_);
    }

    [[nodiscard]] std::string to_plain_string() const {
        if (cents_ == std::numeric_limits<int64_t>::min()) {
            throw std::overflow_error("Money 格式化溢出");
        }
        const int64_t abs_val = std::abs(cents_);
        const std::string sign = (cents_ < 0) ? "-" : "";
        return std::format("{}{}.{:02d}", sign, abs_val / 100, abs_val % 100);
    }

    constexpr Money operator+(Money rhs) const { return Money(CheckedAdd(cents_, rhs.cents_)); }
    constexpr Money operator-(Money rhs) const { return Money(CheckedSub(cents_, rhs.cents_)); }
    constexpr Money operator-() const { return Money(CheckedSub(0, cents_)); }
    constexpr Money operator*(int64_t scalar) const { return Money(CheckedMul(cents_, scalar)); }
    Money operator*(Money rhs) const = delete; // 严禁金额相乘
    Money operator/(Money rhs) const = delete; // 金额除法只允许经由 DistributeExpense 欧几里得分解

    constexpr Money& operator+=(Money rhs) { cents_ = CheckedAdd(cents_, rhs.cents_); return *this; }
    constexpr Money& operator-=(Money rhs) { cents_ = CheckedSub(cents_, rhs.cents_); return *this; }

    constexpr auto operator<=>(const Money& rhs) const noexcept = default;
};

} // namespace recta
