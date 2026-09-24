#include "recta/capi/recta_capi.h"

#include <nlohmann/json.hpp>

#include "recta/Conservation.hpp"
#include "recta/Money.hpp"
#include "recta/Roles.hpp"
#include "recta/Split.hpp"
#include "recta/core/AuthService.hpp"
#include "recta/core/WorkflowService.hpp"
#include "recta/storage/EnvConfig.hpp"
#include "recta/storage/EntityAccountsRepo.hpp"
#include "recta/storage/LedgerRepo.hpp"
#include "recta/storage/NeonContext.hpp"
#include "recta/storage/RequestRepo.hpp"
#include "recta/storage/StudentAccountsRepo.hpp"
#include "recta/storage/UsersRepo.hpp"

#include <pqxx/pqxx>

#include <algorithm>
#include <cstring>
#include <memory>
#include <mutex>
#include <optional>
#include <string>
#include <utility>
#include <vector>

namespace {
using json = nlohmann::json;

struct State {
    std::mutex mutex;
    std::unique_ptr<recta::storage::NeonContext> neon;
    std::unique_ptr<recta::core::AuthService> auth;
    std::unique_ptr<recta::core::WorkflowService> workflow;
    std::unique_ptr<recta::core::RosterService> roster;
    bool ready = false;
};

State& S() {
    static State state;
    return state;
}

thread_local std::string g_last_error;

struct NotReadyError final : std::runtime_error {
    using std::runtime_error::runtime_error;
};

void SetError(const std::string& message) { g_last_error = message; }

// 统一异常→错误码分派。任何异常都不允许跨越 C 边界。
int32_t DispatchError() {
    try {
        throw;
    } catch (const NotReadyError& e) {
        SetError(e.what());
        return RECTA_ERR_NOT_READY;
    } catch (const recta::PermissionDeniedException& e) {
        SetError(e.what());
        return RECTA_ERR_PERMISSION;
    } catch (const recta::core::AuthenticationException& e) {
        SetError(e.what());
        return RECTA_ERR_AUTH;
    } catch (const recta::core::WeakPasswordException& e) {
        SetError(e.what());
        return RECTA_ERR_WEAK_PASSWORD;
    } catch (const recta::core::InvalidRequestStateException& e) {
        SetError(e.what());
        return RECTA_ERR_STATE;
    } catch (const pqxx::sql_error& e) {
        SetError(std::string("数据库错误：") + e.what());
        return RECTA_ERR_DB;
    } catch (const std::invalid_argument& e) {
        SetError(e.what());
        return RECTA_ERR_INVALID_ARG;
    } catch (const std::logic_error& e) {
        SetError(e.what());
        return RECTA_ERR_LOGIC;
    } catch (const std::overflow_error& e) {
        SetError(e.what());
        return RECTA_ERR_LOGIC;
    } catch (const std::exception& e) {
        SetError(e.what());
        return RECTA_ERR_UNKNOWN;
    } catch (...) {
        SetError("未知错误");
        return RECTA_ERR_UNKNOWN;
    }
}

int32_t WriteOut(char* buf, int32_t cap, const std::string& out) {
    if (buf == nullptr || cap <= 0 || static_cast<std::size_t>(cap) <= out.size()) {
        SetError("输出缓冲容量不足");
        return RECTA_ERR_BUFFER_SMALL;
    }
    std::memcpy(buf, out.data(), out.size());
    buf[out.size()] = '\0';
    return static_cast<int32_t>(out.size());
}

// 出参为 JSON 字符串的导出统一骨架。
template <typename Body>
int32_t JsonCall(char* buf, int32_t cap, Body&& body) {
    const std::lock_guard<std::mutex> lock(S().mutex);
    try {
        return WriteOut(buf, cap, body());
    } catch (...) {
        return DispatchError();
    }
}

// 无字符串出参的导出统一骨架。
template <typename Body>
int32_t Call(Body&& body) {
    const std::lock_guard<std::mutex> lock(S().mutex);
    try {
        body();
        return 0;
    } catch (...) {
        return DispatchError();
    }
}

void RequireReady() {
    if (!S().ready) {
        throw NotReadyError("原生核心未初始化：请先调用 recta_init / recta_init_test");
    }
}

std::string ReqStr(const char* s) {
    if (s == nullptr || *s == '\0') {
        throw std::invalid_argument("必填字符串参数为空");
    }
    return s;
}

std::optional<std::string> OptStr(const char* s) {
    if (s == nullptr || *s == '\0') return std::nullopt;
    return std::string(s);
}

recta::AccountCategory ParseCategory(const char* text) {
    const auto category = recta::ParseAccountCategory(ReqStr(text));
    if (!category) {
        throw std::invalid_argument(std::string("非法资金渠道: ") + text);
    }
    return *category;
}

recta::InflowDestination ParseDestination(const std::string& text) {
    const auto destination = recta::ParseInflowDestination(text);
    if (!destination) {
        throw std::invalid_argument("非法入账去向: " + text);
    }
    return *destination;
}

json OptJson(const std::optional<std::string>& value) {
    return value.has_value() ? json(*value) : json(nullptr);
}

json OptJson(const std::optional<int64_t>& value) {
    return value.has_value() ? json(*value) : json(nullptr);
}

json SessionJson(const recta::core::LoginSession& session) {
    return json{
        {"user_id", session.user_id},
        {"username", session.username},
        {"display_name", session.display_name},
        {"role", session.role},
        {"must_change_password", session.must_change_password},
    };
}

json RequestJson(const recta::storage::ExpenseRequestRow& r) {
    return json{
        {"id", r.id},
        {"title", r.title},
        {"account_category", r.account_category},
        {"applied_amount_cents", r.applied_amount_cents},
        {"approved_amount_cents", OptJson(r.approved_amount_cents)},
        {"settled_amount_cents", OptJson(r.settled_amount_cents)},
        {"applicant_id", r.applicant_id},
        {"reviewer_id", OptJson(r.reviewer_id)},
        {"settler_id", OptJson(r.settler_id)},
        {"status", r.status},
        {"review_notes", OptJson(r.review_notes)},
        {"settlement_notes", OptJson(r.settlement_notes)},
        {"created_at", OptJson(r.created_at)},
        {"reviewed_at", OptJson(r.reviewed_at)},
        {"settled_at", OptJson(r.settled_at)},
    };
}

json SplitJson(const recta::storage::SplitRow& s) {
    return json{
        {"student_id", s.student_id},
        {"student_name", OptJson(s.student_name)},
        {"amount_cents", s.amount_cents},
        {"is_tail_bearer", s.is_tail_bearer},
        {"advance_cents", s.advance_cents},
    };
}

json StudentJson(const recta::storage::StudentAccountRow& s) {
    return json{
        {"student_id", s.student_id},
        {"name", s.name},
        {"balance_cents", s.balance_cents},
        {"total_recharged_cents", s.total_recharged_cents},
        {"total_spent_cents", s.total_spent_cents},
    };
}

json AccountJson(const recta::storage::EntityAccountRow& a) {
    return json{
        {"id", a.id},
        {"name", a.name},
        {"type", a.type},
        {"balance_cents", a.balance_cents},
        {"custodian_id", OptJson(a.custodian_id)},
    };
}

json UserJson(const recta::storage::UserRow& u) {
    // 永不导出 password_hash。
    return json{
        {"id", u.id},
        {"username", u.username},
        {"display_name", u.display_name},
        {"role", u.role},
        {"must_change_password", u.must_change_password},
        {"is_active", u.is_active},
        {"last_login_at", OptJson(u.last_login_at)},
    };
}

json SettleJson(const recta::core::SettlementResult& r) {
    json advances = json::array();
    for (const auto& line : r.advances) {
        advances.push_back(json{
            {"student_id", line.student_id},
            {"name", line.name},
            {"amount_cents", line.amount_cents},
        });
    }
    return json{
        {"request_id", r.request_id},
        {"category", r.category},
        {"settled_cents", r.settled_cents},
        {"advances", advances},
        {"total_advance_cents", r.total_advance_cents},
        {"custody",
         {
             {"balances_sum_cents", r.balances_sum_after},
             {"custodian_cash_cents", r.custodian_cash_after},
             {"advance_total_cents", r.advance_total_after},
         }},
    };
}

} // namespace

