using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Resources;
using CombatSolver.Engine.InCombat.Simulation;
using RebalancedSpire.Core.Powers;

namespace AutoRebalancedSpire;

/// <summary>
/// RebalancedSpire 新增的 Power 的钩子镜像。
/// </summary>
/// <remarks>
/// 不补这些的后果分两种：走注册表的钩子会记一条未镜像风险（红字，至少看得见），
/// 而求解器写死分发的那几个时点连风险都不记，直接静默算错。
/// </remarks>
internal static class PowerMirrors
{
    public static int RegisterAll()
    {
        AfterEnergyResetMirrors.Registry.Register<SpinnerPlusPower>(SpinnerPlus);
        return 1;
    }

    /// <summary>
    /// 纺纱+：每回合重置能量之后按层数充能玻璃球，然后把场上**所有**玻璃球各触发一次被动。
    /// </summary>
    /// <remarks>
    /// 和原版 <c>SpinnerPower</c> 的差别就是后面那一段 —— 原版只充能。触发的是
    /// <c>OrbCmd.Passive(..., countAffectedByHooks: false)</c>，对应求解器的
    /// <c>OrbPassive</c> 而不是 <c>TriggerOrbPassive</c>：后者会先过
    /// <c>ModifyOrbPassiveTriggerCount</c>，而原版这一句明确要求不过钩子。
    ///
    /// 遍历前先拷一份：被动会改队列。每一步都查 <c>HasPendingChoice</c>，起了选择就停下，
    /// 和求解器自己那几个 handler 的写法一致。
    /// </remarks>
    private static void SpinnerPlus(SpinnerPlusPower power, AfterEnergyResetMirrorContext context)
    {
        CombatPredictionSimulator simulator = context.Simulator;
        simulator.OrbChannel<GlassOrb>(context.Player, power.Amount);
        if (simulator.HasPendingChoice)
            return;

        OrbModel[] orbs = simulator.State.GetPlayerCombatState(context.Player)
            .OrbQueue.Orbs.OfType<GlassOrb>().Cast<OrbModel>().ToArray();
        foreach (OrbModel orb in orbs)
        {
            simulator.OrbPassive(orb);
            if (simulator.HasPendingChoice)
                return;
        }
    }
}
