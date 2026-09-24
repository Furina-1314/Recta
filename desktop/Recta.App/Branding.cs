using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Recta.App;

// 品牌资产:窗口图标(Assets/RectaLogo-256.png,与 Equora 同构的蓝底白 R)。
public static class Branding
{
    private static WindowIcon? _icon;

    public static void ApplyIcon(Window window)
    {
        _icon ??= new WindowIcon(
            AssetLoader.Open(new Uri("avares://Recta.App/Assets/RectaLogo-256.png")));
        window.Icon = _icon;
    }
}
