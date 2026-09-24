#include "recta/core/PasswordHasher.hpp"

#include <gtest/gtest.h>

#include <string>
#include <string_view>
#include <unordered_set>

namespace {

TEST(PasswordHasherTest, HashVerifyRoundTrip) {
    const std::string hash = recta::core::PasswordHasher::Hash("correct horse battery staple");
    EXPECT_NE(hash.find("$argon2id$"), std::string::npos); // Vibe.md 指定 Argon2id
    EXPECT_LT(hash.size(), 255U);                          // users.password_hash VARCHAR(255)
    EXPECT_TRUE(recta::core::PasswordHasher::Verify(hash, "correct horse battery staple"));
    EXPECT_FALSE(recta::core::PasswordHasher::Verify(hash, "wrong password"));
    EXPECT_FALSE(recta::core::PasswordHasher::Verify(hash, ""));
}

TEST(PasswordHasherTest, SaltedHashesDiffer) {
    const auto a = recta::core::PasswordHasher::Hash("same password");
    const auto b = recta::core::PasswordHasher::Hash("same password");
    EXPECT_NE(a, b); // 盐内建
    EXPECT_TRUE(recta::core::PasswordHasher::Verify(a, "same password"));
    EXPECT_TRUE(recta::core::PasswordHasher::Verify(b, "same password"));
}

TEST(PasswordHasherTest, TempPasswordGenerator) {
    const std::string pwd = recta::core::PasswordHasher::GenerateTempPassword();
    EXPECT_EQ(pwd.size(), 12U);

    const auto custom = recta::core::PasswordHasher::GenerateTempPassword(16);
    EXPECT_EQ(custom.size(), 16U);

    // 无歧义字母表:不出现 0/O/1/l/I。
    for (const char c : pwd + custom) {
        EXPECT_EQ(std::string_view("0O1lI").find(c), std::string_view::npos);
    }

    // 采样足够多次,临时口令互不相同且覆盖字母表(粗验 CSPRNG 生效)。
    // 注意:遍历字符集必须用 string_view,字面量会多带一个结尾 '\0'。
    constexpr std::string_view kFullAlphabet =
        "23456789abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ";
    std::unordered_set<std::string> seen;
    std::string all;
    for (int i = 0; i < 50; ++i) {
        const auto p = recta::core::PasswordHasher::GenerateTempPassword();
        seen.insert(p);
        all += p;
    }
    EXPECT_EQ(seen.size(), 50U);
    for (const char c : kFullAlphabet) {
        EXPECT_NE(all.find(c), std::string::npos) << "字母表字符从未出现: " << c;
    }

    EXPECT_THROW((void)recta::core::PasswordHasher::GenerateTempPassword(0), std::invalid_argument);
}

} // namespace
