using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using System.Reflection;
using HarmonyLib;
using CombatSolver;
using CombatSolver.Engine.InCombat.Simulation;
using RebalancedSpire.Core.Configs;

namespace AutoRebalancedSpire;

/// <summary>
/// 改版换掉了怪物的行动状态机，求解器那几条写死的条件分支会指向**已经不存在的招式**。
/// </summary>
/// <remarks>
/// 求解器在搜索里推进怪物行动时，遇到条件分支（<c>ConditionalBranchState</c>）不会去调用实机
/// 的那个 <c>Func&lt;bool&gt;</c>（那要读实机模型，搜索里的局面是假的），而是按
/// <c>(怪物类名, 分支 id)</c> 在 <c>BranchMonsterAi.ResolveConditional</c> 里写死了一张表，
/// 自己照原版的条件重算一遍，返回**招式 id 字符串**，调用方再拿去 <c>machine.States[id]</c>。
///
/// 改版整份替换了 <c>GenerateMoveStateMachine</c> 的怪物有 45 个，其中两个正好也在那张写死的
/// 表里，而且新机器里没有表中那个 id：
///
/// <list type="bullet">
///   <item>
///     **组装师**（<c>fabricateBranch</c>）：原版分支通向随机节点 <c>RAND</c>（在组装和组装打击
///     之间随机）；改版把组装打击整个删了，分支直接通向组装或崩解，机器里没有 <c>RAND</c>。
///   </item>
///   <item>
///     **活体护盾**（<c>SHIELD_SLAM_BRANCH</c>）：原版有同伴时用 <c>SHIELD_SLAM_MOVE</c>；
///     改版换成了 <c>SHIELD_UP_MOVE</c>（单纯加格挡），机器里没有 <c>SHIELD_SLAM_MOVE</c>。
///   </item>
/// </list>
///
/// 后果是 <c>machine.States[id]</c> 抛 <c>KeyNotFoundException</c>，这一支被
/// <c>SearchTransitionGuard</c> 兜住变成搜索失败 —— 整场战斗算不出来，和字符串字段那个坑
/// （见 <see cref="StringFieldPolicyPatch"/>）同一种后果，同一种成因：**一张写死的表，
/// 认不出来就炸**。这是第六个没有第三方入口的地方。
///
/// 另外三个也被改版换了机器、但恰好没中招的，记在下面免得下次重查：
/// <c>TestSubject.REVIVE_BRANCH</c> 两个目标 id 和条件都没变；
/// <c>LivingShield</c> 之外的几条（觉醒者、蛋机、女王等）改版根本没动；
/// <c>KnowledgeDemon</c> 的分支求解器是在 <c>Advance</c> 里提前截掉的，截的那两个 id 也都还在。
/// </remarks>
internal static class BranchConditionalPatch
{
    private const string TargetName = "ResolveConditional";

    public static MethodInfo ResolveTarget()
        => AccessTools.Method(typeof(BranchMonsterAi), TargetName)
           ?? throw new MissingMethodException(nameof(BranchMonsterAi), TargetName);

    public static bool Prefix(
        ConditionalBranchState branch,
        BranchMonsterAiState source,
        SimulatedCombatState combat,
        CombatPredictionSimulator simulator,
        ref string __result)
    {
        RebalancedSpireSettings settings = AdapterSettings.Current;
        Creature owner = source.Monster.Creature;

        if (settings.Fabricator
            && source.Monster is Fabricator
            && branch.Id == "fabricateBranch")
        {
            __result = CanFabricate(combat, simulator, owner)
                ? "FABRICATE_MOVE"
                : "DISINTEGRATE_MOVE";
            return false;
        }

        if (settings.TurretOperator
            && source.Monster is LivingShield
            && branch.Id == "SHIELD_SLAM_BRANCH")
        {
            bool hasAliveAlly = combat.GetTeammatesOf(owner)
                .Any(candidate => candidate != owner
                    && simulator.State.GetCreature(candidate).IsAlive);
            __result = hasAliveAlly ? "SHIELD_UP_MOVE" : "SMASH_MOVE";
            return false;
        }

        return true;
    }

    /// <summary>
    /// 改版组装师还造不造机器人。和原版的「同侧活着的不到 4 个」是两个条件。
    /// </summary>
    /// <remarks>
    /// 改版的判据是「场上带随从的还活着的不超过 2 只」**并且**「自己血够付两台的代价」——
    /// 每造一台自伤最大生命的 1/15，不够就不造。原版那条只数同侧活着的数量，两者会给出
    /// 不同的答案：比如场上只有组装师自己加一台机器人、但它已经被打到剩一点血，
    /// 原版口径说「继续造」，改版口径说「改成崩解」。
    ///
    /// 血不够时改版会直接换成逃跑，那条走的是 <c>AfterCurrentHpChanged</c>，
    /// 在 <see cref="MonsterReactionMirrors"/> 里；这里只管分支本身。
    /// </remarks>
    private static bool CanFabricate(
        SimulatedCombatState combat,
        CombatPredictionSimulator simulator,
        Creature owner)
    {
        int minionsAlive = combat.Enemies
            .Count(candidate => simulator.State.GetCreature(candidate).IsAlive
                && combat.GetAmount<MinionPower>(candidate) > 0);
        if (minionsAlive > 2)
            return false;

        SimCreatureState state = simulator.State.GetCreature(owner);
        return state.CurrentHp > 2 * (int)((float)state.MaxHp * (1f / 15f));
    }
}
