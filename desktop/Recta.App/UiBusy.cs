using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Recta.App;

// 动作按钮防重闸:点击"提交/核准出账/驳回/办结"等按钮后,scope 内所有按钮
// (含模板生成的行内按钮)立即置灰,数据库处理完毕后恢复原状——
// 杜绝连点造成的重复提交/重复审批(第二次点击在按钮已禁用后根本不会派发)。
// 快照恢复:原本就因业务状态禁用的按钮不会被误启用;嵌套调用只在最外层结束才恢复。
public static class UiBusy
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control,
        System.Runtime.CompilerServices.StrongBox<int>> Depths = new();

    public static async Task RunAsync(Control scope, Func<Task> operation)
    {
        var depth = Depths.GetOrCreateValue(scope);
        List<(Button Button, bool WasEnabled)>? snapshot = null;
        if (Interlocked.Increment(ref depth.Value) == 1)
        {
            snapshot = scope.GetVisualDescendants().OfType<Button>()
                .Select(b => (b, b.IsEnabled)).ToList();
            foreach (var (button, _) in snapshot)
            {
                button.IsEnabled = false;
            }
        }
        try
        {
            await operation();
        }
        finally
        {
            if (Interlocked.Decrement(ref depth.Value) == 0 && snapshot is not null)
            {
                foreach (var (button, wasEnabled) in snapshot)
                {
                    button.IsEnabled = wasEnabled;
                }
            }
        }
    }
}
