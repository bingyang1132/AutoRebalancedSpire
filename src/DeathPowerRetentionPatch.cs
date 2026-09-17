using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using CombatSolver;
using RebalancedSpire.Core.Powers;

namespace AutoRebalancedSpire;

/// <summary>
/// 死亡清理 Power 时，改版那几个「主人死了也不走」的 Power 要留下。
/// </summary>
/// <remarks>
/// 原版的判据是两个条件**或**起来（<c>Creature.RemoveAllPowersInternalExcept</c>）：
///
/// <code>
/// keep = !p.ShouldPowerBeRemovedAfterOwnerDeath() || !Hook.ShouldPowerBeRemovedOnDeath(p)
/// </code>
///
/// 求解器的 <c>SimulatedCombatState.RemovePowersAfterDeath</c> 先按前一条算出 <c>keep</c>，
/// 一旦死者身上有幻象就把 <c>keep</c> **整个覆盖**成后一条，而不是或上去。原版所有的 Power
/// 里没有一个同时是减益又声明「主人死了也不走」，所以这条差异在原版永远看不见。
///
/// 改版的「幻灭」正好两条都占：它是减益，又重写了 <c>ShouldPowerBeRemovedAfterOwnerDeath</c>
/// 返回 false。寄生惧魔身上恰好有幻象，于是求解器每次都把幻灭清成 0，复活时
/// <c>MonsterMirrors.RevivePostfix</c> 读到 0 层、那 4 点负力量一次都不扣 —— 胧光怪那一场
/// 每次复活都会因为这一处对不上而重算。
///
/// 这里按原版口径把「声明了主人死后不走」的那一支补回来。范围收在改版自己的 Power 上：
/// 原版的由求解器负责，我们不去动它的语义。
/// </remarks>
internal static class DeathPowerRetentionPatch
{
    private const string TargetName = nameof(SimulatedCombatState.RemovePowersAfterDeath);

    private static readonly Assembly RebalancedSpireAssembly = typeof(DisillusionPower).Assembly;

    public static MethodInfo ResolveTarget()
        => AccessTools.Method(typeof(SimulatedCombatState), TargetName)
           ?? throw new MissingMethodException(nameof(SimulatedCombatState), TargetName);

    public static void Prefix(
        SimulatedCombatState __instance,
        Creature creature,
        out List<(PowerModel Power, int Amount)>? __state)
    {
        __state = null;
        foreach (PowerModel power in __instance.EffectivePowers())
        {
            if (power.Amount == 0 || !ReferenceEquals(power.Owner, creature))
                continue;
            if (power.GetType().Assembly != RebalancedSpireAssembly)
                continue;
            if (power.ShouldPowerBeRemovedAfterOwnerDeath())
                continue;
            (__state ??= []).Add((power, power.Amount));
        }
    }

    public static void Postfix(
        SimulatedCombatState __instance,
        List<(PowerModel Power, int Amount)>? __state)
    {
        if (__state == null)
            return;
        foreach ((PowerModel power, int amount) in __state)
            __instance.SetPowerAmount(power, amount);
    }
}
