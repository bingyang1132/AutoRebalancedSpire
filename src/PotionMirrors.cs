using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Potions.OnUse;
using RebalancedSpire.Core.Potions;

namespace AutoRebalancedSpire;

/// <summary>
/// RebalancedSpire 新加的三瓶药水。
/// </summary>
/// <remarks>
/// 三瓶都进事件药水池，战斗里都能喝。不镜像的话求解器会在建根时记一条未镜像风险（红字），
/// 不会算错 —— 但也**不会把它们排进路线**，等于玩家白带。药水是求解器最会用的资源之一
/// （它有专门的药水门槛和省血估算），所以这三瓶漏掉的代价不只是红字。
///
/// 三瓶都真的重写了 <c>OnUse</c>，所以能直接登记进求解器的药水注册表，不用打补丁。
/// </remarks>
internal static class PotionMirrors
{
    public static int RegisterAll()
    {
        PotionOnUseMirrors.Registry.Register<BoneTeaPotion>(BoneTea);
        PotionOnUseMirrors.Registry.Register<EmberTeaPotion>(EmberTea);
        PotionOnUseMirrors.Registry.Register<TeaOfDiscourtesyPotion>(TeaOfDiscourtesy);
        return 3;
    }

    /// <summary>骨茶：把抽牌堆里每一张能升级的牌都升一级。</summary>
    /// <remarks>
    /// 只动抽牌堆，手牌和弃牌堆不碰 —— 实机读的是 <c>PileType.Draw</c>。
    /// 升级动的是分支里的预览牌，不碰实机模型。
    /// </remarks>
    private static void BoneTea(BoneTeaPotion potion, PotionOnUseMirrorContext context)
    {
        if (potion.Owner is not { } owner)
            return;
        foreach (PredictedCard card in context.Simulator.State
                     .GetPlayerCombatState(owner).DrawPile.Cards.ToArray())
        {
            if (card.Preview.IsUpgradable)
                card.Upgrade();
        }
    }

    /// <summary>余烬茶：给目标力量。</summary>
    private static void EmberTea(EmberTeaPotion potion, PotionOnUseMirrorContext context)
    {
        if (context.Target is not { } target)
            return;
        if (context.CombatState is not SimulatedCombatState combat)
            return;
        combat.Apply<StrengthPower>(target, ReadPowerVar(potion, "Strength"), potion.Owner.Creature);
    }

    /// <summary>失礼茶：给目标的弃牌堆塞几张恍惚，位置随机。</summary>
    /// <remarks>战斗外那一支（在商人处升级一张牌）不用管：求解器只模拟战斗内。</remarks>
    private static void TeaOfDiscourtesy(
        TeaOfDiscourtesyPotion potion,
        PotionOnUseMirrorContext context)
    {
        if (context.Target is not { } target)
            return;
        context.Simulator.AddToCombat<Dazed>(
            target,
            PileType.Discard,
            potion.DynamicVars.Cards.IntValue,
            potion.Owner,
            CardPilePosition.Random);
    }

    /// <summary>
    /// 读一个由 <c>PowerVar&lt;T&gt;</c> 声明的动态变量。
    /// </summary>
    /// <remarks>
    /// <c>PowerVar&lt;T&gt;</c> 的单参构造按**类型名**建键（<c>StrengthPower</c>），
    /// 而声明它的代码通常用短名访问器（<c>DynamicVars.Strength</c>）去读。两种键都试一遍，
    /// 都没有就把这张牌上实际有哪些变量报出来 —— 这个坑在尸爆术上踩过一次，
    /// 当时的报错只说「找不到」，查了很久才知道键名是什么。
    /// </remarks>
    private static int ReadPowerVar(PotionModel potion, string shortName)
    {
        foreach (string key in new[] { shortName + "Power", shortName })
        {
            if (potion.DynamicVars.TryGetValue(key, out DynamicVar? variable))
                return variable.IntValue;
        }
        throw new InvalidOperationException(
            $"镜像在 {potion.Id.Entry} 上读不到动态变量 {shortName}。该药水实际有："
            + string.Join("、", potion.DynamicVars.Select(static entry => entry.Key)));
    }
}
