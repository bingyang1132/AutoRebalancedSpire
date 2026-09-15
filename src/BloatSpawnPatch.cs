using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Monsters;
using CombatSolver;
using CombatSolver.Engine.Common;
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
///   <item><c>BloatAmount</c> 每用一次 +1，上限 5。求解器读的是建根时冻结的静态值
///     （<c>combat.GetMonsterStaticInt</c>），一场战斗内不变，多回合的计划会越推越少算。</item>
/// </list>
///
/// <para><b>递增怎么模拟的。</b>计数挂在 <c>simulator.StateStore</c> 上、按怪物模型索引。
/// 那个存储的条目实现 <c>IPredictionStateForkable</c>，**搜索分叉时跟着分支各复制一份**，
/// 所以「这条时间线上已经膨胀过几次」在不同分支里互不干扰——这正是需要的语义。
/// 求解器给第三方的模型状态登记点（<c>ModelPredictionStateMirrors</c>）只对遗物和修饰器开放，
/// 怪物模型根本不会被 <c>CaptureRootState</c> 捕获，所以走不了那条路，直接用存储。</para>
///
/// <para>第 k 次膨胀生的气弹数是 <c>min(建根时的 BloatAmount + (k-1), 5)</c>，
/// 和改版那句 <c>BloatAmount = Math.Min(BloatAmount + 1, 5)</c> 对得上。</para>
/// </remarks>
internal static class BloatSpawnPatch
{
    private const string TargetName = nameof(MonsterMoveEffects.ApplyBeforeAttack);

    /// <summary>改版里的上限，见 <c>LivingFogPatch.MaxGasBombs</c>。</summary>
    private const int MaxGasBombs = 5;

    /// <summary>这条时间线上「膨胀」已经用过几次。分叉时跟着分支各复制一份。</summary>
    private sealed class BloatState : IPredictionStateForkable
    {
        public int Uses { get; set; }

        public object Fork(PredictionForkContext context)
        {
            _ = context;
            return MemberwiseClone();
        }
    }

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

        BloatState state = simulator.StateStore.Get<BloatState>(move.Owner.Monster!);
        int count = Math.Min(combat.GetMonsterStaticInt(move.Owner, "BloatAmount") + state.Uses, MaxGasBombs);
        state.Uses++;
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
