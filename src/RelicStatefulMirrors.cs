using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Card;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.TurnEnd;
using CombatSolver.Engine.InCombat.Simulation;
using RebalancedSpire.Core.Configs;
using RebalancedSpire.Core.Powers;

namespace AutoRebalancedSpire;

/// <summary>
/// 两个被彻底重做、而且带「本回合打了几张牌」这种自有状态的遗物：钻石冠冕、轰鸣海螺。
/// </summary>
/// <remarks>
/// 这两个在求解器里都是 <c>RelicTurnStart</c> 那个大 switch 里的一行，做的还是**原版**的事
/// （冠冕：首回合给格挡和模糊；海螺：精英房首回合给能量和多抽牌）。改版把它们换成了完全不同的
/// 机制，所以这里分两步：
/// <list type="number">
///   <item>把这两个遗物从「参与回合开始结算的遗物」名单里摘掉，求解器那几行就不会跑；</item>
///   <item>改版的机制自己用注册表补回来 —— 打牌计数、回合结束给 Power、改牌的费用。</item>
/// </list>
/// 计数用求解器的分支状态存储，不碰实机模型；根状态从遗物的 <c>DisplayAmount</c> 读，
/// 那正是 RebalancedSpire 用来显示已打牌数的那个值。
/// </remarks>
internal static class RelicStatefulMirrors
{
    private const string ParticipatingName = "RelicsParticipatingInSideTurn";
    private const string HandDrawName = "GetTurnBasedHandDrawContribution";
    private const string TriggerRegularName = nameof(EndTurnPowerSupport.TriggerRegular);
    private const string PrepareTurnEndName = nameof(SimulatedCombatState.PrepareRelicsBeforeSideTurnEnd);

    public static MethodInfo ResolveParticipatingTarget()
        => AccessTools.Method(typeof(SimulatedCombatState), ParticipatingName)
           ?? throw new MissingMethodException(nameof(SimulatedCombatState), ParticipatingName);

    public static MethodInfo ResolveHandDrawTarget()
        => AccessTools.Method(typeof(PersistentPowerSupport), HandDrawName)
           ?? throw new MissingMethodException(nameof(PersistentPowerSupport), HandDrawName);

    public static MethodInfo ResolveTurnEndPowerTarget()
        => AccessTools.Method(typeof(EndTurnPowerSupport), TriggerRegularName)
           ?? throw new MissingMethodException(nameof(EndTurnPowerSupport), TriggerRegularName);

    public static MethodInfo ResolvePrepareTurnEndTarget()
        => AccessTools.Method(typeof(SimulatedCombatState), PrepareTurnEndName)
           ?? throw new MissingMethodException(nameof(SimulatedCombatState), PrepareTurnEndName);

    public static MethodInfo ResolveEnergyCostTarget()
        => AccessTools.Method(
               typeof(ModifyEnergyCostInCombatMirrors),
               nameof(ModifyEnergyCostInCombatMirrors.Invoke))
           ?? throw new MissingMethodException(
               nameof(ModifyEnergyCostInCombatMirrors), nameof(ModifyEnergyCostInCombatMirrors.Invoke));

    public static MethodInfo ResolveStarCostTarget()
        => AccessTools.Method(
               typeof(ModifyStarCostMirrors),
               nameof(ModifyStarCostMirrors.Invoke))
           ?? throw new MissingMethodException(
               nameof(ModifyStarCostMirrors), nameof(ModifyStarCostMirrors.Invoke));

    /// <summary>把我们自己接管的遗物从求解器的回合开始名单里摘掉。</summary>
    public static void ParticipatingPostfix(List<RelicModel> __result)
    {
        RebalancedSpireSettings settings = RebalancedSpireSettingsStore.Settings;
        __result.RemoveAll(relic => relic switch
        {
            DiamondDiadem => settings.DiamondDiadem,
            BoomingConch => settings.BoomingConch,
            _ => false,
        });
    }

    /// <summary>改版的海螺不再多抽牌（<c>ModifyHandDraw</c> 原样返回）。</summary>
    public static void HandDrawPostfix(RelicModel relic, ref decimal __result)
    {
        if (relic is BoomingConch && RebalancedSpireSettingsStore.Settings.BoomingConch)
            __result = 0m;
    }

