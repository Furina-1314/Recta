#include "recta/core/AuthService.hpp"

#include "recta/Roles.hpp"
#include "recta/core/PasswordHasher.hpp"
#include "recta/storage/EntityAccountsRepo.hpp"
#include "recta/storage/UsersRepo.hpp"

#include <pqxx/pqxx>

#include <optional>
#include <string>

namespace recta::core {
namespace {

constexpr int kMinPasswordLength = 8;
constexpr int kMaxPasswordLength = 64;

void ValidatePasswordPolicy(const std::string& password) {
    if (password.size() < kMinPasswordLength || password.size() > kMaxPasswordLength) {
        throw WeakPasswordException("新密码长度须在 8~64 位之间");
    }
}

LoginSession ToSession(const storage::UserRow& user) {
    LoginSession session;
    session.user_id = user.id;
    session.username = user.username;
    session.display_name = user.display_name;
    session.role = user.role;
    session.must_change_password = user.must_change_password;
    return session;
}

// RBAC:账号管理仅团支书。actor 必须存在、启用且为 BRANCH_SECRETARY。
void RequireSecretary(pqxx::work& tx, const std::string& actor_id) {
    const auto actor = storage::UsersRepo::FindById(tx, actor_id);
    if (!actor || !actor->is_active) {
        throw AuthenticationException("操作者账号不存在或已停用");
    }
    const auto role = ParseRole(actor->role);
    if (role != Role::BranchSecretary) {
        throw PermissionDeniedException("权限拒绝：" + actor->display_name + " 无权进行账号管理（仅团支书）");
    }
}

int CountActiveRoleHolders(pqxx::work& tx, const std::string& role_name) {
    const auto result = tx.exec(
        "SELECT COUNT(*) FROM users WHERE role = $1 AND is_active",
        pqxx::params(role_name));
    return result.front()[0].as<int>();
}

// 实体账户席位:账户不存在则建,存管人指向当前席位持有者。
void EnsureEntityAccountCustodian(pqxx::work& tx, const std::string& type, const std::string& name,
                                  const std::string& custodian_id) {
    if (!storage::EntityAccountsRepo::FindByType(tx, type).has_value()) {
        storage::EntityAccountsRepo::Insert(tx, name, type, custodian_id);
    } else {
        storage::EntityAccountsRepo::SetCustodian(tx, type, custodian_id);
    }
}

} // namespace

LoginSession AuthService::Login(const std::string& username, const std::string& password) {
    return context_.ExecuteTransaction([&](pqxx::work& tx) -> LoginSession {
        const auto user = storage::UsersRepo::FindByUsername(tx, username);
        // 失败路径统一异常:不向调用方泄露"是用户名还是密码错"。
        if (!user || !user->is_active || !PasswordHasher::Verify(user->password_hash, password)) {
            throw AuthenticationException("用户名或口令错误，或账号已被停用");
        }
        storage::UsersRepo::TouchLastLogin(tx, user->id);
        return ToSession(*user);
    });
}

void AuthService::ChangePassword(const std::string& user_id, const std::string& old_password,
                                 const std::string& new_password) {
    ValidatePasswordPolicy(new_password);

    context_.ExecuteTransaction([&](pqxx::work& tx) {
        const auto user = storage::UsersRepo::FindById(tx, user_id);
        if (!user || !user->is_active) {
            throw AuthenticationException("账号不存在或已停用");
        }
        if (!PasswordHasher::Verify(user->password_hash, old_password)) {
            throw AuthenticationException("原口令校验失败");
        }
        // 改密成功即解除首登强制改密状态。
        storage::UsersRepo::SetPassword(tx, user_id, PasswordHasher::Hash(new_password), false);
        return 0;
    });
}

std::string AuthService::CreateUser(const std::string& actor_id, const std::string& new_user_id,
                                    const std::string& username, const std::string& display_name,
                                    const std::string& role_name) {
    if (new_user_id.empty() || username.empty() || display_name.empty()) {
        throw std::invalid_argument("账号 id / 用户名 / 姓名不能为空");
    }
    const auto role = ParseRole(role_name);
    if (!role) {
        throw std::invalid_argument("非法角色取值: " + role_name);
    }

    return context_.ExecuteTransaction([&](pqxx::work& tx) -> std::string {
        RequireSecretary(tx, actor_id);

        // 席位唯一(§1:团支书、生活委员均为唯一存管人)。
        if ((role == Role::BranchSecretary || role == Role::LifeCommittee) &&
            CountActiveRoleHolders(tx, role_name) > 0) {
            throw std::invalid_argument(role == Role::BranchSecretary
                                            ? "团支书席位已有在任者，不可重复开立"
                                            : "生活委员席位已有在任者，不可重复开立");
        }

        const std::string temp_password = PasswordHasher::GenerateTempPassword();
        try {
            storage::UsersRepo::Insert(tx, new_user_id, username, display_name, role_name,
                                       PasswordHasher::Hash(temp_password));
        } catch (const pqxx::unique_violation&) {
            throw std::invalid_argument("用户名或账号 id 已存在: " + username);
        }

        // 席位与实体账户存管人绑定(§1 资金通道存管人语义)。
        if (role == Role::BranchSecretary) {
            EnsureEntityAccountCustodian(tx, "FLEXIBLE_PUBLIC", "班级灵活公款", new_user_id);
        } else if (role == Role::LifeCommittee) {
            EnsureEntityAccountCustodian(tx, "FACULTY_REIMBURSE", "系级报销往来暂挂", new_user_id);
        }
        return temp_password;
    });
}

std::string AuthService::ResetPassword(const std::string& actor_id, const std::string& target_user_id) {
    return context_.ExecuteTransaction([&](pqxx::work& tx) -> std::string {
        RequireSecretary(tx, actor_id);

        const auto target = storage::UsersRepo::FindById(tx, target_user_id);
        if (!target) {
            throw std::invalid_argument("目标账号不存在: " + target_user_id);
        }
        // 团支书可重置他人临时密码,但自身的密码仍须经 ChangePassword(需旧密码,避免自我锁死管理权)。
        if (target->id == actor_id) {
            throw std::invalid_argument("不可重置自身密码，请使用修改密码功能");
        }
        const std::string temp_password = PasswordHasher::GenerateTempPassword();
        storage::UsersRepo::SetPassword(tx, target_user_id, PasswordHasher::Hash(temp_password), true);
        return temp_password;
    });
}

void AuthService::DeactivateUser(const std::string& actor_id, const std::string& target_user_id) {
    context_.ExecuteTransaction([&](pqxx::work& tx) {
        RequireSecretary(tx, actor_id);
        if (actor_id == target_user_id) {
            throw std::invalid_argument("团支书不可停用自身账号");
        }
        if (!storage::UsersRepo::FindById(tx, target_user_id)) {
            throw std::invalid_argument("目标账号不存在: " + target_user_id);
        }
        storage::UsersRepo::SetActive(tx, target_user_id, false);
        return 0;
    });
}

void AuthService::ActivateUser(const std::string& actor_id, const std::string& target_user_id) {
    context_.ExecuteTransaction([&](pqxx::work& tx) {
        RequireSecretary(tx, actor_id);
        const auto target = storage::UsersRepo::FindById(tx, target_user_id);
        if (!target) {
            throw std::invalid_argument("目标账号不存在: " + target_user_id);
        }
        if (target->is_active) {
            return 0;
        }
        // 席位唯一(§1):重启团支书/生活委员席位前确认无在任者。
        if ((target->role == "BRANCH_SECRETARY" || target->role == "LIFE_COMMITTEE") &&
            CountActiveRoleHolders(tx, target->role) > 0) {
            throw std::invalid_argument(target->role == "BRANCH_SECRETARY"
                                            ? "团支书席位已有在任者，不可重复启用"
                                            : "生活委员席位已有在任者，不可重复启用");
        }
        storage::UsersRepo::SetActive(tx, target_user_id, true);
        return 0;
    });
}

void AuthService::UpdateDisplayName(const std::string& actor_id, const std::string& target_user_id,
                                    const std::string& display_name) {
    if (display_name.empty()) throw std::invalid_argument("姓名不能为空");

    context_.ExecuteTransaction([&](pqxx::work& tx) {
        RequireSecretary(tx, actor_id);
        if (!storage::UsersRepo::FindById(tx, target_user_id)) {
            throw std::invalid_argument("目标账号不存在: " + target_user_id);
        }
        tx.exec("UPDATE users SET display_name = $2 WHERE id = $1",
                pqxx::params(target_user_id, display_name));
        return 0;
    });
}

std::string AuthService::BootstrapFirstSecretary(const std::string& username,
                                                 const std::string& display_name) {
    if (username.empty() || display_name.empty()) {
        throw std::invalid_argument("用户名 / 姓名不能为空");
    }

    return context_.ExecuteTransaction([&](pqxx::work& tx) -> std::string {
        const auto count = tx.exec("SELECT COUNT(*) FROM users").front()[0].as<int>();
        if (count > 0) {
            throw std::runtime_error("系统已初始化，不可重复引导(需由团支书开立账号)");
        }

        const std::string temp_password = PasswordHasher::GenerateTempPassword();
        storage::UsersRepo::Insert(tx, "u_sec_01", username, display_name, ToString(Role::BranchSecretary),
                                   PasswordHasher::Hash(temp_password));
        // 灵活公款存管人即团支书;系报销账户待生活委员开立时再绑定存管人。
        EnsureEntityAccountCustodian(tx, "FLEXIBLE_PUBLIC", "班级灵活公款", "u_sec_01");
        return temp_password;
    });
}

} // namespace recta::core
