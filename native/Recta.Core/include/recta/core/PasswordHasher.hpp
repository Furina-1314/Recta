#pragma once
// Argon2id 口令哈希(Vibe.md §6 users.password_hash)。
// libsodium crypto_pwhash_str:输出形如 "$argon2id$v=19$m=65536,t=2,p=1$...",长度 < 128,
// 满足 VARCHAR(255)。盐内建、常数时间比较。
#include <cstddef>
#include <string>

namespace recta::core {

class PasswordHasher {
public:
    [[nodiscard]] static std::string Hash(const std::string& password);
    [[nodiscard]] static bool Verify(const std::string& hash, const std::string& password);

    // 一次性临时口令:CSPRNG + 无歧义字母表(无 0/O/1/l/I),拒绝采样消模偏。
    [[nodiscard]] static std::string GenerateTempPassword(std::size_t length = 12);
};

} // namespace recta::core
