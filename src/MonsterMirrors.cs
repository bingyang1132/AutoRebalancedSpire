using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using CombatSolver;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Attack;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Death;
using CombatSolver.Engine.InCombat.Simulation;
using RebalancedSpire.Core.Configs;
using RebalancedSpire.Core.Powers;

namespace AutoRebalancedSpire;

/// <summary>
/// 被改写了**招式效果**的怪物。
/// </summary>
/// <remarks>
/// 44 张出招表（<c>GenerateMoveStateMachine</c>）不用管：求解器读的是怪物身上活的那张表，
/// 换了顺序自动跟随。22 处 <c>AfterAddedToRoom</c>（改血量、开局 Power）也是活的，同理。
/// 真正要补的只有被换掉实现的那几个**招式**。
///
/// 求解器模拟敌人招式走 <c>MonsterMoveEffects.Apply</c> 里一张按「怪物类型名 + 招式 id」的
/// 大表，不是注册表，所以只能挂前缀：认得的那几条自己结算并拦下，其余放行。
/// </remarks>
internal static class MonsterMirrors
{
    public static MethodInfo ResolveApplyTarget()
        => AccessTools.Method(typeof(MonsterMoveEffects), nameof(MonsterMoveEffects.Apply))
           ?? throw new MissingMethodException(
               nameof(MonsterMoveEffects), nameof(MonsterMoveEffects.Apply));

    public static int RegisterAll()
    {
        AfterDeathMirrors.Registry.Register<PingPongPower>(PingPong);
        return 1;
    }

    public static MethodInfo ResolveReviveTarget()
        => AccessTools.Method(
               typeof(SimulatedCombatState),
               nameof(SimulatedCombatState.ResolveReviveMove))
           ?? throw new MissingMethodException(
               nameof(SimulatedCombatState), nameof(SimulatedCombatState.ResolveReviveMove));

    /// <summary>幻影复活之后，把「幻灭」那几层力量收回去。</summary>
    /// <remarks>
    /// 改版 <c>IllusionPower.ReviveMove</c> 在治满之后，如果自己是恐惧蛛，就按场上
    /// <c>DisillusionPower</c> 的层数给自己一份等量的负力量。治满那半求解器本来就算对
    /// （通用的 <c>REVIVE_MOVE</c> 分支），所以这里只补负力量。
    /// </remarks>
    public static void RevivePostfix(
        SimulatedCombatState __instance,
        Creature creature,
        string moveId)
    {
        if (moveId != "REVIVE_MOVE" || !RebalancedSpireSettingsStore.Settings.TheObscura)
            return;
        if (creature.Monster is not Parafright)
            return;
        int disillusion = __instance.GetAmount<DisillusionPower>(creature);
        if (disillusion > 0)
            __instance.Apply<StrengthPower>(creature, -disillusion, creature);
    }

    /// <summary>惊惶：挨了强化攻击给自己起甲之外，改版还给自己一层负力量。</summary>
    /// <remarks>
    /// 求解器登记的处理只有起甲那半。判据逐条照抄它的：本回合还没起过甲、伤害算招式伤害、
    /// 伤害源是一张牌、而且自己确实吃到了未被格挡的伤害。
    /// </remarks>
    public static IEnumerable<MirroredHookReplacement> Replacements()
    {
        yield return new MirroredHookReplacement(
            typeof(SkittishPower),
            settings => settings.PhantasmalGardener,
            () => RegistryOverride.DropRegistration(AfterAttackMirrors.Registry, typeof(SkittishPower)),
            () => AfterAttackMirrors.Registry.Register<SkittishPower>(Skittish));
    }

    private static void Skittish(SkittishPower power, AfterAttackMirrorContext context)
    {
        SkittishPredictionState state = context.StateStore.Get(power, () => new SkittishPredictionState(power));
        if (state.HasGainedBlockThisTurn
            || !context.Command.DamageProps.HasFlag(ValueProp.Move)
            || context.Command.ModelSource is not CardModel)
        {
            return;
        }

        DamageResult? damage = context.Command.Results
            .SelectMany(results => results)
            .FirstOrDefault(result => result.Receiver == power.Owner);
        if (damage?.UnblockedDamage is not > 0)
            return;

        state.HasGainedBlockThisTurn = true;
        context.Simulator.GainBlock(power.Owner, power.Amount, ValueProp.Unpowered);
        if (context.CombatState is SimulatedCombatState combat)
            combat.Apply<StrengthPower>(power.Owner, -1, power.Owner);
    }

