using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using CombatSolver.Engine.InCombat.Simulation;
using CombatSolver;
using CombatSolver.Engine.InCombat.Mirrors;
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

    /// <summary>严阵以待：按手牌里攻击牌的张数给能量。</summary>
    /// <remarks>
    /// 原版是按力量给；改版把乘数换成手牌里的攻击牌张数（基数 0、每张 1 点），费用 2，
    /// 升级 −1 费。乘数不是在这里现算的，而是走求解器自己的计算变量通道 ——
    /// <see cref="CalculatedVarPatch" /> 已经把那张写死的乘数表接管了，所以这里和别处
    /// （估值、牌面预览）读到的一定是同一个数，不会各算各的。
    /// </remarks>
    private static void ExpectAFight(ExpectAFight card, CardOnPlayMirrorContext context)
        => V.GainEnergy(context, V.RequireVar(card, "CalculatedEnergy")
            .InvokeCalculate(context.Simulator, context.Card, context.CardPlay.Target));

    // ---------- Regent ----------

    /// <summary>必然结局：上一层「必然结局+」。</summary>
    /// <remarks>
    /// 层数取的是牌的 <c>Cards</c> 变量。那个 Power 每回合发牌前让你从抽牌堆挑几张放到牌堆顶，
    /// 见 <see cref="PowerMirrors" />。
    /// </remarks>
    private static void ForegoneConclusion(ForegoneConclusion card, CardOnPlayMirrorContext context)
        => V.Power(context, typeof(ForegoneConclusionPlusPower), V.VarInt(card, "Cards"));

    /// <summary>传家宝锤：锻造若干次，然后从手牌选一张无色牌，复制一份进手牌。</summary>
    /// <remarks>
    /// 锻造那半是确定的，照结算。选牌那半求解器没有为它开分支，显式记一条「未建模的选择」——
    /// 宁可红字也不要静默按「没选」算，那会把这张牌的价值整个抹掉。
    /// </remarks>
    private static void HeirloomHammer(HeirloomHammer card, CardOnPlayMirrorContext context)
    {
        PersistentPowerSupport.Forge(context.Simulator, card.Owner, V.VarInt(card, "Forge"));
        if (context.Simulator.HasPendingChoice)
            return;
        V.PlayerChoice(context, "传家宝锤从手牌里选一张无色牌复制");
    }

    // ---------- Ironclad ----------

    /// <summary>坦克：上一层「坦克+」。</summary>
    /// <remarks>
    /// 那个 Power 在回合结束前给**其他**玩家角色加甲 —— 单人局里没有别的玩家角色，
    /// 所以它在单人局是个空转。这里照样把层数上上去：层数本身会进指纹，也会被别的效果读到。
    /// </remarks>
    private static void Tank(Tank card, CardOnPlayMirrorContext context)
        => V.Power(context, typeof(TankPlusPower), V.VarInt(card, "TankPlusPower"));

    /// <summary>遗忘仪式：本回合消耗过牌就给能量，然后自己本场费用 +1。</summary>
    /// <remarks>
    /// 原版是无条件给能量。改版加了「本回合消耗过牌」这个前提，还加了自己越打越贵。
    /// 两条都要补：少了前提会高估，少了涨价会让连打的路线便宜一大截。
    /// </remarks>
    private static void ForgottenRitual(ForgottenRitual card, CardOnPlayMirrorContext context)
    {
        if (V.Combat(context).WasCardExhaustedThisTurn(V.Self(context)))
            V.GainEnergy(context, V.Var(card, "Energy"));
        context.MutablePreviewCard.EnergyCost.AddThisCombat(1);
    }

    // ---------- 无色 ----------

    /// <summary>永恒护甲：给镀甲，再上一层「永恒护甲」。</summary>
    /// <remarks>
    /// 那一层是个纯标记，唯一作用是让镀甲不再每回合衰减，见 <see cref="PlatingDecayPatch" />。
    /// </remarks>
    private static void EternalArmor(EternalArmor card, CardOnPlayMirrorContext context)
    {
        V.Power(context, typeof(PlatingPower), V.VarInt(card, "PlatingPower"));
        V.Power(context, typeof(EternalArmorPower), V.VarInt(card, "EternalArmorPower"));
    }

    /// <summary>齐射：打全体，然后给自己一层「保留手牌」。</summary>
    private static void Salvo(Salvo card, CardOnPlayMirrorContext context)
    {
        V.AttackAllEnemies(context);
        if (context.Simulator.HasPendingChoice)
            return;
        V.Power(context, typeof(RetainHandPower), 1);
    }

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
        yield return MirroredCard.For<ExpectAFight>(
            settings => settings.ExpectAFight,
            registry => registry.Register<ExpectAFight>(ExpectAFight));
        yield return MirroredCard.For<ForegoneConclusion>(
            settings => settings.ForegoneConclusion,
            registry => registry.Register<ForegoneConclusion>(ForegoneConclusion));
        yield return MirroredCard.For<HeirloomHammer>(
            settings => settings.HeirloomHammer,
            registry => registry.Register<HeirloomHammer>(HeirloomHammer));
        yield return MirroredCard.For<Tank>(
            settings => settings.Tank,
            registry => registry.Register<Tank>(Tank));
        yield return MirroredCard.For<ForgottenRitual>(
            settings => settings.ForgottenRitual,
            registry => registry.Register<ForgottenRitual>(ForgottenRitual));
        yield return MirroredCard.For<EternalArmor>(
            settings => settings.EternalArmor,
            registry => registry.Register<EternalArmor>(EternalArmor));
        yield return MirroredCard.For<Salvo>(
            settings => settings.Salvo,
            registry => registry.Register<Salvo>(Salvo));
        yield return MirroredCard.For<Spinner>(
            settings => settings.Spinner,
            registry => registry.Register<Spinner>(Spinner),
            replacesBuiltIn: true);
    }
}
