using Recta.App.NativeInterop;

namespace Recta.App;

// 进程级服务定位:原生客户端单例 + 当前会话。
// GUI 只经此类与 RectaClient 打交道;所有 P/Invoke 调用建议走 Task.Run 避免阻塞 UI 线程。
public static class AppServices
{
    public static RectaClient Client { get; } = new();

    public static LoginSession? Session { get; private set; }

    public static bool NativeReady { get; private set; }

    /// <summary>原生初始化失败的原因(缺 VC++ 运行库/DLL 损坏等),登录窗据此给出可见错误。</summary>
    public static string? NativeInitError { get; private set; }

    public static bool SmokeMode =>
        Environment.GetEnvironmentVariable("RECTA_SMOKE") == "1";

    // 由 launch-test.bat 设置:标题栏据此显示【测试库】,防止测试/生产窗口认错。
    public static bool TestMode =>
        Environment.GetEnvironmentVariable("RECTA_TEST_MODE") == "1";

    // 冒烟模式下改连 recta-test 测试分支(演示数据),不影响生产。
    public static bool SmokeUseTestDb =>
        SmokeMode && Environment.GetEnvironmentVariable("RECTA_SMOKE_TESTDB") == "1";

    // 冒烟模式下启动后直达的导航页(如 requests/audit)。
    public static string? SmokePage =>
        SmokeMode ? Environment.GetEnvironmentVariable("RECTA_SMOKE_PAGE") : null;

    public static void InitNative()
    {
        try
        {
            if (SmokeUseTestDb)
            {
                Client.InitTest();
            }
            else
            {
                Client.Init(null); // 原生侧按 DATABASE_URL / .env.local 装载
            }
            NativeReady = true;
        }
        catch (RectaException ex)
        {
            NativeReady = false; // 登录窗会给出明确错误
            NativeInitError = ex.Message;
        }
        catch (Exception ex)
        {
            // recta_capi.dll 装载失败(缺 VC++ 运行库/文件损坏)等非领域异常:
            // 不能让启动静默崩溃——记日志,登录窗呈现原因。
            NativeReady = false;
            NativeInitError = $"原生核心加载失败:{ex.Message}";
            CrashLog.Append(ex);
        }
    }

    public static void SetSession(LoginSession session) => Session = session;

    public static void ClearSession() => Session = null;
}
