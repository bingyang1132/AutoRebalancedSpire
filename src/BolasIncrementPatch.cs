using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Cards;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using RebalancedSpire.Core.Configs;

namespace AutoRebalancedSpire;

/// <summary>
/// 流星锤每次自己飞回手里，伤害永久 +3。
/// </summary>
/// <remarks>
/// 原版流星锤：上个玩家回合打出过它，这个回合发牌前它自己回到手上。改版在「回到手上」那一步
/// 之后多加一句 —— 把 <c>Damage</c> 的基值加上 <c>IncrementAmount</c>（3）。别的都没改。
///
/// 求解器**已经**模拟了回手那一半：它不走牌的 <c>BeforeHandDraw</c>（那个钩子求解器根本不给牌
/// 分发），而是自己有一套 <c>_returnToHandNextTurn</c>，名单写死为
/// <c>card is Bolas or ThrummingHatchet</c>。所以这里要补的只有涨伤害那一句。
///
/// 涨的是本场的基值，一场里可以涨很多次 —— 少算的话，一条「留着流星锤滚雪球」的路线会被
/// 按第一回合的伤害估值，越到后面差得越多。
///
/// 做法是在 <c>PrepareBeforeHandDraw</c> 前后各看一眼：前缀记下这一刻**不在手上**的流星锤，
/// 后缀看它们是不是真的进手了，进了才涨。求解器那段的判据是同一个（<c>pile?.Type != Hand</c>
/// 才搬），所以两边对得上；中途因为待处理选择提前返回时，没搬成的那张也不会被误涨。
///
/// 这一段期间唯一会把牌搬进手里的就是那个循环本身，所以「前缀不在手、后缀在手」等价于
/// 「这次搬进去的」。
/// </remarks>
internal static class BolasIncrementPatch
{
    private const string TargetName = nameof(SimulatedCombatState.PrepareBeforeHandDraw);

    [ThreadStatic]
    private static List<PredictedCard>? _pending;

    public static MethodInfo ResolveTarget()
        => AccessTools.Method(
               typeof(SimulatedCombatState),
               TargetName,
               [typeof(CombatPredictionSimulator), typeof(Player), typeof(TurnStartChoiceCursor)])
           ?? throw new MissingMethodException(nameof(SimulatedCombatState), TargetName);

    public static void Prefix(CombatPredictionSimulator simulator, Player player)
    {
        _pending = null;
        if (!AdapterSettings.Current.Bolas)
            return;

        foreach (PredictedCard card in simulator.State.GetPlayerCombatState(player).AllCards)
        {
            if (card.Preview is not Bolas || card.Preview.HasBeenRemovedFromState)
                continue;
            if (card.GetPile(simulator.State)?.Type == PileType.Hand)
                continue;
            (_pending ??= []).Add(card);
        }
    }

    public static void Postfix(CombatPredictionSimulator simulator)
    {
        if (_pending is not { Count: > 0 } candidates)
            return;
        _pending = null;

        foreach (PredictedCard card in candidates)
        {
            if (card.Preview.HasBeenRemovedFromState)
                continue;
            if (card.GetPile(simulator.State)?.Type != PileType.Hand)
                continue;
            decimal increment = card.Preview.DynamicVars["IncrementAmount"].BaseValue;
            if (increment == 0m)
                continue;
            card.MutablePreview.DynamicVars.Damage.BaseValue += increment;
        }
    }
}
