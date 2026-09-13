using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using RebalancedSpire.Core.Configs;

namespace AutoRebalancedSpire;

/// <summary>
/// 战史课程重放的范围从「攻击牌」放宽到「攻击或技能」。
/// </summary>
/// <remarks>
/// 原版战史课程在每回合的自动出牌阶段复制并打出「上个玩家回合最后一张**攻击**牌」；
/// 改版（开关 `WarHistorianRepy`）把条件放宽成**攻击或技能**，别的一个字没动
/// （同样跳过复制牌、同样第一回合不触发）。
///
/// 求解器这一段是写死的，不走注册表：<c>SimulatedCombatState.RecordHistoryCourseAttack</c>
/// 记录当回合的候选，<c>GetPreviousTurnAttack</c> 取上回合的那张。两处都按
/// <c>Type != CardType.Attack</c> 过滤，认不出技能，也**不记风险** —— 于是路线里少掉一次
/// 免费重放，而且少掉的正是玩家特意留到最后打的那张。
///
/// 两个方法都要改：记录那一处决定当回合的候选，取值那一处在「建根时还没物化」的分支里
/// 会直接回原始战斗历史重新找一遍，过滤条件是另写的一份。只改一处会在续接的局面上漏。
/// </remarks>
internal static class HistoryCoursePatch
{
    private const string RecordName = "RecordHistoryCourseAttack";
    private const string LookupName = "GetPreviousTurnAttack";

    public static MethodInfo ResolveRecordTarget()
        => AccessTools.Method(typeof(SimulatedCombatState), RecordName)
           ?? throw new MissingMethodException(nameof(SimulatedCombatState), RecordName);

    public static MethodInfo ResolveLookupTarget()
        => AccessTools.Method(typeof(SimulatedCombatState), LookupName)
           ?? throw new MissingMethodException(nameof(SimulatedCombatState), LookupName);

    private static bool Qualifies(CardModel card)
        => card.Type is CardType.Attack or CardType.Skill && !card.IsDupe;

    public static bool RecordPrefix(SimulatedCombatState __instance, PredictedCard card)
    {
        if (!RebalancedSpireSettingsStore.Settings.WarHistorianRepy)
            return true;
        if (Qualifies(card.Preview))
        {
            __instance._lastAttackThisTurn ??= [];
            __instance._lastAttackThisTurn[card.Preview.Owner] = card;
        }
        return false;
    }

    /// <summary>
    /// 取上回合那张候选。整段替换而不是在后面补，因为过滤条件放宽之后，
    /// 「原版口径找到的那张」和「改版口径该找的那张」可能是不同的两张牌 ——
    /// 玩家先打攻击后打技能时，改版取的是技能。只在返回空时补就会取错。
    /// </summary>
    public static bool LookupPrefix(
        SimulatedCombatState __instance,
        CombatPredictionSimulator simulator,
        Player player,
        ref PredictedCard? __result)
    {
        if (!RebalancedSpireSettingsStore.Settings.WarHistorianRepy)
            return true;

        if (__instance._rootMaterialized)
        {
            __result = __instance._lastAttackPreviousTurn?.GetValueOrDefault(player);
            return false;
        }
        if (__instance._lastAttackPreviousTurn?.TryGetValue(player, out PredictedCard? cached) == true)
        {
            __result = cached;
            return false;
        }

        CardPlayFinishedEntry? live = __instance._rootHistory.CardPlaysFinished.LastOrDefault(entry =>
            entry.CardPlay.Player == player
            && entry.HappenedLastPlayerTurn(player)
            && Qualifies(entry.CardPlay.Card));
        if (live == null)
        {
            __result = null;
            return false;
        }

        PredictedCard predicted = simulator.State.FindCard(live.CardPlay.Card)
            ?? PredictedCard.FromGenerated(
                PredictionUtils.CloneCardStateForSimulation(live.CardPlay.Card));
        __instance._lastAttackPreviousTurn ??= [];
        __instance._lastAttackPreviousTurn[player] = predicted;
        __result = predicted;
        return false;
    }
}
