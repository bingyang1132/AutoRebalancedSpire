using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using CombatSolver;
using CombatSolver.Engine.InCombat.Simulation;
using RebalancedSpire.Core.Powers;

namespace AutoRebalancedSpire;

/// <summary>回合开始重置能量之后（晚段）结算一个 Power 时的上下文。</summary>
internal readonly record struct AfterEnergyResetLateContext(
    CombatPredictionSimulator Simulator,
    SimulatedCombatState Combat,
    Player Player);

/// <summary>
/// 把「回合开始重置能量之后（晚段）」这个时点分发给 Power —— 求解器不分发。
/// </summary>
/// <remarks>
/// 求解器在这个时点只跑一个遗物（<c>TurnStartRelicSupport.TriggerAfterEnergyResetLate</c> 里
/// 写死了 <c>BoundPhylactery</c>），**Power 一个都不发，也不记风险**。也就是说重写了
/// <c>AfterEnergyResetLate</c> 的第三方 Power 会被静默忽略 —— 和 PR #88 修好的
/// <c>AfterEnergyReset</c> 是同一类毛病的兄弟，只是那一条已经改成注册表了，这一条还没有。
///
/// 没有登记入口，只能在那个方法后面挂 postfix 自己分发。等上游也把这里开成注册表，
/// 这个文件就该删掉换成登记。
///
/// **和原版的次序差异**：原版是所有监听者（遗物和 Power）在同一条监听链上按顺序结算，
/// 这里是「求解器的遗物那段先跑完，再跑我们的 Power」。目前唯一的 Power 是往世
/// （召唤或治疗奥斯提），和 <c>BoundPhylactery</c>（也召唤奥斯提）同场时次序会有影响，
/// 真遇上再收紧。
/// </remarks>
internal static class AfterEnergyResetLateDispatch
{
    private const string TargetName = nameof(TurnStartRelicSupport.TriggerAfterEnergyResetLate);

    private static readonly Dictionary<Type, Action<PowerModel, AfterEnergyResetLateContext>> Handlers = new()
    {
        [typeof(AfterlifePower)] = static (power, context) => Afterlife(power, context),
    };

    public static int HandlerCount => Handlers.Count;

    public static MethodInfo ResolveTarget()
        => AccessTools.Method(typeof(TurnStartRelicSupport), TargetName)
           ?? throw new MissingMethodException(nameof(TurnStartRelicSupport), TargetName);

    public static void Postfix(
        CombatPredictionSimulator simulator,
        SimulatedCombatState combat,
        Player player)
    {
        if (simulator.HasPendingChoice)
            return;

        Creature owner = player.Creature;
        var context = new AfterEnergyResetLateContext(simulator, combat, player);
        foreach (PowerModel power in combat.EffectivePowers().ToArray())
        {
            if (power.Owner != owner || power.Amount <= 0)
                continue;
            if (!Handlers.TryGetValue(power.GetType(), out var handler))
                continue;
            handler(power, context);
            if (simulator.HasPendingChoice)
                return;
        }
    }

    /// <summary>往世：奥斯提不在场就按 Summon 召唤一只，在场就按 Heal 治疗它。</summary>
    /// <remarks>
    /// 原版判的是 <c>player.IsOstyMissing || player.Osty == null</c>。分支里对应的说法是
    /// 「拿不到奥斯提，或者拿到了但已经死了」。两个变量都挂在 Power 自己身上，
    /// 层数变化时由 <c>AfterPowerAmountChanged</c> 同步成 Amount 和 Amount−1。
    /// </remarks>
    private static void Afterlife(PowerModel power, AfterEnergyResetLateContext context)
    {
        Creature? osty = context.Simulator.State.GetOsty(context.Player);
        bool alive = osty != null && context.Simulator.State.GetCreature(osty).IsAlive;
        if (!alive)
        {
            context.Combat.SummonOsty(
                context.Simulator, context.Player, (int)power.DynamicVars.Summon.BaseValue);
            return;
        }
        context.Simulator.Heal(osty!, power.DynamicVars.Heal.BaseValue);
    }
}
