using Avalonia;
using Avalonia.Win32;
using Recta.App;

// 启动自愈:显卡驱动异常(个别新卡/混合 CPU+GPU 机型)可能让 Avalonia 的 GPU 渲染初始化
// 在窗口出现之前就抛异常退出——表现为"双击后转圈、无窗口、无进程"。
// 首次启动失败时记崩溃日志,并以纯软件渲染重启一次;再失败则弹原生错误框给出原因。
// 另可用 RECTA_RENDER=software 强制软件渲染。
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                CrashLog.Append(ex);
            }
        };

        if (Environment.GetEnvironmentVariable("RECTA_RENDER") == "software")
        {
            return Start(args, softwareRender: true);
        }

        try
        {
            return Start(args, softwareRender: false);
        }
        catch (Exception ex)
        {
            CrashLog.Append(ex);
            Console.Error.WriteLine(ex);
            // GPU/驱动类启动失败:退回软件渲染再试一次。
            return Start(args, softwareRender: true);
        }
    }

    private static int Start(string[] args, bool softwareRender)
    {
        try
        {
            return BuildAvaloniaApp(softwareRender).StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            CrashLog.Fatal("Recta 矩衡 启动失败", ex);
            return unchecked((int)0xE0000001);
        }
    }

    public static AppBuilder BuildAvaloniaApp(bool softwareRender)
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
        if (softwareRender)
        {
            builder = builder.With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.Software],
            });
        }
        return builder;
    }
}
