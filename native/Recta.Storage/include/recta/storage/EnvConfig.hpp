#pragma once
// 连接串装载:.env.local 是本机秘密,绝不入库、绝不进日志。
#include <optional>
#include <string>

namespace recta::storage {

// 优先级:环境变量 DATABASE_URL > RECTA_ENV_FILE 指定文件 > 自当前目录逐级向上寻找 .env.local。
// 找不到任何来源时抛 std::runtime_error。
[[nodiscard]] std::string LoadConnectionString();

// 集成测试专用:优先 RECTA_TEST_DATABASE_URL 环境变量,再向上寻找 .env.test.local。
// 两者皆无时返回 nullopt(调用方应 GTEST_SKIP,而不是失败)。
[[nodiscard]] std::optional<std::string> TryLoadTestConnectionString();

} // namespace recta::storage
