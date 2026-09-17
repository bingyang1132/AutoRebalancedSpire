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
        AfterCardPlayedMirrors.Registry.Register<LongDistancePower>(LongDistance);
        return 5;
    }

    private const string HandDrawName = nameof(TurnStartPowerSupport.TriggerBeforeHandDraw);

    public static MethodInfo ResolveHandDrawTarget()
        => AccessTools.Method(typeof(TurnStartPowerSupport), HandDrawName)
           ?? throw new MissingMethodException(nameof(TurnStartPowerSupport), HandDrawName);

    /// <summary>发牌之前：无尽之刃+ 造匕首，必然结局+ 挑几张牌放到牌堆顶。</summary>
    /// <remarks>
    /// 求解器这个时点是 <c>TurnStartPowerSupport.TriggerBeforeHandDraw</c> 里一段按类型写死的
    /// 流程，第三方 Power 进不去，也**不记未镜像风险**（<c>BeforeHandDraw</c> 根本没有镜像注册表，
    /// 风险只在注册表那条路上才记），所以不补就是静默算错，只能挂在它后面。
    ///
    /// 返回值要跟着改：这个方法的 <c>bool</c> 是「有没有产生待处理选择」，调用方拿它当搜索边界。
    /// 我们这一段一旦挂起选择却不回写 <c>__result</c>，后面的遗物、噩梦结算都会带着未决选择继续跑。
    /// </remarks>
    public static void HandDrawPostfix(
        CombatPredictionSimulator simulator,
        SimulatedCombatState combat,
        Player player,
        TurnStartChoiceCursor choices,
        ref bool __result)
    {
        if (__result || combat.HasPendingChoice)
            return;

        foreach (PowerModel power in combat.EffectivePowers().ToArray())
        {
            if (power.Amount <= 0 || !ReferenceEquals(power.Owner.Player, player))
                continue;

            switch (power)
            {
                case InfiniteBladesPlusPower blades:
                    simulator.CreateAndAddGeneratedCardsToCombat<Shiv>(
                        player, PileType.Hand, blades.Amount, player);
                    break;

                // 必然结局+：洗牌（如有必要）之后从抽牌堆挑 Amount 张放到牌堆顶。和原版的差别有两处：
                // 原版是挑完进手牌并把自己移除，改版是挑完放牌堆顶、自己留着，每回合都来一次。
                case ForegoneConclusionPlusPower:
                {
                    SimPlayerCombatState state = simulator.State.GetPlayerCombatState(player);
                    if (state.DrawPile.IsEmpty && !state.DiscardPile.IsEmpty)
                    {
                        simulator.Shuffle(player);
                        if (combat.HasPendingChoice)
                        {
                            __result = true;
                            return;
                        }
                    }
                    if (!TurnChoiceMirrors.ResolveMoveToDrawTop(
                            simulator, combat, player, choices, power.Id.Entry, power.Amount))
                    {
                        __result = true;
                        return;
                    }
                    break;
                }

                default:
                    continue;
            }

            if (combat.HasPendingChoice)
            {
                __result = true;
                return;
            }
        }
    }

    /// <summary>长距离：自己打出一张仓皇逃窜就涨一层，涨到 11 层沙虫直接退场。</summary>
    /// <remarks>
    /// 这一层数是伤害倍率的唯一输入（挨打和打沙虫各一条曲线），少算一层整场的伤害预期全错。
    /// 求解器自己会报 <c>COVERAGE source=..._LONG_DISTANCE_POWER method=AfterCardPlayed
    /// reason=MethodNotMirrored</c>，但那只是记一条风险，数值照样按没涨算。
    ///
    /// 涨层走 <c>SetPowerAmount</c> 而不是 <c>Apply</c>：原版这里用的是
    /// <c>PowerCmd.ModifyAmount</c>，不过遗物的施加修正，也不吃神器。
    ///
    /// 11 层那一段原版是「沙虫吃饱走人」：把场上所有沙虫移出战斗，另外发一瓶药水和一个稀有
    /// 遗物。退场用求解器的逃跑口径镜像；两份局外奖励没有镜像，求解器的长期收益会低估这条路线。
    /// </remarks>
    private static void LongDistance(LongDistancePower power, AfterCardPlayedMirrorContext context)
    {
        if (context.PreviewCard is not FranticEscape)
            return;
        if (context.PreviewCard.Owner.Creature != power.Owner)
            return;
        if (context.CombatState is not SimulatedCombatState combat)
            return;

        // SetPowerAmount 改的是分支里那份可变实例，power 自己可能还是根上的只读副本，
        // 所以阈值要重新读一遍。
        combat.SetPowerAmount(power, power.Amount + 1);
        if (combat.GetAmount<LongDistancePower>(power.Owner) < LongDistanceEscapeAmount)
            return;
        foreach (Creature enemy in combat.Enemies.ToArray())
        {
            if (enemy.Monster is TheInsatiable)
                combat.CreatureEscaped(enemy);
        }
    }

    /// <summary>改版写死的 <c>LongDistancePower.MaxAmount</c>。</summary>
    private const int LongDistanceEscapeAmount = 11;

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
