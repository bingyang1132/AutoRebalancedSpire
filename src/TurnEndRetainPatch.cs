using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using CombatSolver;
using CombatSolver.Engine.InCombat.Simulation;
using RebalancedSpire.Core.Powers;

namespace AutoRebalancedSpire;

/// <summary>
/// 周密计划+：回合结束清手牌之前，挑最多 N 张给一次性保留。
/// </summary>
/// <remarks>
/// 原版的周密计划给的是 <c>WellLaidPlansPower</c>，它只重写一个 <c>ShouldFlush</c> —— 取值钩子，
/// 求解器走 <c>PersistentRelicSupport.ShouldFlush</c> 现算，整只手牌都留下。改版换成了
/// <c>WellLaidPlansPlusPower</c>：不再重写 <c>ShouldFlush</c>，而是在 <c>BeforeFlushLate</c> 让玩家
/// 挑最多 Amount 张（1，升级 2）给一次性保留。
///
/// 求解器完全没有 <c>BeforeFlush</c> / <c>BeforeFlushLate</c> 这两个时点 —— 它的注释写得很直白：
/// 原版唯一的监听者当前版本用不到，所以整段省掉了。于是不补的话，求解器会认为打出周密计划
/// 什么都没发生，把一张牌当空气。
///
/// 挂在 <c>PlayerTurnEndLifecycle.RunPhaseOne</c> 的后面，而不是清手牌那一步里：
/// <c>FlushPlayerHandAtTurnEnd</c> 返回 void，调用方紧接着就进第二阶段，在那里挂起一次选择
/// 没人接得住；<c>RunPhaseOne</c> 的返回值本来就是「有没有待处理选择」，调用方会把它变成搜索边界。
/// 两者之间只隔着提交回合历史和敌人死亡结算，都不碰保留标记。
///
/// 选牌走求解器那条通用通道（见 <see cref="TurnChoiceMirrors"/>），所以分支会进 beam、
/// 会写进计划、部署时会去应答原生选牌页。游标存在 <c>SimulatedCombatState</c> 上，
/// 回合结束这一整段由 <c>AdvanceRound</c> 开的那一个游标覆盖。
/// </remarks>
internal static class TurnEndRetainPatch
{
    private const string TargetName = nameof(PlayerTurnEndLifecycle.RunPhaseOne);

    public static MethodInfo ResolveTarget()
        => AccessTools.Method(typeof(PlayerTurnEndLifecycle), TargetName)
           ?? throw new MissingMethodException(nameof(PlayerTurnEndLifecycle), TargetName);

    public static void Postfix(
        CombatPredictionSimulator simulator,
        SimulatedCombatState combat,
        Player player,
        ref bool __result)
    {
        if (!__result || combat.HasPendingChoice || simulator.IsOverOrEnding)
            return;
        // 本来就不清手牌的回合（例如还留着原版那张周密计划），这个钩子在实机里也直接返回。
        if (!PersistentRelicSupport.ShouldFlush(combat, player))
            return;

        foreach (PowerModel power in combat.EffectivePowers().ToArray())
        {
            if (power is not WellLaidPlansPlusPower || power.Amount <= 0)
                continue;
            if (!ReferenceEquals(power.Owner.Player, player))
                continue;

            if (!TurnChoiceMirrors.ResolveSingleTurnRetain(
                    simulator,
                    combat,
                    player,
                    combat._activeActionChoices,
                    power.Id.Entry,
                    power.Amount))
            {
                __result = false;
                return;
            }
            if (combat.HasPendingChoice)
            {
                __result = false;
                return;
            }
        }
    }
}