// ---- 生命周期 ----

int32_t recta_init(const char* connection_string) {
    return Call([&] {
        std::string conn = (connection_string != nullptr && *connection_string != '\0')
                               ? std::string(connection_string)
                               : recta::storage::LoadConnectionString();
        S().neon = std::make_unique<recta::storage::NeonContext>(std::move(conn));
        S().auth = std::make_unique<recta::core::AuthService>(*S().neon);
        S().workflow = std::make_unique<recta::core::WorkflowService>(*S().neon);
        S().roster = std::make_unique<recta::core::RosterService>(*S().neon);
        S().ready = true;
    });
}

int32_t recta_init_test(void) {
    return Call([&] {
        const auto conn = recta::storage::TryLoadTestConnectionString();
        if (!conn) {
            throw std::runtime_error("未找到测试连接串(RECTA_TEST_DATABASE_URL / .env.test.local)");
        }
        S().neon = std::make_unique<recta::storage::NeonContext>(*conn);
        S().auth = std::make_unique<recta::core::AuthService>(*S().neon);
        S().workflow = std::make_unique<recta::core::WorkflowService>(*S().neon);
        S().roster = std::make_unique<recta::core::RosterService>(*S().neon);
        S().ready = true;
    });
}

void recta_shutdown(void) {
    const std::lock_guard<std::mutex> lock(S().mutex);
    S().roster.reset();
    S().workflow.reset();
    S().auth.reset();
    S().neon.reset();
    S().ready = false;
}

