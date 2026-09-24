#include "recta/storage/EnvConfig.hpp"

#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <optional>
#include <string_view>
#include <system_error>

namespace recta::storage {
namespace {

#if defined(_MSC_VER)
#pragma warning(push)
#pragma warning(disable : 4996) // getenv:无跨平台安全替代,此处仅读非关键配置来源
#endif

std::string_view Trim(std::string_view text) {
    while (!text.empty() && (text.front() == ' ' || text.front() == '\t')) text.remove_prefix(1);
    while (!text.empty() && (text.back() == ' ' || text.back() == '\t')) text.remove_suffix(1);
    return text;
}

// 行格式:KEY="value" / KEY=value / KEY='value';# 开头为注释;兼容 CRLF。
std::optional<std::string> ExtractKey(const std::filesystem::path& file, std::string_view key) {
    std::ifstream in(file, std::ios::binary);
    if (!in) return std::nullopt;

    std::string line;
    while (std::getline(in, line)) {
        if (!line.empty() && line.back() == '\r') line.pop_back();
        const auto trimmed = Trim(line);
        if (trimmed.empty() || trimmed.front() == '#') continue;

        const auto eq = trimmed.find('=');
        if (eq == std::string_view::npos) continue;
        if (Trim(trimmed.substr(0, eq)) != key) continue;

        auto value = std::string(Trim(trimmed.substr(eq + 1)));
        if (value.size() >= 2) {
            const char first = value.front();
            const char last = value.back();
            if ((first == '"' && last == '"') || (first == '\'' && last == '\'')) {
                value = value.substr(1, value.size() - 2);
            }
        }
        return value;
    }
    return std::nullopt;
}

std::filesystem::path FindEnvFile(std::string_view filename) {
    if (const char* override_path = std::getenv("RECTA_ENV_FILE"); override_path && *override_path) {
        return override_path;
    }
    namespace fs = std::filesystem;
    std::error_code ec;
    for (fs::path dir = fs::current_path(ec); !ec; dir = dir.parent_path()) {
        const fs::path candidate = dir / filename;
        if (fs::exists(candidate, ec)) return candidate;
        if (!dir.has_relative_path() || dir.parent_path() == dir) break;
    }
    return {};
}

} // namespace

std::string LoadConnectionString() {
    if (const char* env = std::getenv("DATABASE_URL"); env && *env) return env;

    const auto file = FindEnvFile(".env.local");
    if (!file.empty()) {
        if (auto value = ExtractKey(file, "DATABASE_URL")) return *value;
    }
    throw std::runtime_error("未找到数据库连接串：请设置 DATABASE_URL 环境变量，或在仓库根目录提供 .env.local");
}

std::optional<std::string> TryLoadTestConnectionString() {
    if (const char* env = std::getenv("RECTA_TEST_DATABASE_URL"); env && *env) return env;

    const auto file = FindEnvFile(".env.test.local");
    if (!file.empty()) {
        return ExtractKey(file, "DATABASE_URL");
    }
    return std::nullopt;
}

#if defined(_MSC_VER)
#pragma warning(pop)
#endif

} // namespace recta::storage
