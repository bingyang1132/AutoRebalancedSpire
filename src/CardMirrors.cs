using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using RebalancedSpire.Core.Enchantments;
using CombatSolver.Engine.InCombat.Extensions;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Commands;
using CombatSolver;
using CombatSolver.Engine.Common;
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

    /// <summary>旋转工艺：上一层「旋转工艺+」。</summary>
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

    /// <summary>既定事项：上一层「既定事项+」。</summary>
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

    /// <summary>肉盾：上一层「肉盾+」。</summary>
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

    /// <summary>永恒铠甲：给镀甲，再上一层「永恒铠甲」。</summary>
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

    // ---------- Silent ----------

    /// <summary>手上技法：加甲，然后给手里一张还没有「奇巧」的牌加上奇巧。</summary>
    private static void HandTrick(HandTrick card, CardOnPlayMirrorContext context)
    {
        V.Block(context);
        if (context.Simulator.HasPendingChoice)
            return;
        V.PlayerChoice(context, "手上技法从手牌里挑一张加奇巧");
    }

    /// <summary>藏匿匕首：这里什么都不做，两段效果都由求解器的选择通道负责。</summary>
    /// <remarks>
    /// 这张牌的效果被一个玩家选择劈成两段：先从手牌选几张弃掉，<b>选完之后</b>才造匕首。
    /// 求解器本来就把这两段分开处理——弃牌走 <c>CardChoiceSupport.GetSpec</c> 开成搜索分支
    /// （张数读的是活的 <c>Cards</c> 变量，改版改成 3、升级 −1 会自动跟随），
    /// 造匕首走 <c>CardChoiceSupport.ApplyPostChoiceEffects</c>。
    ///
    /// <para>所以这里**一定不能**自己造匕首。原来那版在出牌那一刻就造了，顺序是反的：
    /// 模拟里手牌提前多出两张匕首，求解器于是计划「把匕首弃掉」，而实机弹弃牌页面时匕首
    /// 还没造出来，部署时报「原生选牌页面找不到 SHIV」，整场操作不了。</para>
    ///
    /// <para>改版和原版唯一的实际差别是匕首挂「充能」而不是随本牌升级，那一处在
    /// <see cref="HiddenDaggersShivPatch" /> 里补。</para>
    /// </remarks>
    private static void HiddenDaggers(HiddenDaggers card, CardOnPlayMirrorContext context)
    {
        _ = card;
        _ = context;
    }

    /// <summary>无尽刀刃：上一层「无尽刀刃+」，并把牌上的张数加进那层的张数变量。</summary>
    /// <remarks>
    /// 原版是一层一张匕首；改版把张数存在 Power 自己的 Cards 变量上，每打一次累加，
    /// 所以必须取到刚施加的那一层再改它的变量 —— 只上层数会把张数丢掉。
    /// </remarks>
    private static void InfiniteBlades(InfiniteBlades card, CardOnPlayMirrorContext context)
    {
        V.Power(context, typeof(InfiniteBladesPlusPower), 1);
        if (V.Combat(context).GetMutablePower<InfiniteBladesPlusPower>(V.Self(context)) is { } power)
            power.DynamicVars.Cards.BaseValue += V.Var(card, "Cards");
    }

    /// <summary>谋划专家：上一层「谋划专家+」。</summary>
    private static void MasterPlanner(MasterPlanner card, CardOnPlayMirrorContext context)
        => V.Power(context, typeof(MasterPlannerPlusPower), V.VarInt(card, "Cards"));

    /// <summary>淬毒之刺：造若干带「剧毒」附魔的匕首进手牌，牌升级过则匕首也升级。</summary>
    private static void PoisonedStab(PoisonedStab card, CardOnPlayMirrorContext context)
        => V.ShivsInHand(
            context,
            V.VarInt(card, "Cards"),
            CanonicalModels.Enchantment<Poisonous>(),
            upgrade: card.IsUpgraded);

    /// <summary>计划妥当：上一层「计划妥当+」。</summary>
    private static void WellLaidPlans(WellLaidPlans card, CardOnPlayMirrorContext context)
        => V.Power(context, typeof(WellLaidPlansPlusPower), V.VarInt(card, "RetainAmount"));

    // ---------- Defect ----------

    /// <summary>吞噬暗影：上一层「吞噬暗影+」。</summary>
    private static void ConsumingShadow(ConsumingShadow card, CardOnPlayMirrorContext context)
        => V.Power(context, typeof(ConsumingShadowPlusPower), V.VarInt(card, "ConsumingShadowPlusPower"));

    /// <summary>玻璃工艺：加甲、充一颗玻璃球，然后给场上每颗玻璃球加被动值。</summary>
    /// <remarks>
    /// 那个被动值是球的私有字段 _passiveVal，原版就是直接改字段。分支里改的是球的克隆，
    /// 不碰实机模型。
    /// </remarks>
    private static void Glasswork(Glasswork card, CardOnPlayMirrorContext context)
    {
        V.Block(context);
        if (context.Simulator.HasPendingChoice)
            return;
        context.Simulator.OrbChannel<GlassOrb>(V.Owner(context));
        if (context.Simulator.HasPendingChoice)
            return;

        decimal value = V.Var(card, "Value");
        foreach (GlassOrb orb in context.Simulator.State
                     .GetPlayerCombatState(V.Owner(context)).OrbQueue.Orbs.OfType<GlassOrb>())
        {
            orb._passiveVal += value;
        }
    }

    /// <summary>飞跃：加甲，再上一层「飞跃」（临时集中）。</summary>
    /// <remarks>
    /// LeapPower 是临时 Power 模板，内部配一份等量的 FocusPower，回合结束一起收回。
    /// 只上记账那一层的话，本回合的球被动会少算，回合结束还照收，下回合开局凭空多出一个负集中 ——
    /// 这正是 AutoWatcher 在观者的「阳」上踩过的坑。
    /// </remarks>
    private static void Leap(Leap card, CardOnPlayMirrorContext context)
    {
        V.Block(context);
        if (context.Simulator.HasPendingChoice)
            return;
        int focus = V.VarInt(card, "FocusPower");
        V.Power(context, typeof(LeapPower), focus);
        V.Power(context, typeof(FocusPower), focus);
    }

    /// <summary>折射：按 Repeat 次充能玻璃球。</summary>
    private static void Refract(Refract card, CardOnPlayMirrorContext context)
        => context.Simulator.OrbChannel<GlassOrb>(V.Owner(context), V.VarInt(card, "Repeat"));

    /// <summary>碎裂：打全体，然后按球数逐个引爆；升级过的每颗引爆两次。</summary>
    private static void Shatter(Shatter card, CardOnPlayMirrorContext context)
    {
        V.AttackAllEnemies(context);
        if (context.Simulator.HasPendingChoice)
            return;

        int orbs = context.Simulator.State
            .GetPlayerCombatState(V.Owner(context)).OrbQueue.Orbs.Count;
        for (int i = 0; i < orbs; i++)
        {
            if (card.IsUpgraded)
            {
                context.Simulator.OrbEvokeNext(V.Owner(context), dequeue: false);
                if (context.Simulator.HasPendingChoice)
                    return;
            }
            context.Simulator.OrbEvokeNext(V.Owner(context));
            if (context.Simulator.HasPendingChoice)
                return;
        }
    }

    /// <summary>同步：上一层「同步+」。</summary>
    private static void Synchronize(Synchronize card, CardOnPlayMirrorContext context)
        => V.Power(context, typeof(SynchronizePlusPower), V.VarInt(card, "SynchronizePlusPower"));

    // ---------- Necrobinder ----------

    /// <summary>来生：上一层「来生」。</summary>
    /// <remarks>
    /// 那一层每回合开始（晚段）在奥斯提不在时召唤一只、在时治疗它，
    /// 见 <see cref="AfterEnergyResetLateDispatch" />。
    /// </remarks>
    private static void Afterlife(Afterlife card, CardOnPlayMirrorContext context)
        => V.Power(context, typeof(AfterlifePower), V.VarInt(card, "AfterlifePower"));

    /// <summary>守墓人：加甲，然后造若干魂进抽牌堆，牌升级过则魂也升级。</summary>
    private static void GraveWarden(GraveWarden card, CardOnPlayMirrorContext context)
    {
        V.Block(context);
        if (context.Simulator.HasPendingChoice)
            return;
        V.SoulsInto(context, PileType.Draw, V.VarInt(card, "Cards"), card.IsUpgraded);
    }

    /// <summary>吸引仇恨：召唤奥斯提，然后加甲。</summary>
    /// <remarks>顺序照原样：先召唤再加甲，中间任何一步起了选择都要停。</remarks>
    private static void PullAggro(PullAggro card, CardOnPlayMirrorContext context)
    {
        V.Combat(context).SummonOsty(context.Simulator, card.Owner, V.VarInt(card, "Summon"));
        if (context.Simulator.HasPendingChoice)
            return;
        V.Block(context);
    }

    /// <summary>死神形态：升级过上「死神形态+」，没升级上原版那层。</summary>
    /// <remarks>层数两边都读同一个变量 ReaperFormPower。</remarks>
    private static void ReaperForm(ReaperForm card, CardOnPlayMirrorContext context)
    {
        int amount = V.VarInt(card, "ReaperFormPower");
        V.Power(
            context,
            card.IsUpgraded ? typeof(ReaperFormPlusPower) : typeof(ReaperFormPower),
            amount);
    }

    /// <summary>降灵会：从抽牌堆选若干张，each 转化成一个魂。</summary>
    /// <remarks>
    /// 选哪几张是玩家定的，求解器没开这个分支。转化本身会实际改牌组，
    /// 所以这里只记一条「未建模的选择」，不擅自替某几张牌做决定。
    /// </remarks>
    private static void Seance(Seance card, CardOnPlayMirrorContext context)
        => V.PlayerChoice(context, "降灵会从抽牌堆里选几张转化成魂");

    /// <summary>紧追不放：奥斯提攻击目标，然后给目标上一层「紧追不放+」。</summary>
    /// <remarks>奥斯提不在或已经死了就什么都不发生，照原样判。</remarks>
    private static void SicEm(SicEm card, CardOnPlayMirrorContext context)
    {
        if (context.CardPlay.Target is not { } target)
            return;
        if (context.State.GetOsty(card.Owner) is not { } osty
            || context.State.GetCreature(osty).IsDead)
        {
            return;
        }

        DamageCmd.Attack(V.Var(card, "OstyDamage"))
            .FromOsty(osty, card, context.CardPlay)
            .Targeting(target)
            .Simulate(context.Simulator);
        if (context.Simulator.HasPendingChoice)
            return;

        V.PowerOn(context, typeof(SicEmPlusPower), target, V.VarInt(card, "SicEmPlusPower"));
    }

    /// <summary>马刺：先把治疗量喂给奥斯提，喂不完的按一半召唤新的。</summary>
    /// <remarks>
    /// 照原样：治疗只补到满血为止，剩下的量除以二（取整）拿去召唤。奥斯提不在就整份拿去召唤。
    /// </remarks>
    private static void Spur(Spur card, CardOnPlayMirrorContext context)
    {
        decimal amount = V.Var(card, "Heal");
        if (context.State.GetOsty(card.Owner) is { } osty
            && context.State.GetCreature(osty) is { IsAlive: true } state)
        {
            decimal heal = Math.Min(amount, state.MaxHp - state.CurrentHp);
            if (heal > 0)
            {
                context.Simulator.Heal(osty, heal);
                amount -= heal;
            }
        }
        if (amount <= 0)
            return;
        V.Combat(context).SummonOsty(context.Simulator, card.Owner, (int)(amount / 2m));
    }

    /// <summary>鬼火：给能量，然后把抽牌堆里随机一个魂变成鬼火自己的复制。</summary>
    /// <remarks>
    /// 随机取的那一步用求解器的洗牌通道，和实机取同一条随机序列。抽牌堆里没有魂就只给能量。
    /// </remarks>
    private static void Wisp(Wisp card, CardOnPlayMirrorContext context)
    {
        V.GainEnergy(context, V.Var(card, "Energy"));
        if (context.Simulator.HasPendingChoice)
            return;

        PredictedCard[] souls = context.OwnerState.DrawPile.Cards
            .Where(candidate => candidate.Preview is Soul)
            .ToArray();
        if (souls.Length == 0)
            return;

        PredictedCard chosen = souls.ToList()
            .UnstableShuffle(context.Simulator.Rng.CombatCardSelection)
            .First();
        CardChoiceSupport.TransformCards(
            context.Simulator, [chosen], CanonicalModels.Card<Wisp>(), card.IsUpgraded);
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
        yield return MirroredCard.For<HandTrick>(
            settings => settings.HandTrick,
            registry => registry.Register<HandTrick>(HandTrick));
        yield return MirroredCard.For<HiddenDaggers>(
            settings => settings.HiddenDaggers,
            registry => registry.Register<HiddenDaggers>(HiddenDaggers));
        yield return MirroredCard.For<InfiniteBlades>(
            settings => settings.InfiniteBlades,
            registry => registry.Register<InfiniteBlades>(InfiniteBlades));
        yield return MirroredCard.For<MasterPlanner>(
            settings => settings.MasterPlanner,
            registry => registry.Register<MasterPlanner>(MasterPlanner));
        yield return MirroredCard.For<PoisonedStab>(
            settings => settings.PoisonedStab,
            registry => registry.Register<PoisonedStab>(PoisonedStab));
        yield return MirroredCard.For<WellLaidPlans>(
            settings => settings.WellLaidPlans,
            registry => registry.Register<WellLaidPlans>(WellLaidPlans));
        yield return MirroredCard.For<ConsumingShadow>(
            settings => settings.ConsumingShadow,
            registry => registry.Register<ConsumingShadow>(ConsumingShadow),
            replacesBuiltIn: true);
        yield return MirroredCard.For<Glasswork>(
            settings => settings.Glasswork,
            registry => registry.Register<Glasswork>(Glasswork),
            replacesBuiltIn: true);
        yield return MirroredCard.For<Leap>(
            settings => settings.Leap,
            registry => registry.Register<Leap>(Leap));
        yield return MirroredCard.For<Refract>(
            settings => settings.Refract,
            registry => registry.Register<Refract>(Refract),
            replacesBuiltIn: true);
        yield return MirroredCard.For<Shatter>(
            settings => settings.Shatter,
            registry => registry.Register<Shatter>(Shatter),
            replacesBuiltIn: true);
        yield return MirroredCard.For<Synchronize>(
            settings => settings.Synchronize,
            registry => registry.Register<Synchronize>(Synchronize));
        yield return MirroredCard.For<Afterlife>(
            settings => settings.Afterlife,
            registry => registry.Register<Afterlife>(Afterlife));
        yield return MirroredCard.For<GraveWarden>(
            settings => settings.GraveWarden,
            registry => registry.Register<GraveWarden>(GraveWarden));
        yield return MirroredCard.For<PullAggro>(
            settings => settings.PullAggro,
            registry => registry.Register<PullAggro>(PullAggro));
        yield return MirroredCard.For<ReaperForm>(
            settings => settings.ReaperForm,
            registry => registry.Register<ReaperForm>(ReaperForm));
        yield return MirroredCard.For<Seance>(
            settings => settings.Seance,
            registry => registry.Register<Seance>(Seance));
        yield return MirroredCard.For<SicEm>(
            settings => settings.SicEm,
            registry => registry.Register<SicEm>(SicEm));
        yield return MirroredCard.For<Spur>(
            settings => settings.Spur,
            registry => registry.Register<Spur>(Spur));
        yield return MirroredCard.For<Wisp>(
            settings => settings.Wisp,
            registry => registry.Register<Wisp>(Wisp));
        yield return MirroredCard.For<Spinner>(
            settings => settings.Spinner,
            registry => registry.Register<Spinner>(Spinner),
            replacesBuiltIn: true);
    }
}
