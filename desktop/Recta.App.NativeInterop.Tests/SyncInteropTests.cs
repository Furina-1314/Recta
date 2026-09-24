using Recta.App.NativeInterop;
using Xunit;

namespace Recta.App.NativeInterop.Tests;

// 增量同步双轨验收:监听连接(非池化端点)LISTEN recta_changes;
// 本进程经池化连接写入 → 事务提交即 NOTIFY → 原生工作线程增量拉取入队 → drain 收敛。
[Collection("Sequential")]
public sealed class SyncInteropTests : IDisposable
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
            return false;
        }
    }

    [Fact]
    public async Task NotifyReachesDrainAndCursorAdvances()
    {
        _client.InitTest();
        if (!TruncateIfPossible())
        {
            return; // 发布构建无开发工具,跳过
        }

        var temp = _client.BootstrapSecretary("sync_sec", "同步测试团支书");
        _client.ChangePassword("u_sec_01", temp, "SyncPass123!");

        _client.SyncStart();
        try
        {
            // 游标对齐后应无积压事件。
            var baseline = _client.SyncDrain();
            Assert.Equal(0, baseline.Events.Count);

            var status = _client.SyncStatus();
            Assert.True(status.Running);

            // 他端写入:录入同学(产生 change_events + NOTIFY recta_changes)。
            _client.AddStudent("u_sec_01", "sync_s1", "同步测试同学");

            // 通知即拉、~15s 兜底拉——最多等 32s。
            var received = false;
            for (var i = 0; i < 16 && !received; i++)
            {
                await Task.Delay(2000);
                var drained = _client.SyncDrain();
                if (drained.Events.Count > 0)
                {
                    received = true;
                    Assert.Contains(drained.Events, e => e.EntityType == "student");
                    Assert.All(drained.Events, e => Assert.True(e.Seq > baseline.Cursor));
                    var after = _client.SyncStatus();
                    Assert.True(after.Cursor >= drained.Cursor);
                }
            }
            Assert.True(received, "32s 内未收到 NOTIFY/兜底拉取的事件——增量同步未收敛");
        }
        finally
        {
            _client.SyncStop();
        }

        var stopped = _client.SyncStatus();
        Assert.False(stopped.Running);
    }

    public void Dispose()
    {
        try
        {
            _client.SyncStop();
        }
        catch
        {
            // 忽略
        }
        _client.Shutdown();
    }
}
