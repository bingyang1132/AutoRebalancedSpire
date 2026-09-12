using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Extensions;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Card;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Damage;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Resources;
using CombatSolver.Engine.InCombat.Simulation;
using RebalancedSpire.Core.Powers;

namespace AutoRebalancedSpire;

/// <summary>
/// RebalancedSpire 新增的 Power 的钩子镜像。
/// </summary>
/// <remarks>
/// 不补这些的后果分两种：走注册表的钩子会记一条未镜像风险（红字，至少看得见），
/// 而求解器写死分发的那几个时点连风险都不记，直接静默算错。
/// </remarks>
internal static class PowerMirrors
{
    public static int RegisterAll()
    {
        AfterEnergyResetMirrors.Registry.Register<SpinnerPlusPower>(SpinnerPlus);
        AfterCardDiscardedMirrors.Registry.Register<MasterPlannerPlusPower>(MasterPlannerPlus);
        AfterDamageGivenMirrors.Registry.Register<ReaperFormPlusPower>(ReaperFormPlus);
        AfterDamageGivenMirrors.Registry.Register<SicEmPlusPower>(SicEmPlus);
        return 4;
    }

    private const string HandDrawName = nameof(TurnStartPowerSupport.TriggerBeforeHandDraw);

    public static MethodInfo ResolveHandDrawTarget()
        => AccessTools.Method(typeof(TurnStartPowerSupport), HandDrawName)
           ?? throw new MissingMethodException(nameof(TurnStartPowerSupport), HandDrawName);

    /// <summary>发牌之前：无尽之刃+ 造匕首。</summary>
    /// <remarks>
    /// 求解器这个时点是 <c>TurnStartPowerSupport.TriggerBeforeHandDraw</c> 里一段按类型写死的
    /// 流程，第三方 Power 进不去，只能挂在它后面。
    ///
    /// 必然结局+ 的那一半（从抽牌堆挑几张放到牌堆顶）没做：那是一次玩家选择，而且改的是抽牌
    /// 顺序，求解器要为它开一条选择分支才谈得上镜像。没做的部分会以未镜像风险显示成红字。
    /// </remarks>
    public static void HandDrawPostfix(
        CombatPredictionSimulator simulator,
        SimulatedCombatState combat,
        Player player)
    {
        if (simulator.HasPendingChoice)
            return;

        foreach (PowerModel power in combat.EffectivePowers().ToArray())
        {
            if (power.Amount <= 0 || !ReferenceEquals(power.Owner.Player, player))
                continue;
            if (power is not InfiniteBladesPlusPower blades)
                continue;

            simulator.CreateAndAddGeneratedCardsToCombat<Shiv>(
                player, PileType.Hand, blades.Amount, player);
            if (simulator.HasPendingChoice)
                return;
        }
    }

    /// <summary>神机妙算+：弃掉一张带「狡诈」的牌时抽等量的牌。</summary>
    private static void MasterPlannerPlus(
        MasterPlannerPlusPower power,
        AfterCardDiscardedMirrorContext context)
    {
        if (!ReferenceEquals(context.PreviewCard.Owner, power.Owner.Player))
            return;
        if (!context.Card.GetKeywords(context.State).Contains(CardKeyword.Sly))
            return;
        context.Simulator.Draw(power.Owner.Player, power.Amount);
    }

    /// <summary>死神形态+：自己或奥斯提打出的强化攻击造成伤害后，按伤害 × 层数给目标上「末日」。</summary>
    private static void ReaperFormPlus(
        ReaperFormPlusPower power,
        AfterDamageGivenMirrorContext context)
    {
        Creature? dealer = context.Dealer;
        if (dealer == null)
            return;
        if (dealer != power.Owner && dealer.PetOwner?.Creature != power.Owner)
            return;
        if (!context.Props.IsPoweredAttack() || context.Result.TotalDamage <= 0)
            return;

        if (context.CombatState is ICombatPredictionEffectSink effects)
        {
            effects.ApplyPower(
                typeof(DoomPower),
                context.Target,
                context.Result.TotalDamage * power.Amount,
                power.Owner);
        }
    }

    /// <summary>叫咬+：奥斯提打到挂着这层的敌人时，给奥斯提的主人再召唤等量的奥斯提。</summary>
    private static void SicEmPlus(SicEmPlusPower power, AfterDamageGivenMirrorContext context)
    {
        if (context.Dealer?.Monster is not Osty osty)
            return;
        if (osty.Creature.PetOwner is not { } petOwner || context.Target != power.Owner)
            return;
        if (context.CombatState is SimulatedCombatState combat)
            combat.SummonOsty(context.Simulator, petOwner, power.Amount);
    }

    /// <summary>
    /// 纺纱+：每回合重置能量之后按层数充能玻璃球，然后把场上**所有**玻璃球各触发一次被动。
    /// </summary>
    /// <remarks>
    /// 和原版 <c>SpinnerPower</c> 的差别就是后面那一段 —— 原版只充能。触发的是
    /// <c>OrbCmd.Passive(..., countAffectedByHooks: false)</c>，对应求解器的
    /// <c>OrbPassive</c> 而不是 <c>TriggerOrbPassive</c>：后者会先过
    /// <c>ModifyOrbPassiveTriggerCount</c>，而原版这一句明确要求不过钩子。
    ///
    /// 遍历前先拷一份：被动会改队列。每一步都查 <c>HasPendingChoice</c>，起了选择就停下，
    /// 和求解器自己那几个 handler 的写法一致。
    /// </remarks>
    private static void SpinnerPlus(SpinnerPlusPower power, AfterEnergyResetMirrorContext context)
    {
        CombatPredictionSimulator simulator = context.Simulator;
        simulator.OrbChannel<GlassOrb>(context.Player, power.Amount);
        if (simulator.HasPendingChoice)
            return;

        OrbModel[] orbs = simulator.State.GetPlayerCombatState(context.Player)
            .OrbQueue.Orbs.OfType<GlassOrb>().Cast<OrbModel>().ToArray();
        foreach (OrbModel orb in orbs)
        {
            simulator.OrbPassive(orb);
            if (simulator.HasPendingChoice)
                return;
        }
    }
}
