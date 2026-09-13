using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Damage;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Death;
using CombatSolver.Engine.InCombat.Simulation;
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
        return 4;
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

    /// <summary>组装师每造一台机器人自伤的量：最大生命的 1/15。</summary>
    private static int SpawnBotCost(SimCreatureState state)
        => (int)((float)state.MaxHp * (1f / 15f));

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
