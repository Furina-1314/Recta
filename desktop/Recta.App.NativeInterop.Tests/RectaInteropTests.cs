using System.Runtime.InteropServices;
using Recta.App.NativeInterop;
using Xunit;

namespace Recta.App.NativeInterop.Tests;

// 全局上下文(recta_init/shutdown)是进程级单例——两测试类禁并行。
[CollectionDefinition("Sequential", DisableParallelization = true)]
public sealed class SequentialCollection { }

[Collection("Sequential")]
public sealed class DomainInteropTests
{
    [Fact]
    public void VersionReportsCore()
    {
        Assert.StartsWith("Recta native core", RectaClient.Version());
    }

    [Fact]
    public void MoneyFormatAndParseRoundTrip()
    {
        Assert.Equal("25.00", RectaClient.FormatMoney(2500));
        Assert.Equal("0.00", RectaClient.FormatMoney(0));
        Assert.Equal("-0.01", RectaClient.FormatMoney(-1));
        Assert.Equal("140.00", RectaClient.FormatMoney(14000));

        Assert.Equal(2500, RectaClient.ParseMoney("25.00"));
        Assert.Equal(-1, RectaClient.ParseMoney("-0.01"));
        Assert.Equal(14000, RectaClient.ParseMoney("140"));

        var ex = Assert.Throws<RectaException>(() => RectaClient.ParseMoney("1.234"));
        Assert.Equal(RectaErrors.InvalidArg, ex.Code);
        Assert.Contains("小数位", ex.Message); // 原生层错误详情穿透到托管侧
    }

    [Fact]
    public void DistributeMatchesDomainInvariant()
    {
        var result = RectaClient.Distribute(10000, ["s1", "s2", "s3"], "s2");
        Assert.Equal(3, result.Count);
        Assert.Equal(10000, result.SumCents);
        var bearer = result.Allocations.Single(a => a.IsTailBearer);
        Assert.Equal("s2", bearer.StudentId);
        Assert.Equal(3334, bearer.AmountCents);
        Assert.Equal(6666, result.Allocations.Where(a => !a.IsTailBearer).Sum(a => a.AmountCents));

        Assert.Throws<RectaException>(() => RectaClient.Distribute(100, [], "x"));
    }

    [Fact]
    public void UninitializedCallReportsNotReady()
    {
        var client = new RectaClient();
        client.Shutdown(); // 确保未初始化(即使其它测试先行 init 过)
        var ex = Assert.Throws<RectaException>(() => client.Login("anyone", "whatever"));
        Assert.Equal(RectaErrors.NotReady, ex.Code);
    }
}

// 全栈往返:C# → P/Invoke → C ABI → C++ 服务层 → pqxx → Neon recta-test 分支。
// 依赖 recta_dev_truncate_all(RECTA_DEV_TOOLS 构建);DLL 未带开发工具时静默跳过。
[Collection("Sequential")]
public sealed class FullStackInteropTests : IDisposable
{
    private readonly RectaClient _client = new();

    private static bool TruncateIfPossible()
    {
        try
        {
            return RectaNativeUnsafe.recta_dev_truncate_all() >= 0;
        }
        catch (EntryPointNotFoundException)
        {
            return false; // 发布构建未暴露开发工具
        }
    }

    [Fact]
    public void EndToEndWorkflowRoundTrip()
    {
        _client.InitTest();
        if (!TruncateIfPossible())
        {
            return;
        }

        // 1) 引导 + 登录 + 强制改密。
        var temp = _client.BootstrapSecretary("csharp_sec", "C#测试团支书");
        var session = _client.Login("csharp_sec", temp);
        Assert.True(session.MustChangePassword);
        _client.ChangePassword(session.UserId, temp, "CsPass1234!");
        var stable = _client.Login("csharp_sec", "CsPass1234!");
        Assert.False(stable.MustChangePassword);
        Assert.Equal("BRANCH_SECRETARY", stable.Role);

        // 2) 团支书开号(生活委员)+ 录入名单 + 预存。
        var lifeTemp = _client.CreateUser(stable.UserId, "u_life_cs", "csharp_life", "C#生活委员",
                                          "LIFE_COMMITTEE");
        Assert.Equal(12, lifeTemp.Length);
        _client.AddStudent(stable.UserId, "s1", "陈一");
        _client.AddStudent(stable.UserId, "s2", "钱二");
        _client.RecordInflow("u_life_cs", "TO_STUDENT_SUB_ACCOUNT", 3000, "陈一班费预存", "s1");

        // 3) 班费提单 → 团支书核减 → 生活委员办结(垫资自动披露)。
        var requestId = _client.SubmitRequest("u_life_cs", "C# 全栈测试采购", "CLASS_FUND", 10000,
                                              ["s1", "s2"], "s1");
        _client.ApproveRequest(stable.UserId, requestId, 9999, "核减 0.01 元");
        var outcome = _client.SettleRequest("u_life_cs", requestId, "全栈办结");

        Assert.Equal(9999, outcome.SettledCents);
        // s1: 30 − 50 = −20;s2: 0 − 49.99 = −49.99 → 垫资合计 69.99。
        Assert.Equal(6999, outcome.TotalAdvanceCents);
        Assert.Equal(-6999, outcome.Custody.BalancesSumCents);
        Assert.Equal(outcome.Custody.CustodianCashCents - outcome.Custody.AdvanceTotalCents,
                     outcome.Custody.BalancesSumCents); // Σb = C − A

        // 4) 查询面:总览守恒、台账、单据详情(分摊联查姓名)、变更事件。
        var overview = _client.GetOverview();
        Assert.True(overview.Custody.Conserved);
        Assert.Equal(-6999, overview.Custody.BalancesSumCents);

        var settledRequests = _client.ListRequests(status: "SETTLED");
        Assert.Contains(settledRequests, r => r.Id == requestId);

        var bundle = _client.GetRequest(requestId);
        Assert.Equal(9999, bundle.Splits.Sum(s => s.AmountCents));
        Assert.Contains(bundle.Splits, s => s.StudentName == "陈一" && s.IsTailBearer);

        var batch = _client.FetchChangeEvents(0);
        Assert.NotEmpty(batch.Events);
        Assert.True(batch.MaxSeq > 0);
        Assert.Contains(batch.Events, e => e.EventType == "SETTLED");
    }

    public void Dispose() => _client.Shutdown();
}

// dev 工具直接声明(非公开面):发布构建 DLL 中不存在该入口。
internal static partial class RectaNativeUnsafe
{
    [LibraryImport("recta_capi")]
    internal static partial int recta_dev_truncate_all();
}
