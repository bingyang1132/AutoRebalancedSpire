using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Helpers;
using CombatSolver.Engine.InCombat.Extensions;
using RebalancedSpire.Core.Afflictions;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using CombatSolver;
using CombatSolver.Engine.Common;
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

            // 猛击：原版 3 力量；改版 2。
            case ("LivingShield", "SMASH_MOVE") when settings.TurretOperator:
                combat.Apply<StrengthPower>(owner, 2, owner);
                __result = true;
                return false;

            // 充能：改版固定 2 力量（原版读怪物自己的静态值）。
            case ("Rocket", "CHARGE_UP_MOVE") when settings.KaiserCrab:
                combat.Apply<StrengthPower>(owner, 2, owner);
                __result = true;
                return false;

            // 践踏：原版虚弱 1 之外还给自己 3 层蒸汽；改版只剩虚弱 1。
            case ("WaterfallGiant", "STOMP_MOVE") when settings.WaterfallGiant:
                combat.ApplyFromMonster<WeakPower>(player, 1, owner);
                __result = true;
                return false;

            // 加压：蒸汽从 3 层提到 9 层。
            case ("WaterfallGiant", "PRESSURE_UP_MOVE") when settings.WaterfallGiant:
                combat.Apply<SteamEruptionPower>(owner, 9, owner);
                __result = true;
                return false;

            // 渐强：原版是「把手上的枯萎升一级再塞几张新的」，改版把那套挪到了凋零那一招，
            // 这一招换成给自己力量和 33 点格挡。
            case ("Aeonglass", "INCREASING_INTENSITY_MOVE") when settings.Aeonglass:
            {
                int intensity = TryStatic(combat, owner, "IncreasingIntensityTotalStrength");
                if (intensity > 0)
                    combat.Apply<StrengthPower>(owner, intensity, owner);
                if (simulator.HasPendingChoice)
                    return false;
                simulator.GainBlock(owner, 33, ValueProp.Move);
                __result = true;
                return false;
            }

            // 咒缚：原版 2 层诅咒；改版每个目标 1 层，外加自己一层虚无。
            case ("SpectralKnight", "HEX_MOVE") when settings.Knights:
                combat.ApplyFromMonster<HexPower>(player, 1, owner);
                combat.Apply<IntangiblePower>(owner, 1, owner);
                __result = true;
                return false;

            // 吸取拥抱：原版虚弱 3 + 自己 3 力量；改版只剩虚弱 3。
            case ("SlimedBerserker", "LEECHING_HUG_MOVE") when settings.SlimedBerserker:
                combat.ApplyFromMonster<WeakPower>(player, 3, owner);
                __result = true;
                return false;

            // 呕吐黏液：原版塞 10 张黏液；改版塞 5 张，并给自己一层「吸取拥抱」。
            case ("SlimedBerserker", "VOMIT_ICHOR_MOVE") when settings.SlimedBerserker:
                simulator.AddToCombat<Slimed>(player, PileType.Discard, 5, null);
                if (simulator.HasPendingChoice)
                    return false;
                combat.Apply<LeechingHugPower>(owner, 1, owner);
                __result = true;
                return false;

            // 沉思：原版回 30 × 人数；改版 20 × 人数。力量那半两边一样。
            case ("KnowledgeDemon", "PONDER_MOVE") when settings.KnowledgeDemon:
                simulator.Heal(owner, 20 * combat.Players.Count);
                if (simulator.HasPendingChoice)
                    return false;
                combat.Apply<StrengthPower>(
                    owner, combat.GetMonsterStaticInt(owner, "PonderStrength"), owner);
                __result = true;
                return false;

            // 苏醒：原版 10 力量；改版 3。
            case ("BygoneEffigy", "WAKE_MOVE") when settings.BygoneEffigy:
                combat.Apply<StrengthPower>(owner, 3, owner);
                __result = true;
                return false;

            // 快拳：原版上脆弱；改版改成虚弱。
            case ("PunchConstruct", "FAST_PUNCH_MOVE") when settings.PunchOff:
                combat.ApplyFromMonster<WeakPower>(player, 1, owner);
                __result = true;
                return false;

            // 狂怒：原版只加 3 力量；改版加甲 6（九阶 7）再加 2 力量。
            case ("SludgeSpinner", "RAGE_MOVE") when settings.SludgeSpinner:
                simulator.GainBlock(
                    owner,
                    AscensionHelper.GetValueIfAscension((AscensionLevel)9, 7, 6),
                    ValueProp.Move);
                if (simulator.HasPendingChoice)
                    return false;
                combat.Apply<StrengthPower>(owner, 2, owner);
                __result = true;
                return false;

            // 尖叫 / 分神：原版 3 张恍惚；改版 2 张。
            case ("Chomper", "SCREECH_MOVE") when settings.Chomper:
            case ("EyeWithTeeth", "DISTRACT_MOVE") when settings.Fogmog:
                simulator.AddToCombat<Dazed>(player, PileType.Discard, 2, null);
                __result = true;
                return false;

            // 喷火：原版 4 张灼烧进手牌；改版 2 张。
            case ("MechaKnight", "FLAMETHROWER_MOVE") when settings.MechaKnight:
                simulator.AddToCombat<Burn>(player, PileType.Hand, 2, null);
                __result = true;
                return false;

            // 剧毒：原版 2 张毒物进手牌；改版 1 张。
            case ("Myte", "TOXIC_MOVE") when settings.Myte:
                simulator.AddToCombat<Toxic>(player, PileType.Hand, 1, null);
                __result = true;
                return false;

            // 感染：原版固定 3 张；改版按自己身上「寄生+」的层数给，没有就一张都不给。
            case ("PhrogParasite", "INFECT_MOVE") when settings.PhrogParasite:
            {
                int infested = combat.GetAmount<InfestedPlusPower>(owner);
                if (infested > 0)
                    simulator.AddToCombat<Infection>(player, PileType.Discard, infested, null);
                __result = true;
                return false;
            }

            // 噪音：原版弃牌堆和抽牌堆各一张恍惚；改版两张都进弃牌堆。
            case ("Noisebot", "NOISE_MOVE") when settings.Fabricator:
                simulator.AddToCombat<Dazed>(player, PileType.Discard, 1, null);
                __result = true;
                return false;

            // 凋零：原版是「把手上的枯萎升一级再塞几张新的」；改版整个换了 ——
            // 给玩家挂一层 12 点的「凋零之威」，再塞 4 张已经假升级两级、且带「凋零」病症的枯萎，
            // 前两张进抽牌堆、后两张进弃牌堆。
            case ("Aeonglass", "WITHERING_MOVE") when settings.Aeonglass:
            {
                if (player.Player is not { } witherOwner)
                    return true;
                combat.ApplyTargeted<WitheringPresencePlusPower>(owner, player, 12, owner);
                if (simulator.HasPendingChoice)
                    return false;
                foreach ((PileType pile, int count) in new[] { (PileType.Draw, 2), (PileType.Discard, 2) })
                {
                    foreach (var added in simulator
                                 .CreateAndAddGeneratedCardsToCombat<Wither>(
                                     witherOwner, pile, count, witherOwner, CardPilePosition.Random))
                    {
                        if (added.CardAdded.MutablePreview is Wither wither)
                        {
                            wither.FakeUpgrade();
                            wither.FakeUpgrade();
                        }
                        simulator.Afflict<Withering>(added.CardAdded, 1m);
                    }
                    if (simulator.HasPendingChoice)
                        return false;
                }
                __result = true;
                return false;
            }

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

    /// <summary>读怪物身上一个静态数值，读不到就返回 0 并记一条风险。</summary>
    /// <remarks>
    /// 求解器只捕获它自己用得上的那些静态值。改版新引用的字段不一定在里面，读不到时宁可
    /// 少算一份加成并显示成红字，也不要把整条搜索抛断。
    /// </remarks>
    private static int TryStatic(SimulatedCombatState combat, Creature owner, string name)
    {
        try
        {
            return combat.GetMonsterStaticInt(owner, name);
        }
        catch (InvalidOperationException)
        {
            EngineDiagnostics.Warn($"[AutoRebalancedSpire] 读不到怪物静态值 {name}，这一份加成没算。");
            return 0;
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
