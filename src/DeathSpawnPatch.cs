using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using CombatSolver;
using CombatSolver.Engine.InCombat.Simulation;
using RebalancedSpire.Core.Powers;

namespace AutoRebalancedSpire;

/// <summary>
/// 异蛙寄生虫精英死后生出来的那批蠕虫。
/// </summary>
/// <remarks>
/// 原版 <c>InfestedPower</c> 死时固定生 4 只；改版换成 <c>InfestedPlusPower</c>，生的数量是
/// <c>min(4, 层数)</c>，而层数由「增殖」一次次叠出来。求解器这两处都是按类型写死的：
/// <c>DeathPowerSupport.Trigger</c> 的 switch 认不出新 Power，<c>SpawnsPrimaryEnemyOnDeath</c>
/// 那张表也没有它。
///
/// 后果不是算错一点数值，而是**整场战斗的终局判断**：求解器会以为打死这只就赢了，
/// 把一条其实还要再打一轮的路线当成致胜路线。
/// </remarks>
internal static class DeathSpawnPatch
{
    public static MethodInfo ResolveTriggerTarget()
        => AccessTools.Method(typeof(DeathPowerSupport), nameof(DeathPowerSupport.Trigger))
           ?? throw new MissingMethodException(
               nameof(DeathPowerSupport), nameof(DeathPowerSupport.Trigger));

    public static MethodInfo ResolveSpawnsPrimaryTarget()
        => AccessTools.Method(
               typeof(DeathPowerSupport),
               nameof(DeathPowerSupport.SpawnsPrimaryEnemyOnDeath))
           ?? throw new MissingMethodException(
               nameof(DeathPowerSupport), nameof(DeathPowerSupport.SpawnsPrimaryEnemyOnDeath));

    public static void SpawnsPrimaryPostfix(PowerModel power, ref bool __result)
    {
        if (!__result && power is InfestedPlusPower { Amount: > 0 })
            __result = true;
    }

    public static void TriggerPostfix(
        CombatPredictionSimulator simulator,
        SimulatedCombatState combat,
        Creature dead,
        ref bool __result)
    {
        if (!__result)
            return;
        if (combat.GetPower<InfestedPlusPower>(dead) is not { Amount: > 0 } infested)
            return;

        int count = Math.Min(4, infested.Amount);
        for (int index = 0; index < count; index++)
        {
            MonsterSpawnSupport.Spawn<Wriggler>(
                simulator,
                combat,
                dead,
                $"wriggler{index + 1}",
                configure: wriggler => wriggler.StartStunned = true);
            if (simulator.HasPendingChoice)
            {
                __result = false;
                return;
            }
        }
    }
}
