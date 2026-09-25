#pragma once
// 预置驳回原因(Vibe.md §5.2 落地细化,2026-09-25 确认):
// 驳回时必选其一入库;统计按此精确分组,不随自由文本漂移。
// "其他"必须附带补充说明;其余原因补充说明可选。
#include <string>
#include <string_view>

namespace recta {

struct RejectCategory {
    std::string_view key;
    std::string_view label;
};

inline constexpr RejectCategory kRejectCategories[]{
    {"VOUCHER_INCOMPLETE", "票据凭证不全"},
    {"AMOUNT_WRONG", "金额有误"},
    {"NOT_CLASS_EXPENSE", "不属于班级支出"},
    {"OVER_BUDGET", "超出预算额度"},
    {"INFO_INCOMPLETE", "信息填写不完整"},
    {"DUPLICATE", "重复提单"},
    {"OTHER", "其他"},
};

inline constexpr std::string_view kRejectCategoryOther = "OTHER";

[[nodiscard]] inline bool IsValidRejectCategory(std::string_view key) {
    for (const auto& category : kRejectCategories) {
        if (category.key == key) return true;
    }
    return false;
}

// 未知键原样返回(防御历史数据),已知键返回中文标签。
[[nodiscard]] inline std::string RejectCategoryLabel(std::string_view key) {
    for (const auto& category : kRejectCategories) {
        if (category.key == key) return std::string(category.label);
    }
    return std::string(key);
}

} // namespace recta