int32_t recta_version(char* buf, int32_t cap) {
    return JsonCall(buf, cap, [] {
        return "Recta native core 0.1.0 (RectaCApi)";
    });
}

int32_t recta_last_error(char* buf, int32_t cap) {
    return JsonCall(buf, cap, [] {
        return g_last_error.empty() ? std::string("(无错误)") : g_last_error;
    });
}

// ---- 领域纯函数 ----

int32_t recta_money_format(int64_t cents, char* buf, int32_t cap) {
    return JsonCall(buf, cap, [&] {
        return recta::Money(cents).to_plain_string();
    });
}

int32_t recta_money_parse(const char* text, int64_t* out_cents) {
    return Call([&] {
        if (out_cents == nullptr) throw std::invalid_argument("out_cents 为空");
        *out_cents = recta::Money::parse(ReqStr(text)).to_cents();
    });
}

int32_t recta_distribute(int64_t total_cents, const char* ids_json, const char* tail_bearer_id,
                         char* buf, int32_t cap) {
    return JsonCall(buf, cap, [&] {
        if (total_cents < 0) throw std::invalid_argument("平摊总额不能为负");
        const json ids = json::parse(ReqStr(ids_json));
        if (!ids.is_array()) throw std::invalid_argument("ids_json 必须是字符串数组");
        std::vector<std::string> student_ids;
        for (const auto& item : ids) {
            if (!item.is_string()) throw std::invalid_argument("ids_json 必须是字符串数组");
            student_ids.push_back(item.get<std::string>());
        }
        const auto allocations = recta::DistributeExpense(recta::Money(total_cents), student_ids,
                                                          ReqStr(tail_bearer_id));
        json out = json::array();
        int64_t sum = 0;
        for (const auto& allocation : allocations) {
            out.push_back(json{
                {"student_id", allocation.student_id},
                {"amount_cents", allocation.amount.to_cents()},
                {"is_tail_bearer", allocation.is_tail_bearer},
            });
            sum += allocation.amount.to_cents();
        }
        return json{
            {"allocations", out},
            {"count", allocations.size()},
            {"sum_cents", sum},
        }.dump();
    });
}

