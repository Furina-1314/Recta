using System.Reflection;
using System.Runtime.InteropServices;

namespace Recta.App;

// 崩溃/异常日志:%LOCALAPPDATA%\Recta\logs\crash.log。
// 不依赖 Avalonia——在 AppBuilder 启动失败等最早的阶段也要能落盘。
public static class CrashLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Recta", "logs", "crash.log");

    [DllImport("user32.dll", SetLastError = false)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);

    public static void Append(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  [{AppVersion}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n\n");
        }
        catch
        {
            // 日志失败不影响主流程
        }
    }

    /// <summary>启动期致命错误:落盘 + Win32 原生弹窗(此时没有可用的 UI 框架)。</summary>
    public static void Fatal(string title, Exception ex)
    {
        Append(ex);
        try
        {
            MessageBoxW(nint.Zero,
                $"{ex.GetType().Name}: {ex.Message}\n\n详细信息已写入:\n{LogPath}",
                title, 0x10 /* MB_ICONERROR */);
        }
        catch
        {
            // 无 GUI 环境下弹窗失败,仅保留日志
        }
    }

    /// <summary>应用版本(来自程序集 InformationalVersion,发布时经 -p:Version 写入)。</summary>
    public static string AppVersion { get; } =
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "0.0.0";
}
