using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Orb;
using CombatSolver.Engine.InCombat.Simulation;
using RebalancedSpire.Core.Powers;

namespace AutoRebalancedSpire;

/// <summary>同步+ 记过哪些球 id 已经给过集中。原版把这张表放在 Power 的内部数据里。</summary>
internal sealed class ChanneledOrbState : IPredictionStateForkable
{
    public HashSet<string> Ids { get; private set; } = [];

    public object Fork(PredictionForkContext context)
        => new ChanneledOrbState { Ids = [.. Ids] };
}

/// <summary>吞噬暗影+ 记上一次暗球引爆算出来的加成。原版把它放在 Power 的私有字段里。</summary>
internal sealed class LastEvokedState(decimal value) : IPredictionStateForkable
{
    public decimal Value { get; set; } = value;

    public object Fork(PredictionForkContext context) => MemberwiseClone();
}

/// <summary>
/// 两个和球打交道、而且带隐藏状态的新 Power。
/// </summary>
/// <remarks>
/// 隐藏状态必须进指纹，否则求解器会把「已经给过集中」和「还没给过」的两个局面当成同一个，
/// 续接和剪枝都会错 —— 这正是求解器 0.33.0 开 <c>PowerHiddenStateMirrors</c> 的原因。
/// 根状态从实机的 Power 上播种：同步+ 读它内部那张球 id 表，吞噬暗影+ 读它的私有字段。
/// </remarks>
internal static class OrbPowerMirrors
{
    public static int RegisterAll()
    {
        AfterOrbChanneledMirrors.Registry.Register<SynchronizePlusPower>(SynchronizePlus);
        AfterOrbChanneledMirrors.Registry.Register<ConsumingShadowPlusPower>(ConsumingShadowChanneled);
        AfterOrbEvokedMirrors.Registry.Register<ConsumingShadowPlusPower>(ConsumingShadowEvoked);

        PowerHiddenStateMirrors.Register<SynchronizePlusPower>(
            "ChanneledOrbIds",
            static (simulator, power) => Channeled(simulator, power).Ids.Count);
        PowerHiddenStateMirrors.Register<ConsumingShadowPlusPower>(
            "LastEvokedVal",
            static (simulator, power) => (long)(LastEvoked(simulator, power).Value * 100m));
        return 3;
    }

    /// <summary>同步+：每channel一种**没见过的**球，给等于层数的集中。</summary>
    private static void SynchronizePlus(
        SynchronizePlusPower power,
        AfterOrbChanneledMirrorContext context)
    {
        if (!ReferenceEquals(context.Player, power.Owner.Player))
            return;
        ChanneledOrbState state = Channeled(context.Simulator, power);
        if (!state.Ids.Add(context.Orb.Id.Entry))
            return;
        if (context.CombatState is ICombatPredictionEffectSink effects)
            effects.ApplyPower(typeof(FocusPower), power.Owner, power.Amount, power.Owner);
    }

    /// <summary>吞噬暗影+：新充能的暗球带上「上一次暗球引爆量 × 层数 × 0.5」。</summary>
    private static void ConsumingShadowChanneled(
        ConsumingShadowPlusPower power,
        AfterOrbChanneledMirrorContext context)
    {
        if (!ReferenceEquals(context.Player, power.Owner.Player) || context.Orb is not DarkOrb dark)
            return;
        dark._evokeVal += LastEvoked(context.Simulator, power).Value;
    }

    /// <summary>吞噬暗影+：记下这次暗球引爆的量，供下一颗暗球继承。</summary>
    private static void ConsumingShadowEvoked(
        ConsumingShadowPlusPower power,
        AfterOrbEvokedMirrorContext context)
    {
        if (context.Orb is not DarkOrb dark
            || !ReferenceEquals(dark.Owner, power.Owner.Player))
        {
            return;
        }
        LastEvoked(context.Simulator, power).Value = dark.EvokeVal * power.Amount * 0.5m;
    }

    private static ChanneledOrbState Channeled(
        CombatPredictionSimulator simulator,
        PowerModel power)
        => simulator.StateStore.Get<ChanneledOrbState>(power);

    private static LastEvokedState LastEvoked(
        CombatPredictionSimulator simulator,
        PowerModel power)
        => simulator.StateStore.Get(power, () => new LastEvokedState(0m));
}
