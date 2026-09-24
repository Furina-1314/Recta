using Recta.App.NativeInterop;

namespace Recta.App;

// 进程级服务定位:原生客户端单例 + 当前会话。
// GUI 只经此类与 RectaClient 打交道;所有 P/Invoke 调用建议走 Task.Run 避免阻塞 UI 线程。
public static class AppServices
{
    public static RectaClient Client { get; } = new();

    public static LoginSession? Session { get; private set; }

    public static bool NativeReady { get; private set; }

    public static bool SmokeMode =>
        Environment.GetEnvironmentVariable("RECTA_SMOKE") == "1";

    public static void InitNative()
    {
        try
        {
            Client.Init(null); // 原生侧按 DATABASE_URL / .env.local 装载
            NativeReady = true;
        }
        catch (RectaException)
        {
            NativeReady = false; // 登录窗会给出明确错误
        }
    }

    public static void SetSession(LoginSession session) => Session = session;

    public static void ClearSession() => Session = null;
}
