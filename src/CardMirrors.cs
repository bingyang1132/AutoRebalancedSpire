using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;
using RebalancedSpire.Core.Configs;
using V = AutoRebalancedSpire.Verbs;

namespace AutoRebalancedSpire;

/// <summary>
/// RebalancedSpire 改写过 <c>OnPlay</c> 的牌的镜像。一张牌一个方法，一行一效果，
/// 按它那份替换实现的调用顺序排列。
/// </summary>
/// <remarks>
/// 只镜像**被替换掉的那部分语义**。数值改动（CanonicalVars、能量费用、关键字）求解器本来就
/// 读活的模型，自动跟随，不用在这里重写。
///
/// 每一张牌都挂在 RebalancedSpire 自己的开关上：那个 mod 把补丁无条件装上，补丁体里再判开关，
/// 所以开关关掉时这张牌走的是原版语义，我们的镜像必须跟着退场。见
/// <see cref="MirroredCards" />。
/// </remarks>
internal static class CardMirrors
{
    /// <summary>燃料：给能量，然后抽牌。原版是把手牌里的状态牌转化掉。</summary>
    /// <remarks>
    /// 替换实现只有两句 <c>PlayerCmd.GainEnergy</c> 和 <c>CardPileCmd.Draw</c>，
    /// 没有目标、没有选择、没有新 Power，所以镜像是逐句对应的。
    /// </remarks>
    private static void Fuel(Fuel card, CardOnPlayMirrorContext context)
    {
        V.GainEnergy(context, V.Var(card, "Energy"));
        V.Draw(context, V.VarInt(card, "Cards"));
    }

    /// <summary>不可触碰：按 Repeat 次数重复加格挡。</summary>
    /// <remarks>
    /// 一次加 Block 点、重复 Repeat 次，不是一次加 Block×Repeat —— 两者在有
    /// 「获得格挡时」触发的能力在场时不等价，所以照原样循环。
    /// </remarks>
    private static void Untouchable(Untouchable card, CardOnPlayMirrorContext context)
    {
        int repeat = V.VarInt(card, "Repeat");
        for (int i = 0; i < repeat; i++)
            V.Block(context);
    }

    public static IEnumerable<MirroredCard> All()
    {
        yield return MirroredCard.For<Fuel>(
            settings => settings.Fuel,
            registry => registry.Register<Fuel>(Fuel));
        yield return MirroredCard.For<Untouchable>(
            settings => settings.Untouchable,
            registry => registry.Register<Untouchable>(Untouchable));
    }
}
