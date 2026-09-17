using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using CombatSolver;
using RebalancedSpire.Core.Configs;
using RebalancedSpire.Core.Powers;

namespace AutoRebalancedSpire;

/// <summary>
/// 镀甲（<c>PlatingPower</c>）每回合衰减的规则改了。
/// </summary>
/// <remarks>
/// 原版 <c>PlatingPower.AfterSideTurnStart</c>：持有者在本次参与名单里、**且不是玩家的第一
/// 回合**、且不是敌人的第一轮，才衰减；敌人按 <c>Decrement</c> 变量减，玩家减 1。
///
/// 改版（开关 `EternalArmor`）换掉了整个方法，两处差别：
/// <list type="number">
///   <item>**玩家第一回合也衰减** —— 原版跳过那一次，改版里那道判断被写成了一个空的 if，
///     实际不起作用。</item>
///   <item>**玩家身上有 <c>EternalArmorPower</c> 时完全不衰减。** 那个 Power 是个纯标记，
///     自己一个钩子都没重写，全部作用就是这里这一句。</item>
/// </list>
/// 敌人侧和原版一致。
///
/// 求解器这一段不在任何注册表里：<c>SimulatedCombatState.TriggerBaseSideTurnStart</c> 里
/// 写死了「按 Decrement 减」，要不要减由调用方一个 <c>decrementPlating</c> 布尔参数决定，
/// 而那个参数算的正是原版「非第一回合」的判据。所以改写这条最省的办法就是在前缀里改那个参数：
/// 玩家侧强制成「有永恒铠甲就不减，没有就减」，敌人侧一个字不动。
///
/// 镀甲是通用 Power（无色牌、Regent 牌、遗物、敌人都会给），算错就是整条防御线算错，
/// 所以这条排在所有角色批前面。
/// </remarks>
internal static class PlatingDecayPatch
{
    private const string TargetName = "TriggerBaseSideTurnStart";

    public static MethodInfo ResolveTarget()
        => AccessTools.Method(typeof(SimulatedCombatState), TargetName)
           ?? throw new MissingMethodException(nameof(SimulatedCombatState), TargetName);

    public static void Prefix(SimulatedCombatState __instance, Creature owner, ref bool decrementPlating)
    {
        if (!AdapterSettings.Current.EternalArmor)
            return;
        if (owner.Player is null)
            return;
        decrementPlating = __instance.GetAmount<EternalArmorPower>(owner) <= 0;
    }
}
