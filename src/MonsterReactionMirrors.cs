using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Damage;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Death;
using MegaCrit.Sts2.Core.ValueProps;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Helpers;
using RebalancedSpire.Core.Powers;

namespace AutoRebalancedSpire;

/// <summary>
/// 几个改版新 Power 的「被打之后临时换招」。
/// </summary>
/// <remarks>
/// 这几条都走注册表，不登记的话会记一条未镜像风险（红字），不至于静默算错；
/// 但那意味着这三场战斗每次都提示不可信。效果本身都不复杂，补掉更划算。
///
/// 其中组装师那两条原本判成「做不了」，理由是它的条件里有 <c>IntendsToAttack</c>，读的是实机
/// 模型当前的意图。这个判断是错的：<c>IntendsToAttack</c> 的定义就是「下一招的意图里有攻击或
/// 致命一击」，而分支里那一招求解器自己也有（<c>CurrentMonsterMove</c>），照同一个定义算即可，
/// 不需要去读实机。<c>CanFabricate</c>（同侧活着的不到 4 个）同理。
/// </remarks>
internal static class MonsterReactionMirrors
{
    public static int RegisterAll()
    {
        AfterDamageReceivedMirrors.Registry.Register<PlowPlusPower>(PlowPlus);
        AfterCurrentHpChangedMirrors.Registry.Register<FabricatorPower>(FabricatorHpChanged);
        AfterDeathMirrors.Registry.Register<FabricatorPower>(FabricatorDeath);
        AfterDeathMirrors.Registry.Register<MinionFakePower>(MinionFakeDeath);
        ModifyDamageMirrors.MultiplicativeRegistry.Register<FabricatorPower>(FabricatorIncomingDamage);
        return 5;
    }

    /// <summary>耕耘+：祭祀之兽被打到剩血低于阈值时清空力量、眩晕一回合并换招。</summary>
    /// <remarks>
    /// 和原版的犁击（<c>PlowPower</c>）差三处：只清 <c>StrengthPower</c>（原版连临时力量一起清）、
    /// 换的那一招看自己身上有没有「已耕耘」（有就是野兽咆哮，没有就是第二踏），
    /// 以及事后给自己挂一层「已耕耘」，所以第二次触发走的是另一条分支。
    /// </remarks>
    private static void PlowPlus(PlowPlusPower power, AfterDamageReceivedMirrorContext context)
    {
        if (context.Target != power.Owner
            || context.Result.UnblockedDamage <= 0
            || context.State.GetCreature(context.Target).CurrentHp > power.Amount)
        {
            return;
        }
        if (power.Owner.Monster is not CeremonialBeast)
            return;
        if (context.CombatState is not SimulatedCombatState combat)
            return;
        if (context.CombatState is not ICombatPredictionEffectSink effects)
            return;

        foreach (StrengthPower strength in combat.EffectivePowers()
                     .OfType<StrengthPower>()
                     .Where(candidate => ReferenceEquals(candidate.Owner, power.Owner))
                     .ToArray())
        {
            effects.SetPowerAmount(strength, 0);
        }
        // 换招的判据读的是**这一层挂上之前**的状态，所以先读后挂。
        string nextMove = combat.GetAmount<PlowedPower>(power.Owner) > 0
            ? "BEAST_CRY_MOVE"
            : "SECOND_STAMP_MOVE";
        effects.SetPowerAmount(power, 0);
        effects.ForceStunnedMove(power.Owner, nextMove);
        combat.Apply<PlowedPower>(power.Owner, 1, power.Owner);
    }

    /// <summary>组装师：血量不够再造一台机器人时立刻改成逃跑。</summary>
    /// <remarks>
    /// 判据和它自伤那份是同一个：剩余生命不超过两台机器人的代价（各 1/15 最大生命）就跑。
    /// 不镜像的话求解器会一直按「它还会站在那里挨打」算，把一条其实打不完的路线当成能打完。
    /// </remarks>
    private static void FabricatorHpChanged(
        FabricatorPower power,
        AfterCurrentHpChangedMirrorContext context)
    {
        if (context.Creature != power.Owner || power.Owner.Monster is not Fabricator)
            return;
        if (context.CombatState is not SimulatedCombatState combat)
            return;

        SimCreatureState state = context.State.GetCreature(power.Owner);
        if (state.CurrentHp > 2 * SpawnBotCost(state))
            return;
        combat.ForceMonsterMove(power.Owner, "ESCAPE_MOVE");
    }

