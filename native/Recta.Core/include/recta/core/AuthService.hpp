#pragma once
// 身份认证与账号管理服务(Vibe.md §5.1 RBAC 的服务层落点)。
//   · 登录:Argon2id 校验 + 停用拦截 + last_login 触达
//   · 首登强制改密(must_change_password),改密后方可正常使用
//   · 团支书专属:开立账号、指定固定用户名、分配角色、重置临时密码、停用
//   · 席位唯一:团支书/生活委员活动席位各一,创建时强校验
#include "recta/storage/NeonContext.hpp"
#include "recta/storage/Rows.hpp"

#include <stdexcept>
#include <string>

namespace recta::core {

class AuthenticationException final : public std::runtime_error {
public:
    explicit AuthenticationException(const std::string& message)
        : std::runtime_error(message) {}
};

class WeakPasswordException final : public std::invalid_argument {
public:
    explicit WeakPasswordException(const std::string& message)
        : std::invalid_argument(message) {}
};

struct LoginSession {
    std::string user_id;
    std::string username;
    std::string display_name;
    std::string role;
    bool must_change_password = false;
};

class AuthService {
public:
    explicit AuthService(storage::NeonContext& context) : context_(context) {}

    // 失败一律抛 AuthenticationException,消息不区分"用户不存在/密码错/已停用"。
    [[nodiscard]] LoginSession Login(const std::string& username, const std::string& password);

    void ChangePassword(const std::string& user_id, const std::string& old_password,
                        const std::string& new_password);

    // ---- 团支书专属(§5.1 最高管理)----
    // 开立账号并返回一次性明文临时密码(仅此一次返回,由团支书线下转交)。
    [[nodiscard]] std::string CreateUser(const std::string& actor_id, const std::string& new_user_id,
                                         const std::string& username, const std::string& display_name,
                                         const std::string& role_name);
    // 重置他人密码为新的临时密码,置 must_change_password。
    [[nodiscard]] std::string ResetPassword(const std::string& actor_id, const std::string& target_user_id);
    void DeactivateUser(const std::string& actor_id, const std::string& target_user_id);
    void UpdateDisplayName(const std::string& actor_id, const std::string& target_user_id,
                           const std::string& display_name);

    // 一次性引导:仅当 users 表为空时创建首任团支书(固定 id=u_sec_01),返回临时密码。
    [[nodiscard]] std::string BootstrapFirstSecretary(const std::string& username,
                                                      const std::string& display_name);

private:
    storage::NeonContext& context_;
};

} // namespace recta::core
