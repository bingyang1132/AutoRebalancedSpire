using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Monsters;
using CombatSolver;
using CombatSolver.Engine.InCombat.Simulation;
using RebalancedSpire.Core.Configs;
using RebalancedSpire.Core.Powers;

namespace AutoRebalancedSpire;

/// <summary>
/// 迷雾的「膨胀」生出来的气弹，改版会各挂一层「乒乓」。
/// </summary>
/// <remarks>
/// 这一招的召唤那半截不在 <c>MonsterMoveEffects.Apply</c> 里，而在
/// <c>MonsterMoveEffects.ApplyBeforeAttack</c>（要先把气弹放上场，攻击才有目标），
/// 所以 <see cref="MonsterMirrors"/> 那个前缀够不着，得单独补一处。
///
/// <para>改版方法体（<c>LivingFogPatch.BloatMove</c>）和原版比有两处不同：</para>
/// <list type="number">
///   <item>每只生出来的气弹上 1 层 <c>PingPongPower</c>。少算这一层，求解器会以为打气弹不要钱，
///     实际打一下自己要挨一下。</item>
///   <item><c>BloatAmount</c> 每用一次 +1，上限 5。<b>这一条这里没有补</b>，见下。</item>
/// </list>
///
/// <para><b>为什么第二条没补。</b>求解器读的是建根时冻结的静态值
/// （<c>combat.GetMonsterStaticInt(owner, "BloatAmount")</c>），一场战斗内不变。要模拟递增
/// 得有一份**跟着搜索分支走**的每怪计数——同一条时间线上第三次膨胀比第一次多生两只，而不同
/// 分支的次数还不一样。求解器给第三方的状态登记点是按模型挂的（见
/// <c>WhisperingEarringPatch.RegisterState</c>），拿来挂怪物的招式计数要先确认它在分支复制时
/// 的语义，没确认之前不写。结果是多回合计划里气弹会越推越少算，方向是低估敌人，
/// 记在 docs/coverage-gaps.md 里。</para>
/// </remarks>
internal static class BloatSpawnPatch
{
    private const string TargetName = nameof(MonsterMoveEffects.ApplyBeforeAttack);

    public static MethodInfo ResolveTarget()
        => AccessTools.Method(typeof(MonsterMoveEffects), TargetName)
           ?? throw new MissingMethodException(nameof(MonsterMoveEffects), TargetName);

    public static bool Prefix(
        CombatPredictionSimulator simulator,
        SimulatedCombatState combat,
        ForecastMove move,
        Creature player)
    {
        _ = player;
        if (move.Owner.Monster is not LivingFog || move.Move.Id != "BLOAT_MOVE")
            return true;
        if (!AdapterSettings.Current.LivingFog)
            return true;

        int count = combat.GetMonsterStaticInt(move.Owner, "BloatAmount");
        for (int index = 0; index < count; index++)
        {
            string? slot = MonsterSpawnSupport.NextSlot(combat);
            if (string.IsNullOrEmpty(slot))
                break;
            Creature bomb = MonsterSpawnSupport.Spawn<GasBomb>(simulator, combat, move.Owner, slot);
            combat.Apply<PingPongPower>(bomb, 1, move.Owner);
        }
        // 这个前缀只接管迷雾这一支。同方法里另一支是偷东西的跳虫，和这里互斥，
        // 所以直接跳过原实现不会漏掉它。
        return false;
    }
}
