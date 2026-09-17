using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using CombatSolver;
using RebalancedSpire.Core.Powers;

namespace AutoRebalancedSpire;

/// <summary>
/// Power 自己的数量变了之后要跟着改的那几个动态变量 —— 求解器不分发这个时点。
/// </summary>
/// <remarks>
/// 原版的入口是 <c>AbstractModel.AfterPowerAmountChanged</c>。求解器把它拆成了两段：
/// 数量变化先进 <c>SimulatedCombatState.RecordPowerAmountChange</c> 排队，之后由
/// <c>PowerLifecycleSupport.ResolvePowerAmountChanges</c> 按一张写死的 switch 分发
/// （只有裹尸布、血肉之巧、凶恶三条）。那张 switch 不是注册表，**也不记未镜像风险**。
///
/// 我们挂在入队那一步而不是分发那一步：入队点只有一个（<c>Apply</c> 里），拿到的就是分支里
/// 那份可变实例，而分发那一步的队列已经被 <c>Drain</c> 掉了，后缀里看不到。往世要补的只是
/// 「把两个动态变量重新算成数量的函数」，同步做和排队后做没有差别。
///
/// 不补的后果：往世的 <c>Heal</c>／<c>Summon</c> 停在建实例时那一刻的值（数量 0，也就是
/// 0 和 −1）。两个值都进续接戳，玩家每回合被强制重算；而且
/// <c>AfterEnergyResetLateDispatch</c> 读的就是它们，奥斯提会被治 0 点、或者按 −1 血召唤。
/// </remarks>
internal static class PowerAmountChangedDispatch
{
    private const string TargetName = nameof(SimulatedCombatState.RecordPowerAmountChange);

    public static MethodInfo ResolveTarget()
        => AccessTools.Method(typeof(SimulatedCombatState), TargetName)
           ?? throw new MissingMethodException(nameof(SimulatedCombatState), TargetName);

    public static void Postfix(PowerModel power)
    {
        if (power is not AfterlifePower afterlife || !AdapterSettings.Current.Afterlife)
            return;
        // 照抄改版的 AfterPowerAmountChanged：治疗量等于数量，召唤血量等于数量 − 1。
        afterlife.DynamicVars.Heal.BaseValue = afterlife.Amount;
        afterlife.DynamicVars.Summon.BaseValue = afterlife.Amount - 1;
    }
}
