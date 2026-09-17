using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using CombatSolver;
using RebalancedSpire.Core.Powers;

namespace AutoRebalancedSpire;

/// <summary>
/// 侧回合开始时，改版新 Power 自己那一份 <c>AfterSideTurnStart</c>。
/// </summary>
/// <remarks>
/// 求解器这个时点是 <c>TurnStartPowerSupport.TriggerAfterSideTurnStart</c>，里面只有倒计时和
/// 流沙坑两条写死的处理，没有第三方入口，认不出的 Power 一声不吭地跳过。
///
/// 现在只有「遥远距离」一条：它在自己人的侧回合开始时走
/// <c>PowerCmd.TickDownDuration</c> 掉一层。不镜像的话求解器会一直按施加时的层数算伤害倍率，
/// 而层数每回合都在降 —— 无餍之物那一场每回合都会因为这一处对不上而重算。
/// </remarks>
internal static class SideTurnStartDispatch
{
    private const string TargetName = nameof(TurnStartPowerSupport.TriggerAfterSideTurnStart);

    public static MethodInfo ResolveTarget()
        => AccessTools.Method(typeof(TurnStartPowerSupport), TargetName)
           ?? throw new MissingMethodException(nameof(TurnStartPowerSupport), TargetName);

    public static void Postfix(
        SimulatedCombatState combat,
        IReadOnlyList<Creature> participants,
        ref bool __result)
    {
        if (!__result)
            return;

        foreach (LongDistancePower power in combat.EffectivePowers()
                     .OfType<LongDistancePower>()
                     .ToArray())
        {
            if (power.Amount <= 0 || !participants.Contains(power.Owner))
                continue;
            // 走求解器自己那份，跳过标记的语义才和 TickDownDuration 一致。
            combat.TickDuration<LongDistancePower>(power.Owner);
        }
    }
}
