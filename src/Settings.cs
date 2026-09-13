using STS2RitsuLib.Data;
using RebalancedSpire.Core.Configs;

namespace AutoRebalancedSpire;

/// <summary>
/// RebalancedSpire 的开关，读一次缓存一次。
/// </summary>
/// <remarks>
/// **不要**在热路径上直接用 <c>RebalancedSpireSettingsStore.Settings</c>。那个属性每读一次都是：
///
/// <code>
/// ModDataStore.For("RebalancedSpire").CreateCache&lt;RebalancedSpireSettings&gt;("settings").Value
/// </code>
///
/// 而 <c>CreateCache</c> 是字面意义上的 <c>new ModDataStoreCache&lt;T&gt;(...)</c>：每次调用都新建一个
/// 缓存对象、一把锁，并且**往数据仓库的 <c>EntryReloaded</c> 事件上再挂一个处理器**，还从不释放。
/// 于是每读一次开关就多几笔分配、事件订阅列表长一截 —— 越跑越慢，而且是复利。
///
/// RebalancedSpire 自己没事，它只在每个补丁类的 <c>static readonly bool Disabled</c> 里读一次；
/// 踩坑的是我们：出牌、怪物出招、意图预测这些每秒成千上万次的路径上都在读。
/// 实测一次四回合的搜索，光这一项就把分配量从 66 MB 推到 644 MB。
///
/// 这里自己建**一个**缓存并留住它 —— 这正是 RitsuLib 设计的用法：缓存对象会订阅重载事件，
/// 配置被换掉时自动失效，所以既省了开销，又没有丢掉「运行中改了开关能看见」这条性质。
/// </remarks>
internal static class AdapterSettings
{
    private static ModDataStoreCache<RebalancedSpireSettings>? _cache;

    /// <summary>建好那一个缓存。必须在装任何补丁之前调用。</summary>
    public static void Initialize()
        => _cache ??= ModDataStore.For(PinnedTargets.RebalancedSpireModId)
            .CreateCache<RebalancedSpireSettings>("settings");

    /// <summary>当前开关。热路径上用这个，不要用 <c>RebalancedSpireSettingsStore.Settings</c>。</summary>
    public static RebalancedSpireSettings Current => _cache is { } cache
        ? cache.Value
        : RebalancedSpireSettingsStore.Settings;
}