// ---- 认证与账号管理 ----

int32_t recta_login(const char* username, const char* password, char* buf, int32_t cap) {
    return JsonCall(buf, cap, [&] {
        RequireReady();
        return SessionJson(S().auth->Login(ReqStr(username), ReqStr(password))).dump();
    });
}

int32_t recta_change_password(const char* user_id, const char* old_password,
                              const char* new_password) {
    return Call([&] {
        RequireReady();
        S().auth->ChangePassword(ReqStr(user_id), ReqStr(old_password), ReqStr(new_password));
    });
}

int32_t recta_create_user(const char* actor_id, const char* new_user_id, const char* username,
                          const char* display_name, const char* role_name, char* buf,
                          int32_t cap) {
    return JsonCall(buf, cap, [&] {
        RequireReady();
        return S().auth->CreateUser(ReqStr(actor_id), ReqStr(new_user_id), ReqStr(username),
                                    ReqStr(display_name), ReqStr(role_name));
    });
}

int32_t recta_reset_password(const char* actor_id, const char* target_user_id, char* buf,
                             int32_t cap) {
    return JsonCall(buf, cap, [&] {
        RequireReady();
        return S().auth->ResetPassword(ReqStr(actor_id), ReqStr(target_user_id));
    });
}

int32_t recta_deactivate_user(const char* actor_id, const char* target_user_id) {
    return Call([&] {
        RequireReady();
        S().auth->DeactivateUser(ReqStr(actor_id), ReqStr(target_user_id));
    });
}

int32_t recta_update_display_name(const char* actor_id, const char* target_user_id,
                                  const char* display_name) {
    return Call([&] {
        RequireReady();
        S().auth->UpdateDisplayName(ReqStr(actor_id), ReqStr(target_user_id), ReqStr(display_name));
    });
}

int32_t recta_bootstrap_secretary(const char* username, const char* display_name, char* buf,
                                  int32_t cap) {
    return JsonCall(buf, cap, [&] {
        RequireReady();
        return S().auth->BootstrapFirstSecretary(ReqStr(username), ReqStr(display_name));
    });
}

// ---- 工作流 ----

int32_t recta_submit_request(const char* actor_id, const char* title, const char* category,
                             int64_t applied_cents, const char* split_ids_json,
                             const char* tail_bearer_id, int32_t* out_request_id) {
    return Call([&] {
        RequireReady();
        if (out_request_id == nullptr) throw std::invalid_argument("out_request_id 为空");

        std::optional<recta::core::SplitPlanInput> plan;
        if (split_ids_json != nullptr && *split_ids_json != '\0') {
            const json ids = json::parse(split_ids_json);
            if (!ids.is_array()) throw std::invalid_argument("split_ids_json 必须是字符串数组");
            plan = recta::core::SplitPlanInput{};
            for (const auto& item : ids) {
                if (!item.is_string()) throw std::invalid_argument("split_ids_json 必须是字符串数组");
                plan->participant_ids.push_back(item.get<std::string>());
            }
            plan->tail_bearer_id = ReqStr(tail_bearer_id);
        }
        *out_request_id = S().workflow->SubmitRequest(ReqStr(actor_id), ReqStr(title),
                                                      ParseCategory(category),
                                                      recta::Money(applied_cents), plan);
    });
}

int32_t recta_approve_request(const char* actor_id, int32_t request_id, int64_t approved_cents,
                              const char* notes) {
    return Call([&] {
        RequireReady();
        S().workflow->ApproveRequest(ReqStr(actor_id), request_id, recta::Money(approved_cents),
                                     OptStr(notes));
    });
}

int32_t recta_reject_request(const char* actor_id, int32_t request_id, const char* notes) {
    return Call([&] {
        RequireReady();
        S().workflow->RejectRequest(ReqStr(actor_id), request_id, ReqStr(notes));
    });
}