    public static bool ApplyPrefix(
        CombatPredictionSimulator simulator,
        SimulatedCombatState combat,
        ForecastMove move,
        Creature player,
        ref bool __result)
    {
        RebalancedSpireSettings settings = RebalancedSpireSettingsStore.Settings;
        Creature owner = move.Owner;
        switch (owner.Monster?.GetType().Name, move.Move.Id)
        {
            // 缠绕：3 层 → 2 层。
            case ("SlitheringStrangler", "CONSTRICT") when settings.SlitheringStrangler:
                combat.Apply<ConstrictPower>(player, 2, owner);
                __result = true;
                return false;

            // 瘴气：格挡从固定 8 改成 8 + 自己当前的敏捷（偷完之前读，顺序照原样）。
            case ("TheForgotten", "MIASMA") when settings.TheLostAndForgotten:
            {
                int steal = combat.GetMonsterStaticInt(owner, "DebilitatingSmogDexStealAmount");
                combat.Apply<DexterityPower>(player, -steal, owner);
                simulator.GainBlock(owner, 8 + combat.GetAmount<DexterityPower>(owner), ValueProp.Move);
                combat.Apply<DexterityPower>(owner, steal, owner);
                __result = true;
                return false;
            }

            // 液化：在原版的流沙 4 和 6 张仓皇逃窜之外，多给一层 5 的「长距离」。
            case ("TheInsatiable", "LIQUIFY_GROUND_MOVE") when settings.TheInsatiable:
                combat.ApplyTargeted<SandpitPower>(owner, player, 4, owner);
                combat.Apply<LongDistancePower>(player, 5, owner);
                simulator.AddToCombat<FranticEscape>(player, PileType.Draw, 3, null, CardPilePosition.Random);
                simulator.AddToCombat<FranticEscape>(player, PileType.Discard, 3, null, CardPilePosition.Random);
                __result = true;
                return false;

            // 辐射：改版只剩攻击，原版那份加甲没了。
            case ("InfestedPrism", "RADIATE_MOVE") when settings.InfestedPrism:
                __result = true;
                return false;

            // 脉动：改版给自己 4 点力量，原版是加甲 + 生命火花。
            case ("InfestedPrism", "PULSATE_MOVE") when settings.InfestedPrism:
                combat.Apply<StrengthPower>(owner, 4, owner);
                __result = true;
                return false;

            // 自爆：炸之前先把「乒乓」摘掉，所以自爆不会反伤生成它的迷雾。
            // 摘完仍然放行原实现 —— 伤害和「炸完自己也没了」求解器本来就算对。
            case ("GasBomb", "EXPLODE_MOVE") when settings.LivingFog:
                if (combat.GetMutablePower<PingPongPower>(owner) is { Amount: > 0 } pingPong)
                    combat.SetPowerAmount(pingPong, 0);
                return true;

            default:
                return true;
        }
    }

    /// <summary>乒乓：挂着它的怪物被打死时，反伤给放它出来的那只，伤害等于死者的最大生命。</summary>
    /// <remarks>
    /// 也就是「打死毒气弹会伤到生成它的迷雾」。不镜像的话求解器看不到这条收益，
    /// 会把「先清小怪」这种正确打法压掉。层数在施加时被设成了主人的最大生命，这里直接用它。
    /// </remarks>
    private static void PingPong(PingPongPower power, AfterDeathMirrorContext context)
    {
        if (context.WasRemovalPrevented || context.Creature != power.Owner)
            return;
        if (power.Applier is not { } applier)
            return;
        context.Simulator.Damage([applier], power.Amount, ValueProp.Move, applier);
    }
}
