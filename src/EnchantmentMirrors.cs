using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Enchantments;
using MegaCrit.Sts2.Core.Models.Powers;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Enchantments.OnPlay;
using CombatSolver.Engine.InCombat.Simulation;
using RebalancedSpire.Core.Enchantments;

namespace AutoRebalancedSpire;

/// <summary>
/// RebalancedSpire 新增的两个附魔的 <c>OnPlay</c> 镜像。
/// </summary>
/// <remarks>
/// 这两个附魔改伤害的那几个方法**不用镜像**：求解器算附魔伤害时直接调附魔自己的
/// <c>EnchantDamageAdditive</c> / <c>EnchantDamageMultiplicative</c>，
/// <c>ModifyDamageMultiplicative</c> 也是没登记就回落到监听者自己的实现。所以「充能让这张牌
/// 不造成伤害」是自动跟上的，要补的只有打出时那一次性的效果。
///
/// 同理，墨刃（Inky）被改过的 <c>EnchantDamageAdditive</c>（只对强化攻击加伤）也不用管。
/// </remarks>
internal static class EnchantmentMirrors
{
    public static int RegisterAll()
    {
        EnchantmentOnPlayMirrors.Registry.Register<Energetic>(Energetic);
        EnchantmentOnPlayMirrors.Registry.Register<Poisonous>(Poisonous);
        return 2;
    }

    /// <summary>充能：打出时给一次能量，然后自己失效。</summary>
    /// <remarks>和原版的播种（Sown）逐句同形，照着它写的。</remarks>
    private static void Energetic(Energetic enchantment, EnchantmentOnPlayMirrorContext context)
    {
        if (enchantment.Status != EnchantmentStatus.Normal)
            return;
        enchantment._status = EnchantmentStatus.Disabled;
        context.Simulator.GainEnergy(context.PreviewCard.Owner, enchantment.Amount);
    }

    /// <summary>剧毒：打出时给「目标 + 所有可命中的敌人」各上一份毒。</summary>
    /// <remarks>
    /// 照它写的来：先在不是「全体敌人」牌的时候把 <c>CardPlay</c> 的目标加进名单，再把所有
    /// 可命中敌人整个加进去。**单体牌的目标因此会进名单两次**，而原版
    /// <c>PowerCmd.Apply&lt;T&gt;(IEnumerable)</c> 不去重、逐个施加 —— 也就是目标吃两份毒。
    /// 看着像个笔误，但这是它实际跑出来的结果，镜像必须跟着，不然求解器会低估这张牌。
    /// </remarks>
    private static void Poisonous(Poisonous enchantment, EnchantmentOnPlayMirrorContext context)
    {
        if (context.CombatState is not ICombatPredictionEffectSink effects)
            throw new InvalidOperationException("剧毒附魔缺少可写的预测状态。");

        List<Creature> targets = [];
        if (context.Simulator.GetTargetType(context.Card) != TargetType.AllEnemies
            && context.CardPlay.Target is { } selected)
        {
            targets.Add(selected);
        }
        targets.AddRange(context.State.HittableEnemies);

        int poison = enchantment.DynamicVars.Poison.IntValue;
        Creature applier = context.PreviewCard.Owner.Creature;
        foreach (Creature target in targets)
            effects.ApplyPower(typeof(PoisonPower), target, poison, applier);
    }
}
