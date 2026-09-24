using Recta.App.NativeInterop;

namespace Recta.App;

// 连接门控与忙态中心:
//   · 事务/查询统一走 RunAsync——离线立即报错,慢查询限时(默认 15s)并在忙态灯呈现;
//   · 每次成功即视为在线,数据库错误/超时置离线(由同步状态持续校正);
//   · Friendly 把领域层技术文案翻译为面向普通用户的表述。
public static class ConnectionGate
{
    private static int _busyCount;

    public static volatile bool IsOnline = true;

    /// <summary>忙态变化(UI 线程)。忙 = 至少一个数据库操作进行中。</summary>
    public static event EventHandler? BusyChanged;

    public static bool IsBusy => Volatile.Read(ref _busyCount) > 0;

    public static void SetOnline(bool online)
    {
        if (IsOnline == online)
        {
            return;
        }
        IsOnline = online;
    }

    private static void EnterBusy()
    {
        Interlocked.Increment(ref _busyCount);
        if (Volatile.Read(ref _busyCount) == 1)
        {
            BusyChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    private static void ExitBusy()
    {
        if (Interlocked.Decrement(ref _busyCount) == 0)
        {
            BusyChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static async Task<T> RunAsync<T>(Func<T> operation, int timeoutMs = 15000)
    {
        if (!IsOnline)
        {
            throw new RectaException(RectaErrors.Db, "当前处于离线状态，请等待连接恢复后再试。");
        }

        EnterBusy();
        try
        {
            var result = await Task.Run(operation).WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));
            IsOnline = true;
            return result;
        }
        catch (TimeoutException)
        {
            IsOnline = false;
            throw new RectaException(RectaErrors.Db, $"连接超时（{timeoutMs / 1000} 秒），请检查网络后重试。");
        }
        catch (RectaException ex)
        {
            if (ex.IsDatabase)
            {
                IsOnline = false;
            }
            throw;
        }
        finally
        {
            ExitBusy();
        }
    }

    public static Task RunAsync(Action operation, int timeoutMs = 15000) =>
        RunAsync<object?>(() =>
        {
            operation();
            return null;
        }, timeoutMs);

    public static string Friendly(RectaException ex)
    {
        return ex.Code switch
        {
            RectaErrors.Permission => "您没有执行该操作的权限，请联系团支书。",
            RectaErrors.Auth => "身份校验失败，请重新登录。",
            RectaErrors.State => "该单据当前状态不允许此操作（可能已被其他人处理），请刷新后查看。",
            RectaErrors.WeakPassword => ex.Message,
            RectaErrors.Db => IsOnline
                ? ex.Message
                : "网络或数据库连接异常，请检查网络后重试。",
            RectaErrors.NotReady => "本地服务未就绪，请重启应用。",
            RectaErrors.Logic => "系统账务校验未通过，操作已取消：" + ex.Message,
            RectaErrors.InvalidArg => CleanArgumentMessage(ex.Message),
            _ => ex.Message,
        };
    }

    // 参数类错误去掉内部条款号,翻译枚举词。
    private static string CleanArgumentMessage(string message)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(message, @"[(（]?§[\d.]+[)）]?\s*", "");
        return text.Replace("PENDING_REVIEW", "待审理")
                   .Replace("APPROVED", "待办结")
                   .Replace("SETTLED", "已办结")
                   .Replace("REJECTED", "已驳回")
                   .Replace("FLEXIBLE", "灵活公款")
                   .Replace("FACULTY", "系里报销")
                   .Replace("CLASS_FUND", "班费平摊");
    }
}
