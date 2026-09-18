using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Afflictions.OnPlay;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Card;
using CombatSolver.Engine.InCombat.Simulation;
using RebalancedSpire.Core.Afflictions;
using RebalancedSpire.Core.Powers;

namespace AutoRebalancedSpire;

/// <summary>
/// RebalancedSpire 新增病症的镜像。
/// </summary>
/// <remarks>
/// 病症挂在牌上，由怪物施加（无法逃脱来自永世沙漏，吞噬来自感染棱柱，沉重来自花园幽灵鳗，
/// 物归原主来自多尼斯异鸟），任何角色都可能吃到，所以排在角色批前面。
/// </remarks>
internal static class AfflictionMirrors
{
    public static int RegisterAll()
    {
        AfflictionOnPlayMirrors.Registry.Register<Withering>(Withering);
        AfterCardExhaustedMirrors.Registry.Register<Withering>(WitheringAfterExhausted);
        AfflictionOnPlayMirrors.Registry.Register<ToItsOriginOwner>(ToItsOriginOwner);
        return 3;
    }

    /// <summary>
    /// 无法逃脱：第一次打出把凋萎假升级两级、本场费用 +1、给一层「时之沙」；之后每次打出退一级。
    /// </summary>
    /// <remarks>
    /// 假升级直接调牌自己的 <c>FakeUpgrade()</c>：那个方法已经被 RebalancedSpire 换过
    /// （层数 +1 并给 <c>Damage</c> 加 <c>PerLevel</c>），调它比在这里重写一遍更不容易漂。
    /// 动的是分支克隆 <c>MutablePreviewCard</c>，不碰实机模型。
    ///
    /// 退级那一支是原样照抄：层数 −1，并把 <c>Damage</c> 减回一个 <c>PerLevel</c>。
    /// </remarks>
    private static void Withering(Withering affliction, AfflictionOnPlayMirrorContext context)
    {
        if (context.MutablePreviewCard is not Wither wither)
            return;

        if (wither.FakeUpgradeLevel == 0)
        {
            wither.FakeUpgrade();
            wither.FakeUpgrade();
            wither.EnergyCost.AddThisCombat(1);
            if (context.CombatState is not ICombatPredictionEffectSink effects)
                throw new InvalidOperationException("无法逃脱病症缺少可写的预测状态。");
            Creature owner = wither.Owner.Creature;
            effects.ApplyPower(typeof(SandsOfTimePower), owner, 1, owner);
            return;
        }

        wither.FakeUpgradeLevel--;
        wither.DynamicVars.Damage.UpgradeValueBy(-wither.DynamicVars["PerLevel"].BaseValue);
    }

    /// <summary>物归原主：打出被标记的多尼斯异鸟蛋，每个玩家得一层「物归原主」，多尼斯异鸟直接退场。</summary>
    /// <remarks>
    /// 卵本来是不可打出的任务牌。多尼斯异鸟愤怒时给它挂上这个病症，病症的 <c>AfterApplied</c>
    /// 去掉「不可打出」换成「消耗」，改版另有一个补丁把它的目标类型改成单体敌人 —— 那两样
    /// 求解器都跟得上（一个走病症的 <c>AfterApplied</c>，一个是取值器补丁），缺的只是这里的
    /// 打出效果。
    ///
    /// 「物归原主」那层的作用全在战斗外（战后清掉卵、多给一次选牌），求解器不会结算它。
    /// 但它进指纹：实机施加了而预测没有，下一回合两边的 Power 列表对不上，整场重算。
    ///
    /// 退场用逃跑口径。原版是先清空异鸟身上全部 Power 再把它移出战斗，
    /// <c>CreatureEscaped</c> 做的正是这两件事，而且不会被当成一次死亡
    /// （它本来就不是死亡，死亡效果和死亡奖励都不该结算）。
    ///
    /// 不镜像的后果不是算错，是求解器看不见「打一张卵就结束这场精英战」这条线：
    /// 病症的 <c>OnPlay</c> 没登记会记一条未镜像风险，那一场一直挂红字。
    /// </remarks>
    private static void ToItsOriginOwner(
        ToItsOriginOwner affliction,
        AfflictionOnPlayMirrorContext context)
    {
        if (context.PreviewCard is not ByrdonisEgg)
            return;
        if (context.Target is not { } target || target.Monster is not Byrdonis)
            return;
        if (context.CombatState is not SimulatedCombatState combat)
            return;

        foreach (Player player in combat.Players)
            combat.Apply<ToItsOriginOwnerPower>(player.Creature, 1, player.Creature);
        combat.CreatureEscaped(target);
    }

    /// <summary>无法逃脱：带这个病症的凋萎被消耗之后，回到弃牌堆的随机位置。</summary>
    /// <remarks>
    /// 也就是说这张牌消耗不掉 —— 不镜像的话求解器会以为烧掉它就一了百了，
    /// 把一条实际上还会再吃伤害的路线算成安全的。
    ///
    /// <para><b>位置必须是随机，不能是牌堆底。</b>改版那一句是
    /// <c>CardPileCmd.Add(wither, PileType.Discard, CardPilePosition.Random)</c>，随机位置那一步会从
    /// <b>洗牌随机通道</b>取一个数。求解器的 <c>AddToPile</c> 在 <c>Random</c> 上取的是同一条通道、
    /// 同样一个数，所以只要位置参数一致，两边的随机序列就仍然对齐。
    /// 先前这里用的是默认的牌堆底：一个数都不取，于是每消耗一张凋萎，预测和实机的洗牌计数就差 1 ——
    /// 下一次洗牌顺序整个对不上，回合边界上必然重算。永世沙漏那一场一回合塞 4 张凋萎、
    /// 而凋萎是虚幻牌回合结束全消耗，所以差值一回合就攒到 4（2026-09-18 的问题包正是这个数）。</para>
    /// </remarks>
    private static void WitheringAfterExhausted(
        Withering affliction,
        AfterCardExhaustedMirrorContext context)
    {
        if (context.PreviewCard is not Wither)
            return;
        context.Simulator.AddToPile([context.Card], PileType.Discard, CardPilePosition.Random);
    }
}
