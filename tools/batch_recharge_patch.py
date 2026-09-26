# -*- coding: utf-8 -*-
import os
base = r'E:\code\Recta'

# 1) WorkflowService.hpp: 批量充值声明
p = base + r'\native\Recta.Core\include\recta\core\WorkflowService.hpp'
s = open(p, encoding='utf-8').read()
if 'RecordInflowBatch' not in s:
    s = s.replace('#include <optional>', '#include <optional>\n#include <vector>')
    old = '    // 入账引擎三通道(§5.3)。操作人权限按渠道硬校验。\n    void RecordInflow(const std::string& actor_id, const InflowInput& input);'
    new = old + ('\n\n    // 班费批量充值:单事务内为多名同学各记一笔等额充值(任一失败整体回滚)。\n'
                 '    // 仅生活委员;学号不可重复;返回实际充值人数。\n'
                 '    [[nodiscard]] int RecordInflowBatch(const std::string& actor_id, const std::string& source_title,\n'
                 '                                        Money amount, const std::optional<std::string>& voucher_url,\n'
                 '                                        const std::vector<std::string>& student_ids);')
    assert old in s, 'hpp anchor'
    s = s.replace(old, new)
open(p, 'w', encoding='utf-8').write(s)
print('hpp ok')

# 2) WorkflowService.cpp
p = base + r'\native\Recta.Core\src\WorkflowService.cpp'
s = open(p, encoding='utf-8').read()

if 'ApplyStudentRecharge' not in s:
    anchor = 'std::string FormatYuan(int64_t cents) {\n    return Money(cents).to_plain_string();\n}'
    helper = anchor + ('\n\n// 班费充值单笔入账(调用方负责:操作者校验/分户存在性/守恒校验)。\n'
        'void ApplyStudentRecharge(pqxx::work& tx, const std::string& student_id, Money amount,\n'
        '                          const std::string& source_title,\n'
        '                          const std::optional<std::string>& voucher_url,\n'
        '                          const std::string& operator_id) {\n'
        '    const int64_t amount_cents = amount.to_cents();\n'
        '    const int64_t balance_after =\n'
        '        storage::StudentAccountsRepo::ApplyCredit(tx, student_id, amount_cents);\n\n'
        '    const int64_t inflow_id = storage::LedgerRepo::InsertInflow(\n'
        '        tx, amount_cents, source_title, ToString(InflowDestination::ToStudentSubAccount),\n'
        '        student_id, std::nullopt, operator_id, voucher_url);\n\n'
        '    storage::LedgerEntry entry;\n'
        '    entry.student_id = student_id;\n'
        '    entry.inflow_record_id = inflow_id;\n'
        '    entry.entry_type = "RECHARGE";\n'
        '    entry.change_cents = amount_cents;\n'
        '    entry.balance_after_cents = balance_after;\n'
        '    entry.notes = source_title;\n'
        '    storage::LedgerRepo::AppendLedger(tx, entry);\n'
        '    AppendChangeEvent(tx, "inflow", inflow_id, "CREATED",\n'
        '                      std::format(R"({{"inflow_id":{},"amount_cents":{},"student":"{}"}})",\n'
        '                                  inflow_id, amount_cents, student_id));\n}')
    assert anchor in s, 'helper anchor'
    s = s.replace(anchor, helper)

old2 = ('''        if (input.destination == InflowDestination::ToStudentSubAccount) {
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
        } else {''')
new2 = ('''        if (input.destination == InflowDestination::ToStudentSubAccount) {
            // 定向充值:直接平复负数透支,解除对应债权(§1 核心原则 2)。
            if (!storage::StudentAccountsRepo::Find(tx, *input.target_student_id)) {
                throw std::invalid_argument("目标分户不存在: " + *input.target_student_id);
            }
            ApplyStudentRecharge(tx, *input.target_student_id, input.amount, input.source_title,
                                 input.voucher_url, actor_id);

            const auto custody = storage::StudentAccountsRepo::AggregateCustody(tx);
            if (!IsConserved(Money(custody.balances_sum), Money(custody.custodian_cash),
                             Money(custody.advance_total))) {
                throw std::logic_error("充值后守恒校验失败，事务回滚");
            }
        } else {''')
assert old2 in s, 'single branch mismatch'
s = s.replace(old2, new2)

