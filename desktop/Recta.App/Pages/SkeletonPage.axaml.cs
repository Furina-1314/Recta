using Avalonia.Controls;

namespace Recta.App.Pages;

// P8+ 逐页替换为功能页前的占位骨架:统一带标题/说明/图标。
public partial class SkeletonPage : UserControl
{
    public SkeletonPage()
    {
        InitializeComponent();
    }

    public SkeletonPage(string title, string note, string glyph = "\uE7BA")
    {
        InitializeComponent();
        TitleText.Text = title;
        NoteText.Text = note;
        GlyphText.Text = glyph;
    }
}
