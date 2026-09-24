using Avalonia.Threading;
using Recta.App.NativeInterop;

namespace Recta.App;

// 增量同步的 GUI 侧驾驶:定时 drain 原生队列 + 订阅状态/事件。
// 原生层负责 LISTEN/NOTIFY 轻通知、增量拉取与退避重连;这里只做调度与转发。
// 必须在 UI 线程调用 Start(内部使用 DispatcherTimer)。
public static class SyncController
{
    private static DispatcherTimer? _timer;

    /// <summary>drain 到新事件(另一操作端提交的变更)时触发(UI 线程)。</summary>
    public static event EventHandler<IReadOnlyList<ChangeEventDto>>? EventsReceived;

    /// <summary>同步状态轮询结果(UI 线程,约 3s 一次)。</summary>
    public static event EventHandler<SyncStatusDto>? StatusChanged;

    public static void Start()
    {
        if (!AppServices.NativeReady || _timer is not null)
        {
            return;
        }

        try
        {
            AppServices.Client.SyncStart();
        }
        catch (RectaException)
        {
            return; // 同步失败不阻断主流程
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += async (_, _) => await PollAsync();
        _timer.Start();
    }

    public static void Stop()
    {
        _timer?.Stop();
        _timer = null;
        try
        {
            AppServices.Client.SyncStop();
        }
        catch (RectaException)
        {
            // 退出路径,忽略
        }
    }

    private static async Task PollAsync()
    {
        if (_timer is null)
        {
            return;
        }

        try
        {
            var status = await Task.Run(() => AppServices.Client.SyncStatus());
            // 连接门控校正:监听中/正常轮转视为在线;重连退避视为离线。
            ConnectionGate.SetOnline(status.Listening || status.ReconnectAttempts == 0);
            StatusChanged?.Invoke(null, status);

            var drained = await Task.Run(() => AppServices.Client.SyncDrain());
            if (drained.Events.Count > 0)
            {
                EventsReceived?.Invoke(null, drained.Events);
            }
        }
        catch (RectaException ex)
        {
            if (ex.IsDatabase)
            {
                ConnectionGate.SetOnline(false); // 拉取失败:离线,等待原生层退避自愈
            }
        }
    }
}
