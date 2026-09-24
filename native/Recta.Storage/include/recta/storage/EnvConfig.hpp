#pragma once
// 连接串装载:.env.local 是本机秘密,绝不入库、绝不进日志。
#include <string>

namespace recta::storage {

// 优先级:环境变量 DATABASE_URL > RECTA_ENV_FILE 指定文件 > 自当前目录逐级向上寻找 .env.local。
// 找不到任何来源时抛 std::runtime_error。
[[nodiscard]] std::string LoadConnectionString();

} // namespace recta::storage
