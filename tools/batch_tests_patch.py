# -*- coding: utf-8 -*-
import os
base = r'E:\code\Recta'

# 1) C# Native
p = base + r'\desktop\Recta.App.NativeInterop\RectaNative.cs'
s = open(p, encoding='utf-8').read()
if 'recta_record_inflow_batch' not in s:
    old = '''    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int recta_record_inflow(string actor_id, string inflow_json);'''
    new = old + '''

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int recta_record_inflow_batch(string actor_id, string source_title,
                                                          long amount_cents, string? voucher_url,
                                                          string student_ids_json,
                                                          out int out_count);'''
    assert old in s, 'native anchor'
    s = s.replace(old, new)
open(p, 'w', encoding='utf-8').write(s)
print('native cs ok')

# 2) C# Client
p = base + r'\desktop\Recta.App.NativeInterop\RectaClient.cs'
s = open(p, encoding='utf-8').read()
if 'RecordInflowBatch' not in s:
    old = '''    public void AddStudent(string actorId, string studentId, string name)'''
    new = '''    /// <summary>班费批量充值:单事务内为多名同学各充等额一笔(原子)。返回实际人数。</summary>
    public int RecordInflowBatch(string actorId, string sourceTitle, long amountCents,
                                 string? voucherUrl, IReadOnlyList<string> studentIds)
    {
        var idsJson = JsonSerializer.Serialize(studentIds);
        var rc = RectaNative.recta_record_inflow_batch(actorId, sourceTitle, amountCents,
                                                       voucherUrl, idsJson, out var count);
        return rc >= 0 ? count : throw ToException(rc);
    }

    public void AddStudent(string actorId, string studentId, string name)'''
    assert old in s, 'client anchor'
    s = s.replace(old, new)
open(p, 'w', encoding='utf-8').write(s)
print('client cs ok')

# 3) C++ WorkflowServiceTests: 批量充值用例
p = base + r'\native\Recta.Core.Tests\src\WorkflowServiceTests.cpp'
s = open(p, encoding='utf-8').read()
if 'RecordInflowBatch' not in s:
    anchor = 'TEST_F(WorkflowTest, RosterPermissionsAndRechargeRecovery) {'
    test = ('''TEST_F(WorkflowTest, InflowBatchRechargesAtomically) {
    // 生活委员给 s2、s3 各充 10.00(单事务原子)。
    const auto count = workflow_->RecordInflowBatch("u_life_01", "期中班费集中收取",
                                                    recta::Money(1000), std::nullopt,
                                                    {"s2", "s3"});
    EXPECT_EQ(count, 2);

    const auto balances = context_->ExecuteTransaction([](pqxx::work& tx) {
        return recta::storage::StudentAccountsRepo::ListAll(tx);
    });
    const auto find = [&balances](const std::string& id) {
        return *std::find_if(balances.begin(), balances.end(),
                             [&](const recta::storage::StudentAccountRow& row) {
                                 return row.student_id == id;
                             });
    };
    EXPECT_EQ(find("s1").balance_cents, 1500);  // 未参充,保持 15.00
    EXPECT_EQ(find("s2").balance_cents, 1000);
    EXPECT_EQ(find("s3").balance_cents, 1000);

    // 原子性:含幽灵学号的批量整体回滚,s1 余额不变。
    EXPECT_THROW((void)workflow_->RecordInflowBatch(
                     "u_life_01", "含幽灵学号", recta::Money(500), std::nullopt,
                     std::vector<std::string>{"s1", "ghost"}),
                 std::invalid_argument);
    const auto after_rollback = context_->ExecuteTransaction([](pqxx::work& tx) {
        return recta::storage::StudentAccountsRepo::Find(tx, "s1");
    });
    EXPECT_EQ(after_rollback->balance_cents, 1500);

    // 重复学号拒绝;空名单拒绝;非生活委员拒绝。
    EXPECT_THROW((void)workflow_->RecordInflowBatch("u_life_01", "重复", recta::Money(100),
                                                    std::nullopt, {"s1", "s1"}),
                 std::invalid_argument);
    EXPECT_THROW((void)workflow_->RecordInflowBatch("u_life_01", "空名单", recta::Money(100),
                                                    std::nullopt, {}),
                 std::invalid_argument);
    EXPECT_THROW((void)workflow_->RecordInflowBatch("u_sec_01", "越权", recta::Money(100),
                                                    std::nullopt, {"s1"}),
                 recta::PermissionDeniedException);
}

''' + anchor)
    assert anchor in s, 'test anchor'
    s = s.replace(anchor, test)
    # includes: algorithm
    if '#include <algorithm>' not in s:
        s = s.replace('#include <pqxx/pqxx>', '#include <pqxx/pqxx>\n\n#include <algorithm>')
open(p, 'w', encoding='utf-8').write(s)
print('tests ok')
