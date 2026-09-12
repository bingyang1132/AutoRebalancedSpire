using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Death;
using RebalancedSpire.Core.Cards;
using RebalancedSpire.Core.Powers;
using V = AutoRebalancedSpire.Verbs;

namespace AutoRebalancedSpire;

/// <summary>
/// RebalancedSpire 新加的两张牌。
/// </summary>
/// <remarks>
/// 这两张不进建根审查 —— 审查看的是「原版牌的 OnPlay 上有没有第三方补丁」，而它们本身就是
/// 第三方的牌，没人补它们。但求解器对陌生牌型只能靠读 IL 去推断，推不出来就会拒绝整场战斗，
/// 所以照样要登记镜像。
///
/// 两张都是尘封魔典（`DustyTome`）那个开关放出来的。
/// </remarks>
internal static class NewCardMirrors
{
    public static int RegisterAll()
    {
        CardOnPlayMirrors.Registry.Register<CorpseExplosion>(CorpseExplosion);
        CardOnPlayMirrors.Registry.Register<LimitBreak>(LimitBreak);
        AfterDeathMirrors.Registry.Register<CorpseExplosionPower>(CorpseExplosionDeath);
        return 3;
    }

    /// <summary>尸爆：给目标上毒，再上一层「尸爆」。</summary>
    /// <remarks>
    /// 变量键是 <c>PoisonPower</c> 不是 <c>Poison</c> —— <c>PowerVar&lt;T&gt;</c> 的单参数构造用的是
    /// 类型名做键，牌面上那个 <c>DynamicVars.Poison</c> 只是个取值快捷方式。写错了会在结算到
    /// 这张牌时抛 KeyNotFound，验收矩阵第一次跑就是这么挂的。
    /// </remarks>
    private static void CorpseExplosion(CorpseExplosion card, CardOnPlayMirrorContext context)
    {
        if (context.CardPlay.Target is not { } target)
            return;
        V.PowerOn(context, typeof(PoisonPower), target, V.VarInt(card, "PoisonPower"));
        if (context.Simulator.HasPendingChoice)
            return;
        V.PowerOn(context, typeof(CorpseExplosionPower), target, V.VarInt(card, "CorpseExplosionPower"));
    }

    /// <summary>极限突破：先给一点力量，然后把当前力量再翻一倍。</summary>
    /// <remarks>
    /// 顺序要紧：先加那一点，再读**加完之后**的力量翻倍。反过来算会少一倍的那一点。
    /// </remarks>
    private static void LimitBreak(LimitBreak card, CardOnPlayMirrorContext context)
    {
        V.Power(context, typeof(StrengthPower), V.VarInt(card, "StrengthPower"));
        if (context.Simulator.HasPendingChoice)
            return;
        int strength = V.Combat(context).GetAmount<StrengthPower>(V.Self(context));
        if (strength > 0)
            V.Power(context, typeof(StrengthPower), strength);
    }

    /// <summary>尸爆：挂着它的敌人死掉时，按它的最大生命 × 层数打所有可命中的敌人。</summary>
    /// <remarks>
    /// 伤害属性是「不可格挡 + 无强化」，而且没有施加者 —— 照原版写的来。
    /// 不镜像的话求解器看不到「打死这只会连带清场」，会把一条很强的路线压掉。
    /// </remarks>
    private static void CorpseExplosionDeath(
        CorpseExplosionPower power,
        AfterDeathMirrorContext context)
    {
        if (context.WasRemovalPrevented || context.Creature != power.Owner)
            return;
        if (context.CombatState is not { } combat)
            return;

        Creature[] targets = combat.HittableEnemies.ToArray();
        if (targets.Length == 0)
            return;
        context.Simulator.Damage(
            targets,
            context.Creature.MaxHp * power.Amount,
            ValueProp.Unblockable | ValueProp.Unpowered,
            dealer: null);
    }
}
