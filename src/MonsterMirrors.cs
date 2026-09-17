using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
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
using RebalancedSpire.Core.Cards;

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
        AfterDeathMirrors.Registry.Register<HungerPower>(RemoveWhenApplierDies);
        AfterDeathMirrors.Registry.Register<ScrutinyPower>(RemoveWhenApplierDies);
        // 寄生+：它的 AfterDeath 会生小虫，但那份效果求解器是在**领域清理**那一关算的
        // （DeathPowerSupport.Trigger，见 DeathSpawnPatch），和原版的寄生走同一条路。
        // 这里登记成「已审阅、这个钩子本身不用再算一遍」，否则会记一条没镜像的死亡钩子，
        // 求解器就不敢把打赢那条路线算完。原版的 InfestedPower 是被上游写死在
        // PredictionCoverage 的白名单里放行的，第三方进不去那张表。
        AfterDeathMirrors.Registry.RegisterIgnored<InfestedPlusPower>();
        return 4;
    }

    /// <summary>饥饿 / 审视：施加者死了，病症跟着消失。</summary>
    /// <remarks>
    /// 效果本身不大，但**不登记的代价很大**：`AfterDeath` 这个名字里带 Death，
    /// 求解器一旦在某条能打赢的路线上发现没镜像的死亡钩子，就把这条路线标成
    /// <c>UnsupportedEffect</c> 边界、不敢往下算。表现出来是路线只有半个回合，
    /// 打完就「计划用尽」重算一次。
    /// </remarks>
    private static void RemoveWhenApplierDies(PowerModel power, AfterDeathMirrorContext context)
    {
        if (context.WasRemovalPrevented || !ReferenceEquals(context.Creature, power.Applier))
            return;
        if (context.CombatState is not SimulatedCombatState combat)
            return;
        combat.SetPowerAmount(power, 0);
    }

    /// <summary>改版 <c>ParafrightPatch.AfterAddedToRoom</c> 里写死的幻灭层数。</summary>
    private const int DisillusionAmount = 4;

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
        if (moveId != "REVIVE_MOVE" || !AdapterSettings.Current.TheObscura)
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
        RebalancedSpireSettings settings = AdapterSettings.Current;
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

            // 幻象：召唤恐惧蛛那半和原版一样，改版多给它 4 层「幻灭」——复活时按这个层数扣力量。
            case ("TheObscura", "ILLUSION_MOVE") when settings.TheObscura:
            {
                Creature illusion = MonsterSpawnSupport.Spawn<Parafright>(
                    simulator, combat, owner, "illusion");
                combat.Apply<DisillusionPower>(illusion, DisillusionAmount, illusion);
                combat.SetMonsterBool(owner, "_hasSummoned", true);
                __result = true;
                return false;
            }

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

            // 冲撞：原版打一下之外给自己 3 层蒸汽；改版只剩那一下，蒸汽没了。
            // 改版方法体（WaterfallGiantPatch.RamMove）只有一句 DamageCmd.Attack，
            // 伤害本身走意图、自动跟随，所以这里什么都不做，只是把求解器那 3 层挡掉。
            case ("WaterfallGiant", "RAM_MOVE") when settings.WaterfallGiant:
                __result = true;
                return false;

            // 压力枪：原版打一下、把自己的压力枪伤害累加、再给自己 3 层蒸汽；改版去掉了蒸汽。
            // 累加那一步要留着，不然多回合的计划会一直按第一回合的伤害推。
            case ("WaterfallGiant", "PRESSURE_GUN_MOVE") when settings.WaterfallGiant:
                combat.IncreasePressureGun(owner, combat.GetMonsterStaticInt(owner, "PressureGunIncrease"));
                __result = true;
                return false;

            // 加压：蒸汽从 3 层提到 9 层。
            case ("WaterfallGiant", "PRESSURE_UP_MOVE") when settings.WaterfallGiant:
                combat.Apply<SteamEruptionPower>(owner, 9, owner);
                __result = true;
                return false;

            // 渐强：原版是「把手上的枯萎升一级再塞几张新的」，改版把那套挪到了凋零那一招，
            // 这一招换成给自己力量和 33 点格挡。
            // 退潮：原版打一下之外给自己加甲；改版只剩那一下。
            // 改版方法体（AeonglassPatch.EbbMove）只有一句 DamageCmd.Attack。
            case ("Aeonglass", "EBB_MOVE") when settings.Aeonglass:
                __result = true;
                return false;

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
            // 招式 id 是 "HEX" 不是 "HEX_MOVE"。原来写错成后者，这一段从来没被命中过，
            // 求解器一直按原版的 2 层诅咒算、也不给虚无。原版出招表
            // （SpectralKnight.GenerateMoveStateMachine 里 new MoveState("HEX", ...)）
            // 和求解器的 MonsterMoveEffects 两边都是 "HEX"。
            case ("SpectralKnight", "HEX") when settings.Knights:
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

            // 信息素喷吐：改版把三个分支全改了。
            //   没有蜂巢       → 蜂巢 +1（原版是力量 +2）
            //   蜂巢层数 < 3   → 蜂巢 +2、力量 +1（原版是蜂巢 +1、力量 +1）
            //   蜂巢层数 >= 3  → 力量 +2（和原版一样）
            // 读的是「当下有没有蜂巢」，所以必须按顺序判，不能合并。
            case ("Entomancer", "PHEROMONE_SPIT_MOVE") when settings.Entomancer:
            {
                PersonalHivePower? hive = combat.GetPower<PersonalHivePower>(owner);
                if (hive == null)
                {
                    combat.Apply<PersonalHivePower>(owner, 1, owner);
                }
                else if (hive.Amount < 3)
                {
                    combat.Apply<PersonalHivePower>(owner, 2, owner);
                    combat.Apply<StrengthPower>(owner, 1, owner);
                }
                else
                {
                    combat.Apply<StrengthPower>(owner, 2, owner);
                }
                __result = true;
                return false;
            }

            // 缠绕藤蔓：原版只上 1 层缠绕；改版在那之外给自己加甲 8（九阶 9）。
            // 改版把意图换成了 DefendIntent + CardDebuffIntent，格挡量不在意图里，
            // 所以求解器看不到这份格挡，必须在这里补。
            case ("VineShambler", "GRASPING_VINES_MOVE") when settings.VineShambler:
                combat.ApplyFromMonster<TangledPower>(player, 1, owner);
                simulator.GainBlock(
                    owner,
                    AscensionHelper.GetValueIfAscension((AscensionLevel)9, 9, 8),
                    ValueProp.Move);
                __result = true;
                return false;

            // 凝视：原版打一下之外往弃牌堆塞 GazeMoveAmount 张「召唤」；改版只剩那一下。
            // 改版方法体（SoulFyshPatch.GazeMove）只有一句 DamageCmd.Attack。
            case ("SoulFysh", "GAZE_MOVE") when settings.SoulFysh:
                __result = true;
                return false;

            // ---------- 改版新加的招式：求解器那张表里没有，不补就会被标成「不支持」 ----------

            // 灼热咆哮：改版把张数和力量都调低了，而且调的是补丁类自己的常量，
            // 求解器读的是原版怪物身上的 BurningGrowlBurnCount / BurningGrowlStrengthGain
            // 那两个字段——它们没被补丁动过，所以这一条不会自动跟随。
            //   灼烧张数：原版 5/3（九阶/普通），改版 4/3
            //   力量层数：原版 3/2，改版 2/1
            // 张数会进意图（StatusIntent），所以不会有红字提示，但塞进弃牌堆的张数是错的。
            case ("TestSubject", "BURNING_GROWL_MOVE") when settings.TestSubject:
                simulator.AddToCombat<Burn>(
                    player,
                    PileType.Discard,
                    AscensionHelper.GetValueIfAscension((AscensionLevel)9, 4, 3),
                    null);
                if (simulator.HasPendingChoice)
                    return false;
                combat.Apply<StrengthPower>(
                    owner,
                    AscensionHelper.GetValueIfAscension((AscensionLevel)9, 2, 1),
                    owner);
                __result = true;
                return false;

            // 咆哮：给自己 3 层狂怒。
            case ("TestSubject", "GROWL_MOVE") when settings.TestSubject:
                combat.Apply<EnragePower>(owner, 3, owner);
                __result = true;
                return false;

            // 备战 / 备战二：改版只剩台词。原版的备战是加甲，求解器照原版算会多给一份格挡。
            case ("MagiKnight", "PREP_MOVE") when settings.Knights:
            case ("MagiKnight", "PREP_2_MOVE") when settings.Knights:
                __result = true;
                return false;

            // 蓄势：改版同样只剩演出，原版是给自己 2 力量。
            case ("Vantom", "PREPARE_MOVE") when settings.Vantom:
                __result = true;
                return false;

            // 连发炮击一 / 二：改版只剩那一炮。原版这两招除了伤害还各给自己 2 力量，
            // 求解器照原版口径给，两回合下来就多出 4 点力量，一到实机就对不上、整局重算。
            // 蓄能还是给 2 力量，那条没变。
            case ("CubexConstruct", "REPEATER_BLAST_MOVE") when settings.CubexConstruct:
            case ("CubexConstruct", "REPEATER_BLAST_MOVE_2") when settings.CubexConstruct:
                __result = true;
                return false;

            // 汲取生命：改版只剩那一刀，原版跟着的易伤 2、虚弱 2 都没了。
            // 不补的话求解器每算一次这一招都会白给玩家两层减益，一到实机就对不上、整局重算。
            case ("SoulNexus", "DRAIN_LIFE_MOVE") when settings.SoulNexus:
                __result = true;
                return false;

            // 魂印：收掉自己身上所有「枯魂」，给玩家 99 层易伤。
            case ("SoulNexus", "SOUL_MARK_MOVE") when settings.SoulNexus:
                foreach (SoulWitherPower soulWither in combat.EffectivePowers()
                             .OfType<SoulWitherPower>()
                             .Where(candidate => ReferenceEquals(candidate.Owner, owner))
                             .ToArray())
                {
                    combat.SetPowerAmount(soulWither, 0);
                }
                combat.ApplyFromMonster<VulnerablePower>(player, 99, owner);
                __result = true;
                return false;

            // 逃跑：组装师血量不够再造机器人时直接离场。
            case ("Fabricator", "ESCAPE_MOVE") when settings.Fabricator:
                combat.CreatureEscaped(owner);
                __result = true;
                return false;

            // 组装：和原版一样造两台，但改版每造一台自己掉最大生命的 1/15。
            // 少算这份自伤会让组装师显得比实际难杀得多。
            case ("Fabricator", "FABRICATE_MOVE") when settings.Fabricator:
            {
                int selfDamage = SpawnBotDamage(simulator, owner);
                foreach (bool defensive in new[] { true, false })
                {
                    MonsterMoveEffects.SpawnFabricatorBot(simulator, combat, owner, defensive);
                    if (simulator.HasPendingChoice)
                        return false;
                    simulator.Damage(
                        owner,
                        selfDamage,
                        ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.SkipHurtAnim,
                        owner);
                    if (simulator.HasPendingChoice)
                        return false;
                }
                __result = true;
                return false;
            }

            // 举盾：给自己加甲。
            case ("LivingShield", "SHIELD_UP_MOVE") when settings.TurretOperator:
                simulator.GainBlock(
                    owner,
                    AscensionHelper.GetValueIfAscension((AscensionLevel)9, 25, 20),
                    ValueProp.Move);
                __result = true;
                return false;

            // 弱化黏液 / 挥击：都是给玩家 2 层虚弱。
            case ("HunterKiller", "WEAK_GOOP_MOVE") when settings.HunterKiller:
            case ("ThievingHopper", "ATTACK_MOVE") when settings.ThievingHopper:
                combat.ApplyFromMonster<WeakPower>(player, 2, owner);
                __result = true;
                return false;

            // 第一踏 / 第二踏：按初始最大生命的 80% / 40% 给自己挂「耕耘+」的阈值。
            case ("CeremonialBeast", "FIRST_STAMP_MOVE") when settings.CeremonialBeast:
            case ("CeremonialBeast", "SECOND_STAMP_MOVE") when settings.CeremonialBeast:
            {
                float ratio = move.Move.Id == "FIRST_STAMP_MOVE" ? 0.8f : 0.4f;
                int threshold = (int)((float)TryStatic(combat, owner, "MaxInitialHp") * ratio);
                combat.Apply<PlowPlusPower>(owner, threshold, owner);
                __result = true;
                return false;
            }

            // 守护：加甲并给自己挂「守护」（那层让祭司在有人没破甲时不可选中）。
            case ("KinFollower", "GUARD_MOVE") when settings.TheKin:
                simulator.GainBlock(
                    owner,
                    AscensionHelper.GetValueIfAscension((AscensionLevel)9, 15, 13),
                    ValueProp.Move);
                if (simulator.HasPendingChoice)
                    return false;
                combat.Apply<GuardPower>(owner, 1, owner);
                __result = true;
                return false;

            // 假守护：只加甲，不挂「守护」。
            case ("KinFollower", "GUARD_FAKE_MOVE") when settings.TheKin:
                simulator.GainBlock(
                    owner,
                    AscensionHelper.GetValueIfAscension((AscensionLevel)9, 10, 8),
                    ValueProp.Move);
                __result = true;
                return false;

            // 复仇之舞：给自己力量。
            case ("KinFollower", "REVENGE_DANCE_MOVE") when settings.TheKin:
                combat.Apply<StrengthPower>(
                    owner,
                    AscensionHelper.GetValueIfAscension((AscensionLevel)9, 8, 6),
                    owner);
                __result = true;
                return false;

            // 假力量之舞：按玩家人数治自己。
            case ("KinFollower", "POWER_DANCE_FAKE_MOVE") when settings.TheKin:
                simulator.Heal(
                    owner,
                    AscensionHelper.GetValueIfAscension((AscensionLevel)9, 8, 6) * combat.Players.Count);
                __result = true;
                return false;

            // 逃跑：假随从演完就走。奖励是战斗外的事，这里只让它离场。
            case ("KinFollower", "ESCAPE_MOVE") when settings.TheKin:
                if (combat.GetAmount<MinionFakePower>(owner) > 0)
                    combat.CreatureEscaped(owner);
                __result = true;
                return false;

            // 护卫：祭司召两只信徒，第二只开局就跳舞。
            case ("KinPriest", "GUARD_MOVE") when settings.TheKin:
            {
                Creature guard = MonsterSpawnSupport.Spawn<KinFollower>(
                    simulator, combat, owner, "slot1");
                KinFollowerEntrance(simulator, combat, guard, startsWithDance: false);
                if (simulator.HasPendingChoice)
                    return false;
                Creature dancer = MonsterSpawnSupport.Spawn<KinFollower>(
                    simulator, combat, owner, "slot2",
                    configure: follower => follower.StartsWithDance = true);
                KinFollowerEntrance(simulator, combat, dancer, startsWithDance: true);
                __result = true;
                return false;
            }

            // 强化 / 护盾 / 治疗：都只作用在「除祭司以外的敌人」身上。
            case ("KinPriest", "POWER_UP_MOVE") when settings.TheKin:
            case ("KinPriest", "SHIELD_UP_MOVE") when settings.TheKin:
            case ("KinPriest", "HEAL_UP_MOVE") when settings.TheKin:
            {
                string moveId = move.Move.Id;
                foreach (Creature ally in combat.Enemies
                             .Where(static candidate => candidate.Monster is not KinPriest)
                             .ToArray())
                {
                    switch (moveId)
                    {
                        case "POWER_UP_MOVE":
                            combat.Apply<StrengthPower>(
                                ally,
                                AscensionHelper.GetValueIfAscension((AscensionLevel)9, 3, 2),
                                owner);
                            break;
                        case "SHIELD_UP_MOVE":
                            simulator.GainBlock(
                                ally,
                                AscensionHelper.GetValueIfAscension((AscensionLevel)9, 10, 8),
                                ValueProp.Move);
                            break;
                        default:
                            simulator.Heal(
                                ally,
                                AscensionHelper.GetValueIfAscension((AscensionLevel)9, 6, 5)
                                    * combat.Players.Count);
                            break;
                    }
                    if (simulator.HasPendingChoice)
                        return false;
                }
                __result = true;
                return false;
            }

            // 击溃：攻击之外再给玩家各 1 层脆弱和虚弱。
            case ("KinPriest", "BREAK_UP_MOVE") when settings.TheKin:
                combat.ApplyFromMonster<FrailPower>(player, 1, owner);
                combat.ApplyFromMonster<WeakPower>(player, 1, owner);
                __result = true;
                return false;

            // 发怒：给自己一层「领地」，把玩家牌里的拜尔多尼斯之卵全部标记，再给玩家 2 层脆弱。
            case ("Byrdonis", "ANGRY_MOVE") when settings.Byrdonis:
            {
                combat.Apply<TerritorialPower>(owner, 1, owner);
                if (simulator.HasPendingChoice)
                    return false;
                foreach (Player target in combat.Players)
                {
                    foreach (PredictedCard card in simulator.State
                                 .GetPlayerCombatState(target).AllCards.ToArray())
                    {
                        if (card.Preview is ByrdonisEgg)
                            simulator.Afflict<ToItsOriginOwner>(card, 1m);
                    }
                }
                combat.ApplyFromMonster<FrailPower>(player, 2, owner);
                __result = true;
                return false;
            }

            // 增殖：层数不满 4 就给自己加一层「寄生+」，再给玩家 2 层虚弱。
            // 层数超过 2 之后每加一层还会长 20% 最大生命并把长出来的那份治满。
            case ("PhrogParasite", "PROLIFERATION_MOVE") when settings.PhrogParasite:
            case ("PhrogParasite", "PROLIFERATION_2_MOVE") when settings.PhrogParasite:
            case ("PhrogParasite", "PROLIFERATION_3_MOVE") when settings.PhrogParasite:
            {
                if (combat.GetAmount<InfestedPlusPower>(owner) < 4)
                {
                    combat.Apply<InfestedPlusPower>(owner, 1, owner);
                    if (simulator.HasPendingChoice)
                        return false;
                    if (combat.GetAmount<InfestedPlusPower>(owner) > 2)
                    {
                        SimCreatureState state = simulator.State.GetCreature(owner);
                        int gain = (int)((float)state.MaxHp * 0.2f);
                        state.SetMaxHp(state.MaxHp + gain);
                        simulator.Heal(owner, gain);
                        if (simulator.HasPendingChoice)
                            return false;
                    }
                }
                combat.ApplyFromMonster<WeakPower>(player, 2, owner);
                __result = true;
                return false;
            }

            // 感染二：和感染同一个实现，只是出招表里多排了一次。
            case ("PhrogParasite", "INFECT_2_MOVE") when settings.PhrogParasite:
            {
                int infested2 = combat.GetAmount<InfestedPlusPower>(owner);
                if (infested2 > 0)
                    simulator.AddToCombat<Infection>(player, PileType.Discard, infested2, null);
                __result = true;
                return false;
            }

            // 打我：和自己人互殴 —— 对**其他每只敌人**打一份快拳伤害。
            case ("PunchConstruct", "FIGHT_WITH_ME") when settings.PunchOff:
            {
                int punch = TryStatic(combat, owner, "FastPunchDamage");
                foreach (Creature enemy in combat.Enemies
                             .Where(candidate => !ReferenceEquals(candidate, owner))
                             .ToArray())
                {
                    simulator.Damage([enemy], punch, ValueProp.Move, owner);
                    if (simulator.HasPendingChoice)
                        return false;
                }
                __result = true;
                return false;
            }

            // ---------- 改版换过实现、但求解器那张表里本来就没有的九条 ----------
            // 原版下这九条同样不模拟，所以严格说不算「回退」；补掉之后这几只怪在改版下
            // 反而比原版算得准。放在最后是因为优先级最低，不是因为它们不重要。

            // 斩击：攻击之外给自己 3 力量。
            case ("BygoneEffigy", "SLASHES_MOVE") when settings.BygoneEffigy:
                combat.Apply<StrengthPower>(owner, 3, owner);
                __result = true;
                return false;

            // 增大打击：给玩家 2 层虚弱。
            // 虫刺：原版多段打完还给玩家 2 虚弱 + 2 脆弱；改版只剩那几下。
            // 改版方法体（CrusherPatch.BugStingMove）只有一句多段 DamageCmd.Attack。
            case ("Crusher", "BUG_STING_MOVE") when settings.KaiserCrab:
                __result = true;
                return false;

            case ("Crusher", "ENLARGING_STRIKE_MOVE") when settings.KaiserCrab:
                combat.ApplyFromMonster<WeakPower>(player, 2, owner);
                __result = true;
                return false;

            // 瞄准镜：给玩家 2 层脆弱。
            case ("Rocket", "TARGETING_RETICLE_MOVE") when settings.KaiserCrab:
                combat.ApplyFromMonster<FrailPower>(player, 2, owner);
                __result = true;
                return false;

            // 激光：打完之后自己掉 10 点（不可格挡、不吃增益）。
            // 这一条是**静默**的 —— 它的意图只有攻击，求解器不会把它标成不支持，
            // 但那 10 点自伤完全没算，火箭会显得比实际难杀。
            case ("Rocket", "LASER_MOVE") when settings.KaiserCrab:
                simulator.Damage(
                    owner, 10, ValueProp.Unblockable | ValueProp.Unpowered, owner);
                __result = true;
                return false;

            // 增厚：给自己 1 力量。
            case ("DecimillipedeSegment", "BULK_MOVE") when settings.Decimillipede:
                combat.Apply<StrengthPower>(owner, 1, owner);
                __result = true;
                return false;

            // 疾冲：攻击之外给自己加甲。
            case ("SkulkingColony", "ZOOM_MOVE") when settings.SkulkingColony:
                simulator.GainBlock(
                    owner,
                    AscensionHelper.GetValueIfAscension((AscensionLevel)8, 13, 10),
                    ValueProp.Move);
                __result = true;
                return false;

            // 魂焰 / 魂斩：两招打完都给自己一层虚无。
            case ("SpectralKnight", "SOUL_FLAME") when settings.Knights:
            case ("SpectralKnight", "SOUL_SLASH") when settings.Knights:
                combat.Apply<IntangiblePower>(owner, 1, owner);
                __result = true;
                return false;

            // 墨斑：给玩家虚弱，再给自己 2 层滑溜和 2 力量。
            case ("Vantom", "INK_BLOT_MOVE") when settings.Vantom:
                combat.ApplyFromMonster<WeakPower>(
                    player,
                    AscensionHelper.GetValueIfAscension((AscensionLevel)9, 2, 1),
                    owner);
                if (simulator.HasPendingChoice)
                    return false;
                combat.Apply<SlipperyPower>(owner, 2, owner);
                combat.Apply<StrengthPower>(owner, 2, owner);
                __result = true;
                return false;

            // ---------- 只换了招式 id、实现还是原版的四条 ----------
            // 改版把原版的一招拆成两招（或只是改了名），实现仍然是怪物自己那个方法。
            // 求解器认 id 不认方法，所以原样照抄一份到新 id 上。

            // 犁击：原版叫 PLOW_MOVE，改版拆成第一、第二次，实现没动。
            case ("CeremonialBeast", "FIRST_PLOW_MOVE") when settings.CeremonialBeast:
            case ("CeremonialBeast", "SECOND_PLOW_MOVE") when settings.CeremonialBeast:
                combat.Apply<StrengthPower>(
                    owner, combat.GetMonsterStaticInt(owner, "PlowStrength"), owner);
                __result = true;
                return false;

            // 鼓胀：改版给自己 1 力量，原版是 2。
            // 这三个 case 必须写具体子类名：这里 switch 的是 `GetType().Name`，
            // 实际在场的是 DecimillipedeSegmentFront / Middle / Back，
            // 基类名 "DecimillipedeSegment" 一次都不会命中。
            case ("DecimillipedeSegmentFront", "BULK_MOVE") when settings.Decimillipede:
            case ("DecimillipedeSegmentMiddle", "BULK_MOVE") when settings.Decimillipede:
            case ("DecimillipedeSegmentBack", "BULK_MOVE") when settings.Decimillipede:
                combat.Apply<StrengthPower>(owner, 1, owner);
                __result = true;
                return false;

            // 重接：求解器**已经**完整模拟了这一招（Apply 开头的 ResolveReviveMove 里认这个 id），
            // 只是它没进 Supports 那张表，于是「治疗」这个意图被标成不支持。
            // 这里什么都不做，只是把它从「未知招式」变成「已接管」。
            case ("DecimillipedeSegmentFront", "REATTACH_MOVE") when settings.Decimillipede:
            case ("DecimillipedeSegmentMiddle", "REATTACH_MOVE") when settings.Decimillipede:
            case ("DecimillipedeSegmentBack", "REATTACH_MOVE") when settings.Decimillipede:
                __result = true;
                return false;

            default:
                return true;
        }
    }

    /// <summary>
    /// 信徒进场：跳舞的那只挂「假随从」并把生命减半，另一只乘 1.5。
    /// </summary>
    /// <remarks>
    /// 平衡尖塔用 prefix 整个换掉了 <c>KinFollower.AfterAddedToRoom</c>，原版在那里挂的「随从」
    /// 也随之消失，所以这里只补改版加的两件事，不要再补原版那层。
    /// 取整跟游戏一致：<c>Creature.SetMaxHpInternal</c> 是 <c>(int)</c> 截断，不是四舍五入，
    /// 所以基础 63 的那只是 94 而不是 95。
    /// </remarks>
    private static void KinFollowerEntrance(
        CombatPredictionSimulator simulator,
        SimulatedCombatState combat,
        Creature follower,
        bool startsWithDance)
    {
        if (startsWithDance)
            combat.Apply<MinionFakePower>(follower, 1, follower);

        SimCreatureState state = simulator.State.GetCreature(follower);
        int scaled = (int)(state.MaxHp * (startsWithDance ? 0.5m : 1.5m));
        state.SetMaxHp(scaled);
        state.CurrentHp = scaled;
    }

    /// <summary>组装师每造一台机器人自伤的量：最大生命的 1/15。</summary>
    private static int SpawnBotDamage(CombatPredictionSimulator simulator, Creature owner)
        => (int)((float)simulator.State.GetCreature(owner).MaxHp * (1f / 15f));

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
