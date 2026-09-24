#include "recta/Money.hpp"

#include <charconv>

namespace recta {

// 仅接受定点字符串:可选符号 + 元 + 可选(点 + 1~2 位小数)。
// 前端/接口传输层约定(Vibe.md §8.1):金额只能是 "25.00" 这类定点串或整数分。
Money Money::parse(std::string_view text) {
    if (text.empty()) throw std::invalid_argument("金额字符串为空");

    std::size_t pos = 0;
    bool negative = false;
    if (text[pos] == '+' || text[pos] == '-') {
        negative = (text[pos] == '-');
        ++pos;
    }

    const std::size_t int_begin = pos;
    while (pos < text.size() && text[pos] >= '0' && text[pos] <= '9') ++pos;
    const std::size_t int_len = pos - int_begin;
    if (int_len == 0 || int_len > 18) throw std::invalid_argument("金额整数部分非法: " + std::string(text));

    int64_t yuan = 0;
    if (std::from_chars(text.data() + int_begin, text.data() + int_begin + int_len, yuan).ec != std::errc{}) {
        throw std::invalid_argument("金额解析失败: " + std::string(text));
    }

    int64_t cents_fraction = 0;
    int fraction_digits = 0;
    if (pos < text.size()) {
        if (text[pos] != '.') throw std::invalid_argument("金额含非法字符: " + std::string(text));
        ++pos;
        const std::size_t frac_begin = pos;
        while (pos < text.size() && text[pos] >= '0' && text[pos] <= '9') ++pos;
        fraction_digits = static_cast<int>(pos - frac_begin);
        if (fraction_digits < 1 || fraction_digits > 2) {
            throw std::invalid_argument("金额小数位必须为 1~2 位: " + std::string(text));
        }
        cents_fraction = (text[frac_begin] - '0') * 10;
        if (fraction_digits == 2) cents_fraction += text[frac_begin + 1] - '0';
    }
    if (pos != text.size()) throw std::invalid_argument("金额含多余字符: " + std::string(text));

    // yuan*100 与符号合成均不可能溢出(整数部分已限 18 位)。
    const int64_t cents = negative
        ? -(yuan * 100 + cents_fraction)
        : (yuan * 100 + cents_fraction);
    return Money(cents);
}

} // namespace recta
