using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using CombatSolver.Engine.InCombat.Simulation;
using CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;
using RebalancedSpire.Core.Configs;
using RebalancedSpire.Core.Powers;
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

    /// <summary>光辉：给星，然后抽牌。</summary>
    /// <remarks>
    /// 和原版的差别不在数值上 —— 原版打完还会上一层 <c>DrawCardsNextTurnPower</c>（下回合多抽
    /// 同样张数），改版**把那一层去掉了**，只留当场的星和抽牌，抽牌基数从 1 提到 2。
    /// 少镜像掉的正是那一层，所以这里只有两句。
    /// </remarks>
    private static void Glow(Glow card, CardOnPlayMirrorContext context)
    {
        V.GainStars(context, V.Var(card, "Stars"));
        V.Draw(context, V.VarInt(card, "Cards"));
    }

    /// <summary>袖里乾坤：造 Cards 张匕首进手牌。</summary>
    /// <remarks>
    /// 原版每打出一次还会 <c>EnergyCost.AddThisCombat(-1)</c>，本场越打越便宜；改版**去掉了
    /// 这条**，只留造匕首（费用改成常驻 2、加保留关键字，那些是数据层，求解器自动跟随）。
    /// 漏掉这一点的后果是求解器以为第二张便宜 1 点，整条连打路线的费用都算错。
    /// </remarks>
    private static void UpMySleeve(UpMySleeve card, CardOnPlayMirrorContext context)
        => context.Simulator.CreateAndAddGeneratedCardsToCombat<Shiv>(
            card.Owner, PileType.Hand, V.VarInt(card, "Cards"), card.Owner);

    /// <summary>中子护盾：按花掉的星给镀甲，花够 Stars 颗则翻倍。</summary>
    /// <remarks>
    /// 原版是固定 1 费给一个定值镀甲；改版把它变成了**星 X 牌**（<c>HasStarCostX</c> 真、
    /// 能量费 0、Stars 基数 5，升级 −1），镀甲点数就是花掉的星数，花到 Stars 及以上再翻倍。
    /// 「≥」不是「>」，照它写的来。
    /// </remarks>
    private static void NeutronAegis(NeutronAegis card, CardOnPlayMirrorContext context)
    {
        int stars = context.Card.ResolveStarXValue(context.State);
        if (stars >= V.VarInt(card, "Stars"))
            stars *= 2;
        V.Power(context, typeof(PlatingPower), stars);
    }

    /// <summary>纺纱：上一层「纺纱+」。</summary>
    /// <remarks>
    /// 原版是「升级过的话先充一颗玻璃球，再上 <c>SpinnerPower</c>」；改版费用 1 → 2，
    /// 去掉了升级那颗球，上的换成 <c>SpinnerPlusPower</c>（每回合充能之后还会把场上所有玻璃球
    /// 各触发一次被动，见 <see cref="PowerMirrors" />）。
    ///
    /// 这是求解器自己也登记了 bespoke 镜像的五张之一，登记前要先摘掉它那条。
    /// </remarks>
    private static void Spinner(Spinner card, CardOnPlayMirrorContext context)
        => V.Power(context, typeof(SpinnerPlusPower), V.VarInt(card, "SpinnerPlusPower"));

    public static IEnumerable<MirroredCard> All()
    {
        yield return MirroredCard.For<Fuel>(
            settings => settings.Fuel,
            registry => registry.Register<Fuel>(Fuel));
        yield return MirroredCard.For<Untouchable>(
            settings => settings.Untouchable,
            registry => registry.Register<Untouchable>(Untouchable));
        yield return MirroredCard.For<Glow>(
            settings => settings.Glow,
            registry => registry.Register<Glow>(Glow));
        yield return MirroredCard.For<UpMySleeve>(
            settings => settings.UpMySleeve,
            registry => registry.Register<UpMySleeve>(UpMySleeve));
        yield return MirroredCard.For<NeutronAegis>(
            settings => settings.NeutronAegis,
            registry => registry.Register<NeutronAegis>(NeutronAegis));
        yield return MirroredCard.For<Spinner>(
            settings => settings.Spinner,
            registry => registry.Register<Spinner>(Spinner),
            replacesBuiltIn: true);
    }
}
