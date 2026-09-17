using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CombatSolver;
using CombatSolver.Engine.InCombat.Simulation;
using RebalancedSpire.Core.Powers;

namespace AutoRebalancedSpire;

/// <summary>
/// 侧回合结束时，改版新 Power 自己那一份 <c>AfterSideTurnEnd</c>。
/// </summary>
/// <remarks>
/// 求解器这个时点是 <c>EndTurnPowerSupport.TriggerRegular</c> 里一张按类型写死的大 switch，
/// 认不出的 Power **一声不吭地跳过，也不记风险**，所以缺这几条就是静默算错：
/// 饥饿和细看会永远不衰减，死神形态+ 的提前收割整份不存在。
///
/// 钻石头冠那条也挂在同一个方法上，但写在 <see cref="RelicStatefulMirrors"/> 里跟着遗物走，
/// 这里不重复。守护（<c>GuardPower</c>）的那条**故意不做**：它的条件是
/// 「side 不是敌方 且 参与者里有自己」，而自己是怪物，玩家侧结束时参与者只有玩家，
/// 敌方侧结束时第一个条件就不成立 —— 实机里这条钩子一次都不会触发，镜像成「什么都不做」才是对的。
/// </remarks>
internal static class SideTurnEndDispatch
{
    private const string TargetName = nameof(EndTurnPowerSupport.TriggerRegular);

    public static MethodInfo ResolveTarget()
        => AccessTools.Method(typeof(EndTurnPowerSupport), TargetName)
           ?? throw new MissingMethodException(nameof(EndTurnPowerSupport), TargetName);

    public static void Postfix(
        CombatPredictionSimulator simulator,
        SimulatedCombatState combat,
        CombatSide side,
        IEnumerable<Creature> participants,
        ref bool __result)
    {
        if (!__result)
            return;

        HashSet<Creature> participantSet = participants.ToHashSet();
        foreach (PowerModel power in combat.EffectivePowers().ToArray())
        {
            if (power.Amount <= 0 || !participantSet.Contains(power.Owner))
                continue;

            switch (power)
            {
                // 饥饿：玩家回合结束减 1 层。
                case HungerPower when side == CombatSide.Player:
                    combat.SetPowerAmount(power, power.Amount - 1);
                    break;

                // 细看：玩家回合结束减 2 层。手牌上限跟着回来，见 MaxHandSizePatch。
                case ScrutinyPower when side == CombatSide.Player:
                    combat.SetPowerAmount(power, Math.Max(0, power.Amount - 2));
                    break;

                // 死神形态+ 比原版多一次收割：原版的灾厄只在敌人回合结束时结算，改版在持有者
                // 自己的回合结束时先杀一遍 —— 被收掉的敌人连这一轮的招式都出不了。
                case ReaperFormPlusPower:
                {
                    Creature[] doomed = combat.KnownEnemies
                        .Where(enemy => simulator.State.GetCreature(enemy).IsAlive
                            && combat.GetAmount<DoomPower>(enemy)
                                >= simulator.State.GetCreature(enemy).CurrentHp)
                        .ToArray();
                    if (doomed.Length > 0)
                        combat.DoomKill(simulator, doomed);
                    break;
                }

                default:
                    continue;
            }

            if (simulator.HasPendingChoice)
            {
                __result = false;
                return;
            }
        }
    }
}