    /// <summary>组装师：自己造的机器人被打死时，立刻改成再造一台。</summary>
    /// <remarks>
    /// 三个条件照实机抄：死的那只带「随从」、组装师自己血还够再造（同它的自伤判据）、
    /// 同侧活着的不到 4 个、并且组装师这一招本来是要攻击的（只有要攻击的那一招才值得被打断）。
    ///
    /// <c>IntendsToAttack</c> 在实机里就是「下一招的意图里有攻击或致命一击」，
    /// 分支里那一招求解器自己有，照同一个定义算，不去读实机模型。
    ///
    /// 不镜像的后果不是少算一点伤害，而是**清小怪的收益被算反**：求解器会以为打死机器人
    /// 是纯赚，看不到它下一回合直接补一台、而且原本那一刀也不挨了。
    /// </remarks>
    private static void FabricatorDeath(FabricatorPower power, AfterDeathMirrorContext context)
    {
        if (context.WasRemovalPrevented || ReferenceEquals(context.Creature, power.Owner))
            return;
        if (power.Owner.Monster is not Fabricator)
            return;
        if (context.CombatState is not SimulatedCombatState combat)
            return;
        if (combat.GetAmount<MinionPower>(context.Creature) <= 0)
            return;

        SimCreatureState state = context.State.GetCreature(power.Owner);
        if (state.CurrentHp <= 2 * SpawnBotCost(state))
            return;

        int aliveOnSide = combat.GetTeammatesOf(power.Owner)
            .Count(candidate => context.State.GetCreature(candidate).IsAlive);
        if (aliveOnSide >= 4)
            return;
        if (!IntendsToAttack(combat, power.Owner))
            return;

        combat.ForceMonsterMove(power.Owner, "FABRICATE_MOVE");
    }

    /// <summary>这只怪当前排的那一招里有没有攻击意图。和实机 <c>IntendsToAttack</c> 同一个定义。</summary>
    private static bool IntendsToAttack(SimulatedCombatState combat, Creature creature)
        => combat.CurrentMonsterMove(creature).Move.Intents
            .Any(static intent => intent.IntentType is IntentType.Attack or IntentType.DeathBlow);

    /// <summary>组装师：场上还有机器人时，它受到的强化攻击伤害减半。</summary>
    /// <remarks>
    /// 这条**看上去**不用镜像：取值类的钩子（<c>Modify*</c>）在监听者过滤被关掉之后会回落到
    /// Power 自己的实现，自动跟随改版 —— 前提是那份实现只读求解器喂给它的东西。
    /// 这一条不是：它读的是 <c>CombatState.Enemies</c> 里每只怪**实机当下**的死活和身上的随从层数。
    ///
    /// 于是求解器在第一回合做计划时问它「第二回合这一刀打多少」，它照第一回合的实机局面回答
    /// 「场上没有机器人，不减半」，而第二回合实机已经有两台了 —— 计划里那一刀算成双倍，
    /// 打完对不上，整局重算。玩家看到的就是「打组装师会重算」。
    ///
    /// 所以判据必须照着**模拟局面**重算一遍。这是第三条「能不能自动跟随」的判据：
    /// 取值钩子只有在它读的状态是求解器传进来的那部分时才自动跟随。
    /// </remarks>
    private static decimal FabricatorIncomingDamage(
        FabricatorPower power,
        ModifyDamageMirrorContext context)
    {
        if (context.Target != power.Owner
            || power.Owner.Monster is not Fabricator
            || !context.Props.IsPoweredAttack())
        {
            return 1m;
        }
        if (context.CombatState is not SimulatedCombatState combat)
            return 1m;

        bool anyMinionAlive = combat.Enemies.Any(candidate =>
            context.State.GetCreature(candidate).IsAlive
            && combat.GetAmount<MinionPower>(candidate) > 0);
        return anyMinionAlive ? 0.5m : 1m;
    }

    /// <summary>组装师每造一台机器人自伤的量：最大生命的 1/15。</summary>
    private static int SpawnBotCost(SimCreatureState state)
        => (int)((float)state.MaxHp * (1f / 15f));