int32_t recta_settle_request(const char* actor_id, int32_t request_id, const char* extra_notes,
                             char* buf, int32_t cap) {
    return JsonCall(buf, cap, [&] {
        RequireReady();
        return SettleJson(S().workflow->SettleRequest(ReqStr(actor_id), request_id,
                                                     OptStr(extra_notes)))
            .dump();
    });
}

int32_t recta_record_inflow(const char* actor_id, const char* inflow_json) {
    return Call([&] {
        RequireReady();
        const json input = json::parse(ReqStr(inflow_json));
        if (!input.is_object() || !input.contains("destination") ||
            !input.contains("amount_cents") || !input.contains("source_title")) {
            throw std::invalid_argument("inflow_json 缺少必填字段(destination/amount_cents/source_title)");
        }

        recta::core::InflowInput in;
        in.destination = ParseDestination(input.at("destination").get<std::string>());
        in.amount = recta::Money(input.at("amount_cents").get<int64_t>());
        in.source_title = input.at("source_title").get<std::string>();
        if (input.contains("target_student_id") && input.at("target_student_id").is_string()) {
            in.target_student_id = input.at("target_student_id").get<std::string>();
        }
        if (input.contains("related_request_id") && input.at("related_request_id").is_number()) {
            in.related_request_id = input.at("related_request_id").get<int>();
        }
        if (input.contains("voucher_url") && input.at("voucher_url").is_string()) {
            in.voucher_url = input.at("voucher_url").get<std::string>();
        }
        S().workflow->RecordInflow(ReqStr(actor_id), in);
    });
}

int32_t recta_add_student(const char* actor_id, const char* student_id, const char* name) {
    return Call([&] {
        RequireReady();
        S().roster->AddStudent(ReqStr(actor_id), ReqStr(student_id), ReqStr(name));
    });
}

int32_t recta_rename_student(const char* actor_id, const char* student_id, const char* name) {
    return Call([&] {
        RequireReady();
        S().roster->RenameStudent(ReqStr(actor_id), ReqStr(student_id), ReqStr(name));
    });
}

// ---- 查询 ----

int32_t recta_list_requests(const char* status_filter, const char* category_filter,
                            const char* applicant_id_filter, char* buf, int32_t cap) {
    return JsonCall(buf, cap, [&] {
        RequireReady();
        recta::storage::RequestFilter filter;
        filter.status = OptStr(status_filter);
        filter.category = OptStr(category_filter);
        filter.applicant_id = OptStr(applicant_id_filter);
        const auto requests = S().neon->ExecuteTransaction(
            [&](pqxx::work& tx) { return recta::storage::RequestRepo::List(tx, filter); });
        json array = json::array();
        for (const auto& request : requests) array.push_back(RequestJson(request));
        return json{{"requests", array}}.dump();
    });
}

int32_t recta_get_request(int32_t request_id, char* buf, int32_t cap) {
    return JsonCall(buf, cap, [&] {
        RequireReady();
        const auto data = S().neon->ExecuteTransaction(
            [&](pqxx::work& tx)
                -> std::optional<std::pair<recta::storage::ExpenseRequestRow,
                                           std::vector<recta::storage::SplitRow>>> {
                auto request = recta::storage::RequestRepo::Find(tx, request_id);
                if (!request) return std::nullopt;
                return std::make_pair(*request,
                                      recta::storage::RequestRepo::ListSplits(tx, request_id));
            });
        if (!data.has_value()) {
            throw std::invalid_argument("单据不存在: #" + std::to_string(request_id));
        }
        json splits = json::array();
        for (const auto& split : data->second) splits.push_back(SplitJson(split));
        return json{{"request", RequestJson(data->first)}, {"splits", splits}}.dump();
    });
}

int32_t recta_list_students(char* buf, int32_t cap) {
    return JsonCall(buf, cap, [&] {
        RequireReady();
        const auto students = S().neon->ExecuteTransaction(
            [](pqxx::work& tx) { return recta::storage::StudentAccountsRepo::ListAll(tx); });
        json array = json::array();
        for (const auto& student : students) array.push_back(StudentJson(student));
        return json{{"students", array}}.dump();
    });
}