if 'RecordInflowBatch' not in s.split('ApplyStudentRecharge')[-1]:
    anchor2 = 'void RosterService::AddStudent(const std::string& actor_id, const std::string& student_id,'
    batch = ('int WorkflowService::RecordInflowBatch(const std::string& actor_id, const std::string& source_title,\n'
             '                                       Money amount, const std::optional<std::string>& voucher_url,\n'
             '                                       const std::vector<std::string>& student_ids) {\n'
             '    if (amount.to_cents() <= 0) {\n'
             '        throw std::invalid_argument("充值金额必须为正数(分)");\n'
             '    }\n'
             '    if (source_title.empty()) {\n'
             '        throw std::invalid_argument("入账来源凭据必填(§5.3)");\n'
             '    }\n'
             '    if (student_ids.empty()) {\n'
             '        throw std::invalid_argument("请勾选至少一名充值对象");\n'
             '    }\n'
             '    if (std::unordered_set<std::string>(student_ids.begin(), student_ids.end()).size() !=\n'
             '        student_ids.size()) {\n'
             '        throw std::invalid_argument("充值对象存在重复学号");\n'
             '    }\n\n'
             '    return context_.ExecuteTransaction([&](pqxx::work& tx) -> int {\n'
             '        const auto actor = RequireActiveUser(tx, actor_id);\n'
             '        if (RoleOf(actor) != Role::LifeCommittee) {\n'
             '            throw PermissionDeniedException("权限拒绝：同学班费充值仅生活委员可确认");\n'
             '        }\n'
             '        // 先校验全部分户存在,再逐笔入账(任一不存在则整体失败)。\n'
             '        for (const auto& id : student_ids) {\n'
             '            if (!storage::StudentAccountsRepo::Find(tx, id)) {\n'
             '                throw std::invalid_argument("目标分户不存在: " + id);\n'
             '            }\n'
             '        }\n'
             '        for (const auto& id : student_ids) {\n'
             '            ApplyStudentRecharge(tx, id, amount, source_title, voucher_url, actor_id);\n'
             '        }\n\n'
             '        const auto custody = storage::StudentAccountsRepo::AggregateCustody(tx);\n'
             '        if (!IsConserved(Money(custody.balances_sum), Money(custody.custodian_cash),\n'
             '                         Money(custody.advance_total))) {\n'
             '            throw std::logic_error("批量充值后守恒校验失败，事务回滚");\n'
             '        }\n'
             '        return static_cast<int>(student_ids.size());\n'
             '    });\n'
             '}\n\n')
    assert anchor2 in s, 'batch anchor'
    s = s.replace(anchor2, batch + anchor2)

if '#include <unordered_set>' not in s:
    s = s.replace('#include <optional>', '#include <optional>\n#include <unordered_set>')
open(p, 'w', encoding='utf-8').write(s)
print('workflow cpp ok')

# 3) CAPI 头 + 实现
p = base + r'\native\Recta.CApi\include\recta\capi\recta_capi.h'
s = open(p, encoding='utf-8').read()
if 'recta_record_inflow_batch' not in s:
    old = 'RECTA_API int32_t recta_record_inflow(const char* actor_id, const char* inflow_json);'
    new = old + ('\n/* 班费批量充值:单事务内为 ids_json 中每名同学各充等额一笔(原子)。返回实际人数。 */\n'
                 'RECTA_API int32_t recta_record_inflow_batch(const char* actor_id, const char* source_title,\n'
                 '                                            int64_t amount_cents, const char* voucher_url,\n'
                 '                                            const char* student_ids_json, int32_t* out_count);')
    assert old in s, 'capi h anchor'
    s = s.replace(old, new)
open(p, 'w', encoding='utf-8').write(s)
print('capi h ok')

p = base + r'\native\Recta.CApi\src\RectaCApi.cpp'
s = open(p, encoding='utf-8').read()
if 'recta_record_inflow_batch' not in s:
    anchor = 'int32_t recta_add_student(const char* actor_id, const char* student_id, const char* name) {'
    impl = ('int32_t recta_record_inflow_batch(const char* actor_id, const char* source_title,\n'
            '                                  int64_t amount_cents, const char* voucher_url,\n'
            '                                  const char* student_ids_json, int32_t* out_count) {\n'
            '    return Call([&] {\n'
            '        RequireReady();\n'
            '        if (out_count == nullptr) throw std::invalid_argument("out_count 为空");\n'
            '        const json ids = json::parse(ReqStr(student_ids_json));\n'
            '        if (!ids.is_array()) throw std::invalid_argument("student_ids_json 必须是字符串数组");\n'
            '        std::vector<std::string> student_ids;\n'
            '        for (const auto& item : ids) {\n'
            '            if (!item.is_string()) throw std::invalid_argument("student_ids_json 必须是字符串数组");\n'
            '            student_ids.push_back(item.get<std::string>());\n'
            '        }\n'
            '        *out_count = Svc()->workflow->RecordInflowBatch(\n'
            '            ReqStr(actor_id), ReqStr(source_title), recta::Money(amount_cents),\n'
            '            OptStr(voucher_url), student_ids);\n'
            '    });\n'
            '}\n\n')
    assert anchor in s, 'capi cpp anchor'
    s = s.replace(anchor, impl + anchor)
open(p, 'w', encoding='utf-8').write(s)
print('capi cpp ok')
