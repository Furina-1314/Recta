using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Recta.App;

// UWP 式页面进入过渡:自右侧 32px 滑入 + 淡入(EntranceNavigationTransition 的桌面近似)。
public static class PageAnimation
{
    public static void SlideIn(Control page)
    {
        var translate = new TranslateTransform(32, 0);
        page.RenderTransform = translate;
        page.Opacity = 0;

        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(220),
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
        };

        var start = new KeyFrame { Cue = new Cue(0d) };
        start.Setters.Add(new Setter(Visual.OpacityProperty, 0d));
        start.Setters.Add(new Setter(TranslateTransform.XProperty, 32d));

        var end = new KeyFrame { Cue = new Cue(1d) };
        end.Setters.Add(new Setter(Visual.OpacityProperty, 1d));
        end.Setters.Add(new Setter(TranslateTransform.XProperty, 0d));

        animation.Children.Add(start);
        animation.Children.Add(end);
        _ = animation.RunAsync(page);
    }
}
