using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Cards;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Card;
using CombatSolver.Engine.InCombat.Simulation;

namespace AutoRebalancedSpire;

/// <summary>
/// 只改了钩子、没改 <c>OnPlay</c> 的牌。
/// </summary>
/// <remarks>
/// 这些牌不进建根审查（审查只看 <c>OnPlay</c> 上有没有第三方补丁），所以不需要放行，
/// 但求解器对它们的镜像是按原版写的，改动一样要补。两张都是求解器已经登记过的，
/// 走 0.2 那套摘掉再登记。
///
/// **还有一张没做**：Bolas 改的是 <c>BeforeHandDraw</c>。求解器的
/// <c>TriggerBeforeHandDraw</c> 只遍历 Power，**牌的这个钩子它从来不分发** ——
/// 也就是说原版 Bolas 在求解器里本来就没镜像，不是改动带来的新问题。要补得先在那一段里
/// 加上对牌的遍历，属于另一件事。
/// </remarks>
internal static class CardHookMirrors
{
    public static IEnumerable<MirroredHookReplacement> Replacements()
    {
        yield return new MirroredHookReplacement(
            typeof(RightHandHand),
            settings => settings.RightHandHand,
            () => RegistryOverride.DropRegistration(
                AfterCardPlayedMirrors.LateRegistry, typeof(RightHandHand)),
            () => AfterCardPlayedMirrors.LateRegistry.Register<RightHandHand>(RightHandHand));

        yield return new MirroredHookReplacement(
            typeof(RocketPunch),
            settings => settings.RocketPunch,
            () => RegistryOverride.DropRegistration(
                AfterCardGeneratedForCombatMirrors.Registry, typeof(RocketPunch)),
            () => AfterCardGeneratedForCombatMirrors.Registry.Register<RocketPunch>(RocketPunch));
    }

    /// <summary>右手手：打出一张够贵的牌之后回到手牌，并且自己本场变便宜。</summary>
    /// <remarks>
    /// 和原版比有三处不同：门槛读的是新变量 <c>Required</c> 而不是 <c>Energy</c>；
    /// 回手的来源从「只在弃牌堆时」放宽到「只要不在手上」；而且每触发一次自己本场费用
    /// 减 <c>Energy</c> 点 —— 最后这条原版完全没有，不补的话连打的费用会一直算成原价。
    /// </remarks>
    private static void RightHandHand(RightHandHand card, AfterCardPlayedMirrorContext context)
    {
        if (context.PreviewCard.Owner != card.Owner)
            return;
        if (context.CardPlay.Resources.EnergyValue < card.DynamicVars["Required"].IntValue)
            return;
        if (context.State.FindCard(card) is not { } predicted)
            return;

        if (predicted.GetPile(context.State)?.Type != PileType.Hand)
            context.Simulator.AddToPile(predicted, PileType.Hand);
        predicted.MutablePreview.EnergyCost.AddThisCombat(-card.DynamicVars.Energy.IntValue);
    }

    /// <summary>火箭拳：自己生成了一张状态牌时，费用变成 0（直到被打出）。</summary>
    /// <remarks>
    /// 原版是减 1 费。求解器里那一段已经为 Sts2RebalanceBeta 写了一个「设成 0」的分支，
    /// 但那条是按程序集名判的，认不出 RebalancedSpire，所以这里照样要换。
    /// </remarks>
    private static void RocketPunch(RocketPunch card, AfterCardGeneratedForCombatMirrorContext context)
    {
        if (context.Creator != card.Owner
            || context.PreviewCard.Owner != card.Owner
            || context.PreviewCard.Type != CardType.Status)
        {
            return;
        }
        context.State.FindCard(card)?.MutablePreview.EnergyCost.SetUntilPlayed(0);
    }
}
