using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Extensions;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Card;
using CombatSolver.Engine.InCombat.Simulation;
using RebalancedSpire.Core.Configs;

namespace AutoRebalancedSpire;

/// <summary>
/// 被改动的遗物里，会影响战斗内结算的那几个。
/// </summary>
/// <remarks>
/// 求解器对遗物的镜像基本都写在 <c>SimulatedCombatState.RelicTurnStart</c> 一个大 switch 里，
/// 不是注册表，所以只能挑合适的方法挂补丁。挑的原则是「挂在参数上看得见差异的那个方法」，
/// 而不是去替换整段 switch。
///
/// 不用做的两个，记在这里免得下次再查：
/// <list type="bullet">
///   <item><b>领主之伞</b>（<c>ModifyMaxEnergy</c> +1）：求解器算上限能量走的是
///     <c>Hook.ModifyMaxEnergy</c>，也就是游戏自己的监听链，会调到被补丁过的实现，自动跟随。</item>
///   <item><b>战锤</b>、**炼金匣**等：求解器压根没镜像它们，改动也就无从谈起 ——
///     它们本来就是「未镜像」的状态。</item>
/// </list>
/// </remarks>
internal static class RelicMirrors
{
    private const string GenerateRelicCardsName = "GenerateRelicCards";

    public static MethodInfo ResolveGenerateTarget()
        => AccessTools.Method(typeof(SimulatedCombatState), GenerateRelicCardsName)
           ?? throw new MissingMethodException(nameof(SimulatedCombatState), GenerateRelicCardsName);

    public static MethodInfo ResolveGeneratedToHandTarget()
        => AccessTools.Method(
               typeof(TurnStartChoiceSupport),
               nameof(TurnStartChoiceSupport.ResolveGeneratedToHand))
           ?? throw new MissingMethodException(
               nameof(TurnStartChoiceSupport),
               nameof(TurnStartChoiceSupport.ResolveGeneratedToHand));

    /// <summary>十字弩：每回合开始生成的那张攻击牌，改版是「本场免费 + 消耗」。</summary>
    /// <remarks>
    /// 原版是「本回合免费」，没有关键字。差别有两处，而且方向相反：本场免费比本回合免费更强
    /// （留到后面几回合也不要钱），带消耗则更弱（打完就没了，不会塞满弃牌堆）。
    ///
    /// 这里整段接管而不是改参数：抽牌那一步要用同一个 RNG 通道按同样的参数取，
    /// 换个写法就会让这条分支之后的随机序列和实机对不上。
    /// </remarks>
    public static bool GenerateRelicCardsPrefix(
        SimulatedCombatState __instance,
        CombatPredictionSimulator simulator,
        RelicModel relic,
        IEnumerable<CardModel> options,
        int count,
        bool setFreeThisTurn)
    {
        _ = setFreeThisTurn;
        if (relic is not Crossbow || !RebalancedSpireSettingsStore.Settings.Crossbow)
            return true;

        List<PredictedCard> generated = options
            .GetDistinctForCombat(
                relic.Owner,
                count,
                simulator.Rng.CombatCardGeneration,
                __instance._cardMultiplayerConstraint)
            .ToList();
        foreach (PredictedCard card in generated)
        {
            card.SetToFreeThisCombat();
            card.MutablePreview.AddKeyword(CardKeyword.Exhaust);
        }
        simulator.AddGeneratedCardsToCombat(
            generated,
            PileType.Hand,
            relic.Owner,
            CardPilePosition.Bottom,
            CardGenerationResultKind.Random);
        return false;
    }

    /// <summary>选择悖论：第一回合那几张备选牌，改版是升级过的。</summary>
    /// <remarks>
    /// 备选张数（6）走 CanonicalVars，求解器自动跟随；保留关键字两边都加。唯一的差别是
    /// 改版给每张备选 <c>CardCmd.Upgrade</c>。挂在解析这次选择的入口上：备选牌就是它的入参，
    /// 在它把选择摆出来之前升级，玩家看到的和实机一致。
    /// </remarks>
    public static void ResolveGeneratedToHandPrefix(
        CombatPredictionSimulator simulator,
        string sourceId,
        IReadOnlyList<PredictedCard> options)
    {
        if (!RebalancedSpireSettingsStore.Settings.ChoicesParadox)
            return;
        if (!string.Equals(sourceId, ChoicesParadoxId, StringComparison.Ordinal))
            return;
        foreach (PredictedCard option in options)
            simulator.Upgrade(option);
    }

    private static string ChoicesParadoxId { get; } = CanonicalModels.Relic<ChoicesParadox>().Id.Entry;

    /// <summary>小提琴：改版对持有者永远允许抽牌。</summary>
    /// <remarks>
    /// 原版是「自己的回合里、非发牌抽的那种抽牌」一律不抽（它换成给别的收益）。改版把
    /// <c>ShouldDraw</c> 直接写成 true，也就是这个限制没了 —— 求解器按原版算会以为抽不到牌，
    /// 整条靠抽牌的路线都被压掉。
    ///
    /// 这一条求解器是写在注册表里的（少见），所以走 0.2 那套摘掉再登记。
    /// </remarks>
    public static IEnumerable<MirroredHookReplacement> Replacements()
    {
        yield return new MirroredHookReplacement(
            typeof(Fiddle),
            settings => settings.Fiddle,
            () => RegistryOverride.DropRegistration(ShouldDrawMirrors.Registry, typeof(Fiddle)),
            () => ShouldDrawMirrors.Registry.Register<Fiddle>(static (_, _) => true));
    }
}
