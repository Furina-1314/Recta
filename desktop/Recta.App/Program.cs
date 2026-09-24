using Avalonia;

namespace Recta.App;

internal static class Program
{
    // 初始化代码请勿放入 App 主构造器之前——Avalonia 需要先就绪设计器/生命周期。
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
