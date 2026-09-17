using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;

namespace AutoRebalancedSpire;

/// <summary>
/// 两条「回合边界上的玩家选牌」的通用结算：求解器的回合开始/结束选牌通道只认它自己
/// <c>TurnStartChoiceSupport</c> 里写死的那几种效果，改版新加的两种不在其中。
/// </summary>
/// <remarks>
/// 这里是照着 <c>TurnStartChoiceSupport.Resolve</c> / <c>ResolvePileDiscard</c> 抄的同一套流程：
/// 先按当前牌堆造 <c>CardChoiceSpec</c>，再向游标要答案；游标给不出答案就把请求挂成
/// 「待处理选择」并返回 false，由调用方把这一层的返回值变成一次搜索边界。
/// 分支枚举、计划记录、部署时应答原生选牌页，求解器那边都是对 <c>PlanChoiceEffect</c> 通用的，
/// 只有「选完之后做什么」那一步是写死的 switch —— 缺的就是那一步。
/// </remarks>
internal static class TurnChoiceMirrors
{
    /// <summary>从抽牌堆挑固定张数放到牌堆顶（既定事项+）。</summary>
    public static bool ResolveMoveToDrawTop(
        CombatPredictionSimulator simulator,
        SimulatedCombatState combat,
        Player player,
        TurnStartChoiceCursor? cursor,
        string sourceId,
        int requestedCount)
    {
        SimPlayerCombatState state = simulator.State.GetPlayerCombatState(player);
        IReadOnlyList<PredictedCard> sourceCards = state.DrawPile.Cards;
        int count = Math.Min(requestedCount, sourceCards.Count);
        if (count <= 0)
            return true;

        IReadOnlyList<PredictedCard> options = sourceCards.ToArray();
        CardChoiceSpec spec = new(
            PlanChoiceEffect.MoveToDrawTop,
            PileType.Draw,
            count,
            count,
            options,
            sourceCards,
            ReplacementValue: 0d,
            IsImplicitAllSelection: options.Count <= requestedCount);
        TurnStartChoiceRequest request = new(
            sourceId,
            PlanChoiceEffect.MoveToDrawTop,
            PileType.Draw,
            count,
            spec,
            Timing: combat.ActiveActionChoiceTiming);
        if (cursor == null || !cursor.TryTake(request, out PlanCardChoice? choice))
        {
            if (!combat.HasPendingChoice)
                combat.SetPendingTurnStartChoice(request);
            return false;
        }

        IReadOnlyList<PredictedCard> selected = ResolveTokens(choice!, options, count, count);
        simulator.AddToPile(selected, PileType.Draw, CardPilePosition.Top);
        if (combat.HasPendingChoice)
            return false;
        combat.ClearPendingTurnStartChoice();
        return true;
    }

    /// <summary>从手牌里挑最多若干张给一次性保留（计划妥当+）。可以一张都不挑。</summary>
    public static bool ResolveSingleTurnRetain(
        CombatSolver.Engine.InCombat.Simulation.CombatPredictionSimulator simulator,
        SimulatedCombatState combat,
        Player player,
        TurnStartChoiceCursor? cursor,
        string sourceId,
        int maxCount)
    {
        SimPlayerCombatState state = simulator.State.GetPlayerCombatState(player);
        IReadOnlyList<PredictedCard> sourceCards = state.Hand.Cards;
        IReadOnlyList<PredictedCard> options = sourceCards
            .Where(card => !card.Preview.ShouldRetainThisTurn)
            .ToArray();
        int count = Math.Min(maxCount, options.Count);
        if (count <= 0)
            return true;

        CardChoiceSpec spec = new(
            PlanChoiceEffect.ApplyRetain,
            PileType.Hand,
            0,
            count,
            options,
            sourceCards,
            ReplacementValue: 0d);
        TurnStartChoiceRequest request = new(
            sourceId,
            PlanChoiceEffect.ApplyRetain,
            PileType.Hand,
            count,
            spec,
            Timing: combat.ActiveActionChoiceTiming);
        if (cursor == null || !cursor.TryTake(request, out PlanCardChoice? choice))
        {
            if (!combat.HasPendingChoice)
                combat.SetPendingTurnStartChoice(request);
            return false;
        }

        IReadOnlyList<PredictedCard> selected = ResolveTokens(choice!, options, 0, count);
        foreach (PredictedCard card in selected)
            card.MutablePreview.GiveSingleTurnRetain();
        if (combat.HasPendingChoice)
            return false;
        combat.ClearPendingTurnStartChoice();
        return true;
    }

    /// <summary>
    /// 把计划里的卡牌令牌翻回当前分支里的实际牌。
    /// </summary>
    /// <remarks>
    /// 求解器自己那份（<c>TurnStartChoiceSupport.ResolveTokens</c>）是私有的。这里重写一份，
    /// 不是嫌它不好，而是不想让适配层依赖一个私有方法名 —— 它改名不该让整个适配层加载不上。
    /// 越界时抛 <c>InvalidPlannedChoiceBranchException</c>，和求解器同一个类型：
    /// 那是「这条计划分支已经对不上当前状态」的信号，上层会重算，不是崩溃。
    /// </remarks>
    private static IReadOnlyList<PredictedCard> ResolveTokens(
        PlanCardChoice choice,
        IReadOnlyList<PredictedCard> options,
        int minCount,
        int maxCount)
    {
        List<PredictedCard> selected = [];
        foreach (PlanCardToken token in choice.Cards)
        {
            PredictedCard card = options
                .Where(candidate => CardChoiceSupport.MatchesToken(candidate, token))
                .Skip(token.OptionOccurrence)
                .FirstOrDefault()
                ?? throw new InvalidPlannedChoiceBranchException(
                    $"回合边界选牌时找不到 {token.CardId}+{token.UpgradeLevel}#{token.OptionOccurrence}。");
            if (selected.Contains(card))
                throw new InvalidPlannedChoiceBranchException($"回合边界计划重复选择了 {token.CardId}。");
            selected.Add(card);
        }
        if (selected.Count < minCount || selected.Count > maxCount)
        {
            throw new InvalidPlannedChoiceBranchException(
                $"回合边界计划选择 {selected.Count} 张牌，但当前要求 {minCount}..{maxCount} 张。");
        }
        return selected;
    }
}
