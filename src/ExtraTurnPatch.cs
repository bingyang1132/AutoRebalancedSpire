using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using CombatSolver;
using RebalancedSpire.Core.Powers;

namespace AutoRebalancedSpire;

/// <summary>
/// 「时之沙」给的额外回合。
/// </summary>
/// <remarks>
/// 求解器只认遗物给的额外回合（<c>ConsumeExtraTurnSources</c> 里写死了琥珀香能力和帕尔之眼），
/// Power 给的它看不见。AutoWatcher 为观者的飞跃踩过同一条路，这里照那份写法来。
///
/// 原版 <c>SandsOfTimePower</c>：只要还有层数就 <c>ShouldTakeExtraTurn</c>，
/// 用掉之后 <c>AfterTakingExtraTurn</c> 里 <c>Decrement</c> 一层（不是清零）。
/// </remarks>
internal static class ExtraTurnPatch
{
    public static MethodInfo ResolvePrepareTarget()
        => AccessTools.Method(
               typeof(SimulatedCombatState),
               nameof(SimulatedCombatState.TryPrepareExtraPlayerTurn))
           ?? throw new MissingMethodException(
               nameof(SimulatedCombatState),
               nameof(SimulatedCombatState.TryPrepareExtraPlayerTurn));

    public static MethodInfo ResolveLivePrepareTarget()
        => AccessTools.Method(
               typeof(SimulatedCombatState),
               nameof(SimulatedCombatState.TryPrepareLiveExtraPlayerTurn))
           ?? throw new MissingMethodException(
               nameof(SimulatedCombatState),
               nameof(SimulatedCombatState.TryPrepareLiveExtraPlayerTurn));

    public static MethodInfo ResolveConsumeTarget()
        => AccessTools.Method(
               typeof(SimulatedCombatState),
               nameof(SimulatedCombatState.ConsumeExtraTurnSources))
           ?? throw new MissingMethodException(
               nameof(SimulatedCombatState),
               nameof(SimulatedCombatState.ConsumeExtraTurnSources));

    /// <summary>两条准备入口共用：形参名一致，Harmony 按名字绑定。</summary>
    public static void PreparePostfix(
        SimulatedCombatState __instance,
        Player player,
        bool __result,
        ref bool extraTurn)
    {
        if (!__result)
            return;
        if (__instance.GetAmount<SandsOfTimePower>(player.Creature) > 0)
            extraTurn = true;
    }

    /// <summary>用掉一次就减一层，对应原版的 <c>PowerCmd.Decrement</c>。</summary>
    public static void ConsumePostfix(SimulatedCombatState __instance, Player player)
    {
        if (__instance.GetMutablePower<SandsOfTimePower>(player.Creature) is { Amount: > 0 } power)
            __instance.SetPowerAmount(power, power.Amount - 1);
    }
}
