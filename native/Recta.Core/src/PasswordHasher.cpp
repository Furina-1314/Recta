#include "recta/core/PasswordHasher.hpp"

#include <sodium.h>

#include <array>
#include <mutex>
#include <stdexcept>
#include <string_view>

namespace recta::core {
namespace {

void EnsureSodium() {
    static std::once_flag flag;
    std::call_once(flag, [] {
        if (sodium_init() < 0) throw std::runtime_error("libsodium 初始化失败");
    });
}

// 57 个无歧义字符:剔除 0/O/1/l/I。
constexpr std::string_view kAlphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz";

} // namespace

std::string PasswordHasher::Hash(const std::string& password) {
    EnsureSodium();
    std::array<char, crypto_pwhash_STRBYTES> hashed{};
    if (crypto_pwhash_str(hashed.data(), password.data(), password.size(),
                          crypto_pwhash_OPSLIMIT_INTERACTIVE,
                          crypto_pwhash_MEMLIMIT_INTERACTIVE) != 0) {
        throw std::runtime_error("Argon2id 哈希失败(内存不足)");
    }
    return std::string(hashed.data());
}

bool PasswordHasher::Verify(const std::string& hash, const std::string& password) {
    EnsureSodium();
    return crypto_pwhash_str_verify(hash.c_str(), password.data(), password.size()) == 0;
}

std::string PasswordHasher::GenerateTempPassword(std::size_t length) {
    EnsureSodium();
    if (length == 0) throw std::invalid_argument("临时口令长度必须为正");

    // 256 % 57 = 28:对随机字节做拒绝采样,保证均匀无偏。
    constexpr unsigned kMaxUsable = 256 - (256 % kAlphabet.size());
    std::string password;
    password.reserve(length);
    while (password.size() < length) {
        std::array<unsigned char, 32> bytes{};
        randombytes_buf(bytes.data(), bytes.size());
        for (const unsigned char byte : bytes) {
            if (byte >= kMaxUsable) continue;
            password.push_back(kAlphabet[byte % kAlphabet.size()]);
            if (password.size() == length) break;
        }
    }
    return password;
}

} // namespace recta::core
