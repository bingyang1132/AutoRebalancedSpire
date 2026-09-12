using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Runs;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using RebalancedSpire.Core.Powers;
using STS2RitsuLib.Combat.HandSize;

namespace AutoRebalancedSpire;

/// <summary>
/// 会在战斗中变的手牌上限。
/// </summary>
/// <remarks>
/// 求解器的手牌上限是**建根时冻结**的：<c>PredictionModHookSubscriberCapture</c> 建根时问一次
/// RitsuLib，把结果存进 <c>SimulatedCombatState._rootMaxHandSizes</c>，整条搜索分支此后都读那个数。
/// 这在原版下没有任何问题 —— 原版一个 <c>IMaxHandSizeModifier</c> 都没有，值本来就不会变。
///
/// 改版有三个。<c>FiddleSingleton</c> 看的是遗物，一场战斗里不变，冻结值就是对的；另外两个会变：
/// <list type="bullet">
///   <item><c>InfiniteBladesPlusPower</c>：上限 + min(Cards, 手里匕首数)。打掉一张匕首就少一格。</item>
///   <item><c>ScrutinyPower</c>：上限 − 层数。层数每个敌方回合末减 2。</item>
/// </list>
///
/// 所以这里把冻结值当成「不含这两个来源的底」来用：先减掉建根那一刻它们贡献了多少，
/// 再按当前分支的状态重算一遍加回去。建根那一刻的贡献只能在建根时算 —— 那时候实机模型还在原位，
/// 直接调它们自己的 <c>ModifyMaxHandSize(player, 0)</c> 拿增量最准，不必在这里重写一遍公式。
///
/// 冻结值里其他 mod 的贡献（例如 Loadout、Fiddle）原样留着，一个字不动。
/// </remarks>
internal static class MaxHandSizePatch
{
    private static IReadOnlyDictionary<Player, int> _rootContributions =
        new Dictionary<Player, int>();

    public static MethodInfo ResolveCaptureTarget()
        => AccessTools.Method(typeof(PredictionModHookSubscriberCapture), "Capture")
           ?? throw new MissingMethodException(
               nameof(PredictionModHookSubscriberCapture), "Capture");

    public static MethodInfo ResolveMaxHandSizeTarget()
        => AccessTools.Method(
               typeof(SimulatedCombatState),
               nameof(SimulatedCombatState.GetMaxHandSize),
               [typeof(Player)])
           ?? throw new MissingMethodException(
               nameof(SimulatedCombatState), nameof(SimulatedCombatState.GetMaxHandSize));

    /// <summary>建根时记下这两个 Power 当时各自贡献了多少格。</summary>
    public static void CapturePostfix(RunState runState, CombatState combat)
    {
        Dictionary<Player, int> contributions = [];
        foreach (Player player in combat.Players)
        {
            int delta = 0;
            foreach (var power in player.Creature.Powers)
            {
                if (power is not (InfiniteBladesPlusPower or ScrutinyPower))
                    continue;
                if (power is IMaxHandSizeModifier modifier)
                    delta += modifier.ModifyMaxHandSize(player, 0);
            }
            contributions[player] = delta;
        }
        _rootContributions = contributions;
    }

    /// <summary>把冻结值换成「建根底数 + 当前分支的贡献」。</summary>
    public static void MaxHandSizePostfix(
        SimulatedCombatState __instance,
        Player player,
        ref int __result)
    {
        if (!_rootContributions.TryGetValue(player, out int rootDelta))
            return;
        // 建根之前手牌还没接上预测状态。那一刻冻结值就是实机值，本来就是对的。
        if (__instance._predictionState is null)
            return;

        int current = 0;
        if (__instance.GetPower<InfiniteBladesPlusPower>(player.Creature) is { Amount: > 0 } blades)
        {
            int shivs = ShivsInHand(__instance, player);
            current += Math.Min(blades.DynamicVars.Cards.IntValue, shivs);
        }
        if (__instance.GetPower<ScrutinyPower>(player.Creature) is { Amount: > 0 } scrutiny)
            current -= scrutiny.Amount;

        if (current != rootDelta)
            __result = Math.Max(0, __result - rootDelta + current);
    }

    private static int ShivsInHand(SimulatedCombatState combat, Player player)
    {
        int count = 0;
        foreach (PredictedCard card in combat._predictionState!.GetPlayerCombatState(player).Hand.Cards)
        {
            if (card.Preview is Shiv)
                count++;
        }
        return count;
    }
}
