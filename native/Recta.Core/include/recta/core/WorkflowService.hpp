#pragma once
// 两阶段流转与入账引擎(Vibe.md §5.2 / §5.3 / §8.4)。
//
// 审批(Review):资质认定与额度审核,不动账。全额批准 or 核减批准(必填核减理由) or 直接驳回(必填理由)。
// 办结(Settlement):资金物理到位确认。单事务原子闭环:
//   行级锁主单 → (按渠道) 锁账户/锁分户 → 扣账 → 计算 Δadvance → 回填分摊 →
//   组装垫资批复 → 守恒强校验 → 置 SETTLED → 写流水/change_events;任何异常整体回滚。
//
// 渠道账务语义:
//   FLEXIBLE  灵活公款实体账户,办结扣减余额,余额不足拒绝(公款不可透支);
//   FACULTY   系报销暂挂=外部应收款,办结增加挂账(班委已垫付、待系财务打款),
//             TO_FACULTY_REIMBURSE 入账核销减少挂账,核销不得超过挂账余额;
//   CLASS_FUND 独立分户,办结按分摊逐人扣减,允许透支为负(生委垫资),守恒恒等式强校验。
#include "recta/Money.hpp"
#include "recta/Roles.hpp"
#include "recta/storage/NeonContext.hpp"

#include <cstdint>
#include <optional>
#include <stdexcept>
#include <string>
#include <vector>

namespace recta::core {

class InvalidRequestStateException final : public std::runtime_error {
public:
    explicit InvalidRequestStateException(const std::string& message)
        : std::runtime_error(message) {}
};

struct SplitPlanInput {
    std::vector<std::string> participant_ids;
    std::string tail_bearer_id;
};

struct StudentAdvanceLine {
    std::string student_id;
    std::string name;
    int64_t amount_cents = 0;
};

struct SettlementResult {
    int request_id = 0;
    std::string category;
    int64_t settled_cents = 0;
    std::vector<StudentAdvanceLine> advances; // 仅班费渠道有值
    int64_t total_advance_cents = 0;
    // 办结后守恒快照(§4.1):Σb = custodian_cash − advance_total。
    int64_t balances_sum_after = 0;
    int64_t custodian_cash_after = 0;
    int64_t advance_total_after = 0;
};

struct InflowInput {
    InflowDestination destination;
    Money amount;                                 // 必须 > 0
    std::string source_title;
    std::optional<std::string> target_student_id; // TO_STUDENT_SUB_ACCOUNT 必填
    std::optional<int> related_request_id;        // TO_FACULTY_REIMBURSE 必填(核销关联)
    std::optional<std::string> voucher_url;
};

class WorkflowService {
public:
    explicit WorkflowService(storage::NeonContext& context) : context_(context) {}

    // 提单(任何在册启用账号可提,§5.1)。班费渠道必须携带平摊名单与尾差承担人。
    [[nodiscard]] int SubmitRequest(const std::string& applicant_id, const std::string& title,
                                    AccountCategory category, Money applied_amount,
                                    const std::optional<SplitPlanInput>& split_plan);

    // 审批:approved < applied 即核减(核减理由必填);approved == applied 全额批准。
    void ApproveRequest(const std::string& actor_id, int request_id, Money approved_amount,
                        const std::optional<std::string>& notes);
    // 直接驳回,理由必填,单据归档终止。
    void RejectRequest(const std::string& actor_id, int request_id, const std::string& notes);

    // 办结出账(§8.4 原子闭环)。extra_notes 由办结人附加,系统自动追加垫资批复。
    [[nodiscard]] SettlementResult SettleRequest(const std::string& actor_id, int request_id,
                                                 const std::optional<std::string>& extra_notes);

    // 入账引擎三通道(§5.3)。操作人权限按渠道硬校验。
    void RecordInflow(const std::string& actor_id, const InflowInput& input);

private:
    storage::NeonContext& context_;
};

// 同学名单(§5.3):团支书录入与改名,其余账号只可查看(查看走存储层 ListAll)。
class RosterService {
public:
    explicit RosterService(storage::NeonContext& context) : context_(context) {}

    void AddStudent(const std::string& actor_id, const std::string& student_id,
                    const std::string& name);
    void RenameStudent(const std::string& actor_id, const std::string& student_id,
                       const std::string& name);

private:
    storage::NeonContext& context_;
};

} // namespace recta::core