int32_t recta_list_accounts(char* buf, int32_t cap) {
    return JsonCall(buf, cap, [&] {
        RequireReady();
        const auto accounts = S().neon->ExecuteTransaction(
            [](pqxx::work& tx) { return recta::storage::EntityAccountsRepo::ListAll(tx); });
        json array = json::array();
        for (const auto& account : accounts) array.push_back(AccountJson(account));
        return json{{"accounts", array}}.dump();
    });
}

int32_t recta_list_users(char* buf, int32_t cap) {
    return JsonCall(buf, cap, [&] {
        RequireReady();
        const auto users = S().neon->ExecuteTransaction(
            [](pqxx::work& tx) { return recta::storage::UsersRepo::ListAll(tx); });
        json array = json::array();
        for (const auto& user : users) array.push_back(UserJson(user));
        return json{{"users", array}}.dump();
    });
}

int32_t recta_get_overview(char* buf, int32_t cap) {
    return JsonCall(buf, cap, [&] {
        RequireReady();
        const json overview = S().neon->ExecuteTransaction([](pqxx::work& tx) -> json {
            const auto custody = recta::storage::StudentAccountsRepo::AggregateCustody(tx);
            const auto accounts = recta::storage::EntityAccountsRepo::ListAll(tx);
            const auto counts =
                tx.exec("SELECT status, COUNT(*) FROM expense_requests GROUP BY status");
            json status_counts = json::object();
            for (const auto& row : counts) {
                status_counts[row[0].as<std::string>()] = row[1].as<int64_t>();
            }
            int64_t flexible = 0;
            int64_t faculty = 0;
            for (const auto& account : accounts) {
                if (account.type == "FLEXIBLE_PUBLIC") flexible = account.balance_cents;
                if (account.type == "FACULTY_REIMBURSE") faculty = account.balance_cents;
            }
            return json{
                {"custody",
                 {
                     {"balances_sum_cents", custody.balances_sum},
                     {"custodian_cash_cents", custody.custodian_cash},
                     {"advance_total_cents", custody.advance_total},
                     {"conserved",
                      recta::IsConserved(recta::Money(custody.balances_sum),
                                         recta::Money(custody.custodian_cash),
                                         recta::Money(custody.advance_total))},
                 }},
                {"status_counts", status_counts},
                {"flexible_balance_cents", flexible},
                {"faculty_hanging_cents", faculty},
            };
        });
        return overview.dump();
    });
}

int32_t recta_fetch_change_events(int64_t after_seq, int32_t limit, char* buf, int32_t cap) {
    return JsonCall(buf, cap, [&] {
        RequireReady();
        if (limit <= 0) limit = 100;
        const auto events = S().neon->ExecuteTransaction(
            [&](pqxx::work& tx) {
                return recta::storage::LedgerRepo::FetchChangeEventsSince(tx, after_seq, limit);
            });
        json array = json::array();
        int64_t max_seq = after_seq;
        for (const auto& event : events) {
            array.push_back(json{
                {"seq", event.seq},
                {"entity_type", event.entity_type},
                {"entity_id", event.entity_id},
                {"event_type", event.event_type},
                {"payload", event.payload},
            });
            max_seq = std::max(max_seq, event.seq);
        }
        return json{{"events", array}, {"max_seq", max_seq}}.dump();
    });
}

#ifdef RECTA_DEV_TOOLS
int32_t recta_dev_truncate_all(void) {
    return Call([&] {
        RequireReady();
        S().neon->ExecuteTransaction([](pqxx::work& tx) {
            tx.exec("TRUNCATE account_ledger_entries, inflow_records, expense_splits, "
                    "expense_requests, accounts, student_personal_accounts, users "
                    "RESTART IDENTITY CASCADE");
            return 0;
        });
    });
}
#endif
