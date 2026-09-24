namespace Recta.App;

/// <summary>支持被同步事件触发的数据刷新(页面实现后,服务端变更到达时自动重载)。</summary>
public interface IRefreshable
{
    Task RefreshAsync();
}
