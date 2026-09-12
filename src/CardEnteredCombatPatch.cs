using System.Reflection;
using HarmonyLib;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using RebalancedSpire.Core.Afflictions;
using RebalancedSpire.Core.Powers;

namespace AutoRebalancedSpire;

/// <summary>
/// 牌进场时两个新病症的自我清除。
/// </summary>
/// <remarks>
/// 原版 <c>Devoured</c> / <c>Weighted</c> 在 <c>AfterCardEnteredCombat</c> 里检查持有者还有没有
/// 对应的 Power（饥饿 / 审视），没有就把自己从牌上清掉 —— 也就是说源头死了以后新进场的牌不该
/// 再带病症。
///
/// 求解器这个时点是 <c>SimulatedCombatState.AfterCardEnteredCombat</c> 里一个写死的 switch，
/// 只认四张原版牌，**不分发给病症**，所以这条只能挂 postfix。这是本适配层碰到的第四个
/// 「求解器写死、没有第三方入口」的时点。
/// </remarks>
internal static class CardEnteredCombatPatch
{
    private const string TargetName = nameof(SimulatedCombatState.AfterCardEnteredCombat);

    public static MethodInfo ResolveTarget()
        => AccessTools.Method(typeof(SimulatedCombatState), TargetName)
           ?? throw new MissingMethodException(nameof(SimulatedCombatState), TargetName);

    public static void Postfix(
        SimulatedCombatState __instance,
        CombatPredictionSimulator simulator,
        PredictedCard card)
    {
        if (card.Preview.Owner is not { } owner)
            return;

        bool clear = card.Preview.Affliction switch
        {
            Devoured => __instance.GetAmount<HungerPower>(owner.Creature) <= 0,
            Weighted => __instance.GetAmount<ScrutinyPower>(owner.Creature) <= 0,
            _ => false,
        };
        if (clear)
            card.ClearAffliction();
    }
}
