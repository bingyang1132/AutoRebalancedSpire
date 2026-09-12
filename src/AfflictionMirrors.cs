using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Afflictions.OnPlay;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Card;
using RebalancedSpire.Core.Afflictions;
using RebalancedSpire.Core.Powers;

namespace AutoRebalancedSpire;

/// <summary>
/// RebalancedSpire 新增病症的镜像。
/// </summary>
/// <remarks>
/// 病症挂在牌上，由怪物施加（凋零来自永世沙漏，吞噬来自寄生棱镜，称重来自幻影园丁），
/// 任何角色都可能吃到，所以排在角色批前面。
/// </remarks>
internal static class AfflictionMirrors
{
    public static int RegisterAll()
    {
        AfflictionOnPlayMirrors.Registry.Register<Withering>(Withering);
        AfterCardExhaustedMirrors.Registry.Register<Withering>(WitheringAfterExhausted);
        return 2;
    }

    /// <summary>
    /// 凋零：第一次打出把枯萎假升级两级、本场费用 +1、给一层「时之沙」；之后每次打出退一级。
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
                throw new InvalidOperationException("凋零病症缺少可写的预测状态。");
            Creature owner = wither.Owner.Creature;
            effects.ApplyPower(typeof(SandsOfTimePower), owner, 1, owner);
            return;
        }

        wither.FakeUpgradeLevel--;
        wither.DynamicVars.Damage.UpgradeValueBy(-wither.DynamicVars["PerLevel"].BaseValue);
    }

    /// <summary>凋零：带这个病症的枯萎被消耗之后，回到弃牌堆。</summary>
    /// <remarks>
    /// 也就是说这张牌消耗不掉 —— 不镜像的话求解器会以为烧掉它就一了百了，
    /// 把一条实际上还会再吃伤害的路线算成安全的。
    /// </remarks>
    private static void WitheringAfterExhausted(
        Withering affliction,
        AfterCardExhaustedMirrorContext context)
    {
        if (context.PreviewCard is not Wither)
            return;
        context.Simulator.AddToPile([context.Card], PileType.Discard);
    }
}
