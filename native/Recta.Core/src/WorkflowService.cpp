#include "recta/core/AuthService.hpp"
#include "recta/core/WorkflowService.hpp"

#include "recta/Advance.hpp"
#include "recta/Conservation.hpp"
#include "recta/Split.hpp"
#include "recta/storage/EntityAccountsRepo.hpp"
#include "recta/storage/LedgerRepo.hpp"
#include "recta/storage/RequestRepo.hpp"
#include "recta/storage/StudentAccountsRepo.hpp"
#include "recta/storage/UsersRepo.hpp"

#include <pqxx/pqxx>

#include <format>
#include <map>
#include <optional>

namespace recta::core {
namespace {

// 统一的操作者装载:必须存在且启用,返回其行。
storage::UserRow RequireActiveUser(pqxx::work& tx, const std::string& actor_id) {
    auto actor = storage::UsersRepo::FindById(tx, actor_id);
    if (!actor || !actor->is_active) {
        throw AuthenticationException("操作者账号不存在或已停用");
    }
    return *actor;
}

Role RoleOf(const storage::UserRow& user) {
    const auto role = ParseRole(user.role);
    if (!role) {
        throw std::runtime_error("账号角色数据损坏: " + user.id + " / " + user.role);
    }
    return *role;
}

// 主单锁定装载:行级锁 + 状态机门槛。
storage::ExpenseRequestRow LockRequestInStatus(pqxx::work& tx, int request_id,
                                               RequestStatus expected) {
    auto request = storage::RequestRepo::Lock(tx, request_id);
    if (!request) {
        throw std::invalid_argument("单据不存在: #" + std::to_string(request_id));
    }
    const auto status = ParseRequestStatus(request->status);
    if (!status) {
        throw std::runtime_error("单据状态数据损坏: #" + std::to_string(request_id));
    }
    if (*status != expected) {
        throw InvalidRequestStateException(std::format(
            "单据 #{} 当前状态为 {},仅 {} 状态可执行本操作",
            request_id, request->status, ToString(expected)));
    }
    return *request;
}

// 权限硬前置(§8.3):先按单据渠道校验操作者角色,再进入状态机——
// 未授权调用者无论单据处于何种状态,一律先收到 PermissionDeniedException。
AccountCategory RequireCategoryOf(pqxx::work& tx, int request_id) {
    const auto peek = storage::RequestRepo::Find(tx, request_id);
    if (!peek) {
        throw std::invalid_argument("单据不存在: #" + std::to_string(request_id));
    }
    const auto category = ParseAccountCategory(peek->account_category);
    if (!category) {
        throw std::runtime_error("单据渠道数据损坏: #" + std::to_string(request_id));
    }
    return *category;
}

std::string FormatYuan(int64_t cents) {
    return Money(cents).to_plain_string();
}

void AppendChangeEvent(pqxx::work& tx, const char* entity, int64_t entity_id,
                       const char* event_type, const std::string& payload) {
    storage::LedgerRepo::AppendChangeEvent(tx, entity, std::to_string(entity_id), event_type,
                                           payload);
}

} // namespace

int WorkflowService::SubmitRequest(const std::string& applicant_id, const std::string& title,
                                   AccountCategory category, Money applied_amount,
                                   const std::optional<SplitPlanInput>& split_plan,
                                   const std::optional<std::string>& voucher_url) {
    if (title.empty() || title.size() > 128) {
        throw std::invalid_argument("动账事项标题必填(1~128 字)");
    }
    if (applied_amount.to_cents() <= 0) {
        throw std::invalid_argument("申报金额必须为正数");
    }
    if (category == AccountCategory::ClassFund && !split_plan.has_value()) {
        throw std::invalid_argument("班费平摊单必须携带参摊名单与尾差承担人");
    }

    return context_.ExecuteTransaction([&](pqxx::work& tx) -> int {
        RequireActiveUser(tx, applicant_id);

        std::vector<SplitAllocation> allocations;
        if (split_plan) {
            allocations = DistributeExpense(applied_amount, split_plan->participant_ids,
                                            split_plan->tail_bearer_id);
        }

        const int request_id = storage::RequestRepo::Insert(
            tx, title, ToString(category), applied_amount.to_cents(), applicant_id, voucher_url);

        if (!allocations.empty()) {
            std::vector<storage::SplitRow> rows;
            rows.reserve(allocations.size());
            for (const auto& allocation : allocations) {
                rows.push_back({allocation.student_id, allocation.amount.to_cents(),
                                allocation.is_tail_bearer, 0});
            }
            storage::RequestRepo::InsertSplits(tx, request_id, rows);
        }

        AppendChangeEvent(tx, "expense_request", request_id, "SUBMITTED",
                          std::format(R"({{"request_id":{},"applied_cents":{}}})",
                                      request_id, applied_amount.to_cents()));
        return request_id;
    });
}

void WorkflowService::ApproveRequest(const std::string& actor_id, int request_id,
                                     Money approved_amount,
                                     const std::optional<std::string>& notes) {
    if (approved_amount.to_cents() <= 0) {
        throw std::invalid_argument("核准金额必须为正数(核减到零应走驳回)");
    }

    context_.ExecuteTransaction([&](pqxx::work& tx) {
        const auto actor = RequireActiveUser(tx, actor_id);
        const auto category = RequireCategoryOf(tx, request_id);
        AssertCanReview(RoleOf(actor), category); // §8.3 两阶段权限硬约束
        const auto request = LockRequestInStatus(tx, request_id, RequestStatus::PendingReview);

        const int64_t applied = request.applied_amount_cents;
        const int64_t approved = approved_amount.to_cents();
        if (approved > applied) {
            throw std::invalid_argument(std::format("核准金额 {} 不可超过申报金额 {}",
                                                    FormatYuan(approved), FormatYuan(applied)));
        }
        const bool reduced = approved < applied;
        if (reduced && (!notes || notes->empty())) {
            throw std::invalid_argument("核减批复原因必填(§5.2)");
        }

        storage::RequestRepo::MarkApproved(tx, request_id, approved, actor_id, notes);

        // 班费平摊按核准金额自动重算(§5.2 核减分支)。
        if (category == AccountCategory::ClassFund) {
            const auto old_splits = storage::RequestRepo::ListSplits(tx, request_id);
            std::vector<std::string> participants;
            std::string tail_bearer;
            participants.reserve(old_splits.size());
            for (const auto& split : old_splits) {
                participants.push_back(split.student_id);
                if (split.is_tail_bearer) tail_bearer = split.student_id;
            }
            const auto allocations = DistributeExpense(Money(approved), participants, tail_bearer);
            std::vector<storage::SplitRow> rows;
            rows.reserve(allocations.size());
            for (const auto& allocation : allocations) {
                rows.push_back({allocation.student_id, allocation.amount.to_cents(),
                                allocation.is_tail_bearer, 0});
            }
            storage::RequestRepo::ReplaceSplits(tx, request_id, rows);
        }

        AppendChangeEvent(tx, "expense_request", request_id, reduced ? "REDUCED" : "APPROVED",
                          std::format(R"({{"request_id":{},"approved_cents":{}}})",
                                      request_id, approved));
        return 0;
    });
}

void WorkflowService::RejectRequest(const std::string& actor_id, int request_id,
                                    const std::string& notes) {
    if (notes.empty()) {
        throw std::invalid_argument("驳回原因说明必填(§5.2)");
    }

    context_.ExecuteTransaction([&](pqxx::work& tx) {
        const auto actor = RequireActiveUser(tx, actor_id);
        AssertCanReview(RoleOf(actor), RequireCategoryOf(tx, request_id));
        const auto request = LockRequestInStatus(tx, request_id, RequestStatus::PendingReview);

        storage::RequestRepo::MarkRejected(tx, request_id, actor_id, notes);
        AppendChangeEvent(tx, "expense_request", request_id, "REJECTED",
                          std::format(R"({{"request_id":{}}})", request_id));
        return 0;
    });
}

SettlementResult WorkflowService::SettleRequest(const std::string& actor_id, int request_id,
                                                const std::optional<std::string>& extra_notes) {
    return context_.ExecuteTransaction([&](pqxx::work& tx) -> SettlementResult {
        const auto actor = RequireActiveUser(tx, actor_id);
        AssertCanSettle(RoleOf(actor), RequireCategoryOf(tx, request_id)); // 办结人身份硬前置
        const auto request = LockRequestInStatus(tx, request_id, RequestStatus::Approved);

        const auto category = RequireCategoryOf(tx, request_id);

        const int64_t approved = request.approved_amount_cents.value_or(0);
        if (approved <= 0) {
            throw std::runtime_error("单据缺少核准金额，数据异常: #" + std::to_string(request_id));
        }

        SettlementResult result;
        result.request_id = request_id;
        result.category = request.account_category;
        result.settled_cents = approved;

        std::string settlement_notes;
        if (extra_notes && !extra_notes->empty()) {
            settlement_notes = *extra_notes + "\n";
        }

        if (category == AccountCategory::Flexible) {
            // 灵活公款:锁账户 → 余额充足校验 → 扣减 → 流水。
            auto account = storage::EntityAccountsRepo::LockByType(tx, "FLEXIBLE_PUBLIC");
            if (!account) {
                throw std::runtime_error("灵活公款账户未初始化");
            }
            if (account->balance_cents < approved) {
                throw std::runtime_error(std::format("灵活公款余额不足：现有 {} 元，本次需 {} 元",
                                                     FormatYuan(account->balance_cents),
                                                     FormatYuan(approved)));
            }
            const int64_t balance_after =
                storage::EntityAccountsRepo::AdjustBalance(tx, account->id, -approved);

            storage::LedgerEntry entry;
            entry.account_id = account->id;
            entry.expense_request_id = request_id;
            entry.entry_type = "DISBURSEMENT";
            entry.change_cents = -approved;
            entry.balance_after_cents = balance_after;
            entry.notes = request.title;
            storage::LedgerRepo::AppendLedger(tx, entry);

            settlement_notes += std::format("办结出账 {} 元（灵活公款，余额 {} 元）。",
                                            FormatYuan(approved), FormatYuan(balance_after));
        } else if (category == AccountCategory::Faculty) {
            // 系报销:班委已垫付,办结即确认挂账(应收增加),待系财务打款核销。
            auto account = storage::EntityAccountsRepo::LockByType(tx, "FACULTY_REIMBURSE");
            if (!account) {
                throw std::runtime_error("系报销暂挂账户未初始化(需先开立生活委员)");
            }
            const int64_t balance_after =
                storage::EntityAccountsRepo::AdjustBalance(tx, account->id, approved);

            storage::LedgerEntry entry;
            entry.account_id = account->id;
            entry.expense_request_id = request_id;
            entry.entry_type = "DISBURSEMENT";
            entry.change_cents = approved; // 挂账方向为正(应收)
            entry.balance_after_cents = balance_after;
            entry.notes = request.title;
            storage::LedgerRepo::AppendLedger(tx, entry);

            settlement_notes += std::format(
                "办结挂账 {} 元（系报销应收暂挂，待系财务打款核销，当前挂账 {} 元）。",
                FormatYuan(approved), FormatYuan(balance_after));
        } else {
            // 班费:锁全部分户(按 id 升序) → 逐人扣减+Δadvance → 回填 → 组装垫资批复。
            const auto splits = storage::RequestRepo::ListSplits(tx, request_id);
            if (splits.empty()) {
                throw std::runtime_error("班费单据缺少平摊明细: #" + std::to_string(request_id));
            }
            std::vector<std::string> participant_ids;
            participant_ids.reserve(splits.size());
            for (const auto& split : splits) participant_ids.push_back(split.student_id);

            const auto locked_students = storage::StudentAccountsRepo::LockMany(tx, participant_ids);
            if (locked_students.size() != splits.size()) {
                throw std::runtime_error("分摊名单中存在未建档同学，请先补录名单");
            }
            std::map<std::string, storage::StudentAccountRow> by_id;
            for (const auto& student : locked_students) by_id[student.student_id] = student;

            // 先校验分摊合计 == 核准金额(防脏数据入账)。
            int64_t splits_sum = 0;
            for (const auto& split : splits) splits_sum += split.amount_cents;
            if (splits_sum != approved) {
                throw std::logic_error(std::format("分摊合计 {} 与核准金额 {} 不符，拒绝办结",
                                                   FormatYuan(splits_sum), FormatYuan(approved)));
            }

            std::vector<StudentAdvanceLine> advance_lines;
            int64_t total_advance = 0;
            for (const auto& split : splits) {
                const auto& student = by_id.at(split.student_id);
                const Money balance_before(student.balance_cents);
                const Money deduction(split.amount_cents);
                const Money delta = ComputeAdvanceDelta(balance_before, deduction);

                const int64_t balance_after =
                    storage::StudentAccountsRepo::ApplyDebit(tx, split.student_id,
                                                             split.amount_cents);
                storage::RequestRepo::SetSplitAdvance(tx, request_id, split.student_id,
                                                      delta.to_cents());

                storage::LedgerEntry entry;
                entry.student_id = split.student_id;
                entry.expense_request_id = request_id;
                entry.entry_type = "EXPENSE_SPLIT";
                entry.change_cents = -split.amount_cents;
                entry.balance_after_cents = balance_after;
                entry.notes = request.title + (split.is_tail_bearer ? "（承担尾差）" : "");
                storage::LedgerRepo::AppendLedger(tx, entry);

                if (delta.to_cents() > 0) {
                    advance_lines.push_back({split.student_id, student.name, delta.to_cents()});
                    total_advance += delta.to_cents();
                }
            }

            // 办结后守恒强校验(§4.1 恒等式;任何破坏立即整体回滚)。
            const auto custody = storage::StudentAccountsRepo::AggregateCustody(tx);
            if (!IsConserved(Money(custody.balances_sum), Money(custody.custodian_cash),
                             Money(custody.advance_total))) {
                throw std::logic_error("办结守恒校验失败，事务回滚");
            }

            result.advances = advance_lines;
            result.total_advance_cents = total_advance;
            result.balances_sum_after = custody.balances_sum;
            result.custodian_cash_after = custody.custodian_cash;
            result.advance_total_after = custody.advance_total;

            settlement_notes += std::format("办结出账 {} 元（班费平摊，{} 人）。", FormatYuan(approved),
                                            splits.size());
            if (!advance_lines.empty()) {
                settlement_notes += std::format("生委垫资 {} 笔共 {} 元：", advance_lines.size(),
                                                FormatYuan(total_advance));
                for (std::size_t i = 0; i < advance_lines.size(); ++i) {
                    settlement_notes += std::format("{} {} 元{}", advance_lines[i].name,
                                                    FormatYuan(advance_lines[i].amount_cents),
                                                    i + 1 == advance_lines.size() ? "。" : "；");
                }
            } else {
                settlement_notes += "无生委垫资。";
            }
        }

        storage::RequestRepo::MarkSettled(tx, request_id, approved, actor_id, settlement_notes);
        AppendChangeEvent(tx, "expense_request", request_id, "SETTLED",
                          std::format(R"({{"request_id":{},"settled_cents":{}}})",
                                      request_id, approved));

        // 非班费渠道也带回守恒快照(供界面底栏展示)。
        if (result.category != "CLASS_FUND") {
            const auto custody = storage::StudentAccountsRepo::AggregateCustody(tx);
            result.balances_sum_after = custody.balances_sum;
            result.custodian_cash_after = custody.custodian_cash;
            result.advance_total_after = custody.advance_total;
        }
        return result;
    });
}

void WorkflowService::RecordInflow(const std::string& actor_id, const InflowInput& input) {
    if (input.amount.to_cents() <= 0) {
        throw std::invalid_argument("入账金额必须为正数(分)");
    }
    if (input.source_title.empty()) {
        throw std::invalid_argument("入账来源凭据必填(§5.3)");
    }
    if (input.destination == InflowDestination::ToStudentSubAccount && !input.target_student_id) {
        throw std::invalid_argument("同学补缴充值必须指定目标分户");
    }
    if (input.destination == InflowDestination::ToFacultyReimburse && !input.related_request_id) {
        throw std::invalid_argument("系报销回款核销必须关联报销单号");
    }

    context_.ExecuteTransaction([&](pqxx::work& tx) {
        const auto actor = RequireActiveUser(tx, actor_id);
        const auto role = RoleOf(actor);

        // 入账确认权限(§5.3):灵活账户→团支书;系核销/同学充值→生活委员。
        switch (input.destination) {
        case InflowDestination::ToFlexibleAccount:
            if (role != Role::BranchSecretary) {
                throw PermissionDeniedException("权限拒绝：灵活账户增资仅团支书可确认");
            }
            break;
        case InflowDestination::ToFacultyReimburse:
        case InflowDestination::ToStudentSubAccount:
            if (role != Role::LifeCommittee) {
                throw PermissionDeniedException("权限拒绝：该入账通道仅生活委员可确认");
            }
            break;
        }

        const int64_t amount = input.amount.to_cents();
        const auto destination_name = ToString(input.destination);

        if (input.destination == InflowDestination::ToStudentSubAccount) {
            // 定向充值:直接平复负数透支,解除对应债权(§1 核心原则 2)。
            if (!storage::StudentAccountsRepo::Find(tx, *input.target_student_id)) {
                throw std::invalid_argument("目标分户不存在: " + *input.target_student_id);
            }
            const int64_t balance_after = storage::StudentAccountsRepo::ApplyCredit(
                tx, *input.target_student_id, amount);

            const int64_t inflow_id = storage::LedgerRepo::InsertInflow(
                tx, amount, input.source_title, destination_name, input.target_student_id,
                input.related_request_id, actor_id, input.voucher_url);

            storage::LedgerEntry entry;
            entry.student_id = input.target_student_id;
            entry.inflow_record_id = inflow_id;
            entry.entry_type = "RECHARGE";
            entry.change_cents = amount;
            entry.balance_after_cents = balance_after;
            entry.notes = input.source_title;
            storage::LedgerRepo::AppendLedger(tx, entry);

            const auto custody = storage::StudentAccountsRepo::AggregateCustody(tx);
            if (!IsConserved(Money(custody.balances_sum), Money(custody.custodian_cash),
                             Money(custody.advance_total))) {
                throw std::logic_error("充值后守恒校验失败，事务回滚");
            }
            AppendChangeEvent(tx, "inflow", inflow_id, "CREATED",
                              std::format(R"({{"inflow_id":{},"amount_cents":{},"student":"{}"}})",
                                          inflow_id, amount, *input.target_student_id));
        } else {
            const char* account_type =
                input.destination == InflowDestination::ToFlexibleAccount ? "FLEXIBLE_PUBLIC"
                                                                          : "FACULTY_REIMBURSE";
            auto account = storage::EntityAccountsRepo::LockByType(tx, account_type);
            if (!account) {
                throw std::runtime_error(std::string("实体账户未初始化: ") + account_type);
            }
            if (input.destination == InflowDestination::ToFacultyReimburse &&
                account->balance_cents < amount) {
                throw std::invalid_argument(std::format(
                    "核销金额 {} 元超过当前挂账 {} 元", FormatYuan(amount),
                    FormatYuan(account->balance_cents)));
            }
            // 关联单据必须是已办结的系报销单。
            if (input.related_request_id) {
                const auto related = storage::RequestRepo::Find(tx, *input.related_request_id);
                if (!related || related->account_category != "FACULTY" ||
                    related->status != "SETTLED") {
                    throw std::invalid_argument("关联报销单必须是已办结的系报销单据");
                }
            }
            const int64_t balance_after =
                storage::EntityAccountsRepo::AdjustBalance(tx, account->id,
                                                           input.destination ==
                                                                   InflowDestination::
                                                                       ToFacultyReimburse
                                                               ? -amount // 核销减挂账
                                                               : amount); // 增资加余额

            const int64_t inflow_id = storage::LedgerRepo::InsertInflow(
                tx, amount, input.source_title, destination_name, input.target_student_id,
                input.related_request_id, actor_id, input.voucher_url);

            storage::LedgerEntry entry;
            entry.account_id = account->id;
            entry.inflow_record_id = inflow_id;
            entry.entry_type = "INFLOW";
            entry.change_cents = input.destination == InflowDestination::ToFacultyReimburse
                                     ? -amount
                                     : amount;
            entry.balance_after_cents = balance_after;
            entry.notes = input.source_title;
            storage::LedgerRepo::AppendLedger(tx, entry);

            AppendChangeEvent(tx, "inflow", inflow_id, "CREATED",
                              std::format(R"({{"inflow_id":{},"amount_cents":{}}})", inflow_id,
                                          amount));
        }
        return 0;
    });
}

void RosterService::AddStudent(const std::string& actor_id, const std::string& student_id,
                               const std::string& name) {
    if (student_id.empty() || name.empty()) {
        throw std::invalid_argument("学号与姓名必填");
    }

    context_.ExecuteTransaction([&](pqxx::work& tx) {
        const auto actor = RequireActiveUser(tx, actor_id);
        if (RoleOf(actor) != Role::BranchSecretary) {
            throw PermissionDeniedException("权限拒绝：同学名单仅团支书可录入");
        }
        if (storage::StudentAccountsRepo::Find(tx, student_id)) {
            throw std::invalid_argument("学号已存在: " + student_id);
        }
        storage::StudentAccountsRepo::Upsert(tx, student_id, name);
        storage::LedgerRepo::AppendChangeEvent(tx, "student", student_id, "CREATED",
                                               std::format(R"({{"student_id":"{}"}})", student_id));
        return 0;
    });
}

void RosterService::RenameStudent(const std::string& actor_id, const std::string& student_id,
                                  const std::string& name) {
    if (name.empty()) {
        throw std::invalid_argument("姓名必填");
    }

    context_.ExecuteTransaction([&](pqxx::work& tx) {
        const auto actor = RequireActiveUser(tx, actor_id);
        if (RoleOf(actor) != Role::BranchSecretary) {
            throw PermissionDeniedException("权限拒绝：同学名单仅团支书可改名");
        }
        if (!storage::StudentAccountsRepo::Find(tx, student_id)) {
            throw std::invalid_argument("学号不存在: " + student_id);
        }
        storage::StudentAccountsRepo::Rename(tx, student_id, name);
        return 0;
    });
}

} // namespace recta::core