    /// <summary>
    /// 求解器把同族神官的 <c>AfterDeath</c> 登记成了「忽略」，改版把整段换掉了。
    /// </summary>
    /// <remarks>
    /// 原版那一段只有音乐和一句台词（<c>AllFollowerDeathResponse</c> 就是一句台词），
    /// 所以上游 <c>RegisterIgnored&lt;KinPriest&gt;()</c> 是对的。改版在同一个钩子里加了两件
    /// 有战斗后果的事，摘掉那条忽略登记换成下面这份。
    /// </remarks>
    public static IEnumerable<MirroredHookReplacement> Replacements()
    {
        yield return new MirroredHookReplacement(
            typeof(KinPriest),
            settings => settings.TheKin,
            () => RegistryOverride.DropRegistration(AfterDeathMirrors.Registry, typeof(KinPriest)),
            () => AfterDeathMirrors.Registry.Register<KinPriest>(KinPriestDeath));
    }

    /// <summary>神官自己身上那份「谁死了」的反应，用来跟住它有没有说过那句台词。</summary>
    /// <remarks>
    /// 实机的判据是神官的私有字段 <c>SpeechUsed</c>。求解器只捕获它自己用得上的那几个怪物
    /// 字段（<c>DescribePredictedMonsterState</c> 里一张写死的表），同族神官整条是 "-"，
    /// 直接 <c>GetMonsterBool</c> 会撞上「建根时没捕获」的断言。
    ///
    /// 所以这里只往分支状态里写 1、从不写 0：没写过就去读实机模型，那正是建根时的真值；
    /// 写过就说明这条分支里已经死过信徒了。两边取或，分支之间也不会互相串。
    /// </remarks>
    private const string SpeechUsedKey = "rs_kin_priest_speech_used";

    /// <summary>同族神官：每死一只信徒给自己一份力量，从第二只起立刻改走仪式。</summary>
    /// <remarks>
    /// 改版那段的顺序是「先给力量，再看台词说过没有：说过就换招，然后把台词标记打上」，
    /// 所以第一只信徒死的时候不换招，第二只开始才换。仪式的下一招是光束，那条链求解器
    /// 自己走得通（<c>RITUAL_MOVE</c> 用的还是原版实现，上游已有镜像）。
    ///
    /// 神官自己死的那一支：没跳过舞的信徒立刻改走复仇之舞。
    ///
    /// 实机的 <c>SetMoveImmediate</c> 还要求当前招式 <c>CanTransitionAway</c>，
    /// 求解器整个没有这个概念（自己的女王联动、组装师逃跑也都不判），这里跟着不判。
    /// 神官这几招都是单回合招式，不会卡住。
    /// </remarks>
    private static void KinPriestDeath(KinPriest priest, AfterDeathMirrorContext context)
    {
        if (context.WasRemovalPrevented)
            return;
        if (context.CombatState is not SimulatedCombatState combat)
            return;

        Creature self = priest.Creature;
        if (ReferenceEquals(context.Creature, self))
        {
            foreach (Creature follower in combat.Enemies.ToArray())
            {
                if (follower.Monster is not KinFollower { StartsWithDance: false })
                    continue;
                if (!context.State.GetCreature(follower).IsAlive)
                    continue;
                combat.ForceMonsterMove(follower, "REVENGE_DANCE_MOVE");
            }
            return;
        }

        if (context.Creature.Monster is not KinFollower)
            return;
        if (!context.State.GetCreature(self).IsAlive)
            return;

        combat.Apply<StrengthPower>(
            self,
            AscensionHelper.GetValueIfAscension((AscensionLevel)9, 3, 2),
            self);
        if (SpeechUsed(combat, priest))
            combat.ForceMonsterMove(self, "RITUAL_MOVE");
        combat.SetMonsterInt(self, SpeechUsedKey, 1);
    }

    private static bool SpeechUsed(SimulatedCombatState combat, KinPriest priest)
        => combat.GetCustomMonsterInt(priest.Creature, SpeechUsedKey) != 0
           || priest.SpeechUsed;

    /// <summary>假随从：同伴全死光之后自己也跑。</summary>
    private static void MinionFakeDeath(MinionFakePower power, AfterDeathMirrorContext context)
    {
        if (context.WasRemovalPrevented || ReferenceEquals(context.Creature, power.Owner))
            return;
        if (power.Owner.Monster is not KinFollower)
            return;
        if (context.CombatState is not SimulatedCombatState combat)
            return;

        bool anyoneElseAlive = combat.Enemies
            .Where(candidate => !ReferenceEquals(candidate, power.Owner))
            .Any(candidate => context.State.GetCreature(candidate).IsAlive);
        if (anyoneElseAlive)
            return;
        combat.ForceMonsterMove(power.Owner, "ESCAPE_MOVE");
    }
}