    /// <summary>敌人回合结束时收掉钻石冠冕给的那层减伤。</summary>
    /// <remarks>
    /// 原版 <c>DiamondDiademPower.AfterSideTurnEnd</c> 在敌人侧结束时移除自己。求解器这个
    /// 时点是 <c>EndTurnPowerSupport.TriggerRegular</c> 里一个写死的 switch，第三方 Power
    /// 进不去，只能挂在后面。减伤本身（对自己受到的强化攻击伤害 ×0.5）不用镜像 ——
    /// <c>ModifyDamageMultiplicative</c> 没登记会回落到 Power 自己的实现。
    /// </remarks>
    public static void TurnEndPowerPostfix(
        SimulatedCombatState combat,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Enemy || !RebalancedSpireSettingsStore.Settings.DiamondDiadem)
            return;
        foreach (Creature creature in participants)
        {
            if (combat.GetMutablePower<DiamondDiademPower>(creature) is { Amount: > 0 } power)
                combat.SetPowerAmount(power, 0);
        }
    }

    // ---------- 钻石冠冕 ----------

    /// <summary>本回合打的牌不超过阈值，玩家回合结束时给一层「钻石冠冕」减伤。</summary>
    /// <remarks>
    /// 张数直接用求解器自己的每回合计数，不另起一份：那份是从根历史加分支历史一起算的，
    /// 我们自己数反而容易和它对不上。和原版计数的差别是它按「打牌开始」计、改版按
    /// <c>AfterCardPlayed</c> 计，连击系列里的后续几张两边算法不同 —— 目前没有已知的实际差异，
    /// 真遇上再收紧。
    /// </remarks>
    public static void PrepareTurnEndPostfix(
        SimulatedCombatState __instance,
        CombatPredictionSimulator simulator,
        IReadOnlyList<Creature> participants)
    {
        if (!RebalancedSpireSettingsStore.Settings.DiamondDiadem)
            return;
        if (__instance is not ICombatPredictionEffectSink effects)
            return;

        foreach (DiamondDiadem relic in __instance.Players
                     .SelectMany(__instance.RelicsOf)
                     .OfType<DiamondDiadem>()
                     .Where(relic => !relic.IsMelted && participants.Contains(relic.Owner.Creature)))
        {
            Creature owner = relic.Owner.Creature;
            if (__instance.GetCardPlayStartsThisTurn(owner) <= relic.DynamicVars["CardThreshold"].IntValue)
                effects.ApplyPower(typeof(DiamondDiademPower), owner, 1, owner);
            if (simulator.HasPendingChoice)
                return;
        }
    }

    // ---------- 轰鸣海螺 ----------

    /// <summary>海螺还在「本场前 N 张免费」的窗口里吗。</summary>
    /// <remarks>
    /// 窗口只在精英房开战时打开（改版的 <c>BeforeCombatStart</c> 把 <c>Status</c> 置 1），
    /// 所以直接读 <c>Status</c>。已经用掉几张：根状态读遗物的 <c>DisplayAmount</c>
    /// （改版把那个取值改成了已打牌数），再加上这条分支里打过的牌数 —— 不加后面这半，
    /// 分支里打多少张都免费，求解器会规划出一条实机付不起的路线。
    ///
    /// 挂在 <c>Invoke</c> 的前缀上而不是登记进注册表：注册表要求类型真的重写了那个虚方法，
    /// 而 RebalancedSpire 是补在 <c>AbstractModel</c> 上的，海螺自己并没有重写。
    /// </remarks>
    public static bool EnergyCostPrefix(
        AbstractModel listener,
        ModifyEnergyCostInCombatMirrorContext context,
        ref decimal __result)
        => !TryFreeForConch(listener, context.Simulator, context.Card, context.Cost, ref __result);

    public static bool StarCostPrefix(
        AbstractModel listener,
        ModifyStarCostMirrorContext context,
        ref decimal __result)
        => !TryFreeForConch(listener, context.Simulator, context.Card, context.Cost, ref __result);

    private static bool TryFreeForConch(
        AbstractModel listener,
        CombatPredictionSimulator simulator,
        PredictedCard card,
        decimal cost,
        ref decimal result)
    {
        if (listener is not BoomingConch relic || !RebalancedSpireSettingsStore.Settings.BoomingConch)
            return false;
        if (card.Preview.Owner != relic.Owner)
            return false;

        result = ConchPlayedThisCombat(simulator, relic) < relic.DynamicVars.Cards.IntValue
            && (int)relic.Status == 1
            ? 0m
            : cost;
        return true;
    }

    private static int ConchPlayedThisCombat(CombatPredictionSimulator simulator, BoomingConch relic)
        => relic.DisplayAmount + simulator.History.Entries
            .OfType<CombatPredictionCardPlayFinishedEntry>()
            .Count(entry => entry.CardPlay.Player == relic.Owner);
}
