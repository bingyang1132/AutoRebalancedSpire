using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Card;
using CombatSolver.Engine.InCombat.Simulation;
using RebalancedSpire.Core.Configs;

namespace AutoRebalancedSpire;

/// <summary>低语耳环在这一场里攒了多少能量、有没有蓄势待发。</summary>
/// <remarks>
/// 走求解器给第三方留的 <c>ModelPredictionStateMirrors</c>：它会替我们做分叉拷贝，
/// 并把这两个值一起写进搜索指纹和续接戳。自己往 <c>StateStore</c> 里塞一份不会进指纹，
/// 「攒了 12 点」和「攒了 3 点」会被当成同一个局面。
/// </remarks>
internal sealed class WhisperingEarringState : IPredictionStateForkable
{
    /// <summary>本场已花掉的能量。<c>-1</c> 表示已经攒够过一次，之后不再累计。</summary>
    public long EnergyUsed { get; set; }

    /// <summary>已攒够，下一张牌会触发连打。</summary>
    public bool Armed { get; set; }

    public object Fork(PredictionForkContext context) => MemberwiseClone();
}

/// <summary>
/// 低语耳环整个换了触发条件。
/// </summary>
/// <remarks>
/// 原版：**第一回合**的自动出牌阶段，直接连打最多 13 张。求解器为此专门写了
/// <c>SimulatedCombatState.TriggerWhisperingEarring</c>，里面连 Vakuu 选牌器的固定策略都照抄了。
///
/// 改版（开关 `WhisperingEarring`）把原版那一段**整个关掉**
/// （<c>AfterAutoPrePlayPhaseEnteredLate</c> 直接返回），换成：本场累计花掉 13 点能量之后蓄势，
/// 你**下一张**打出的牌结算完就触发那一轮连打，一场只触发一次。
///
/// 所以不补的话求解器两头都错，而且都是静默的：第一回合替玩家连打了 13 张实机不会打的牌，
/// 该触发的那一次又完全看不见。这是本适配层碰到的最严重的一处单点偏差 ——
/// 它直接决定第一回合能打出什么。
///
/// 连打本身不重写：把求解器那段循环原样复用（用一个线程标记放行自己的再入），
/// 上限两边都是 13 张，选牌策略、目标选取、死亡结算全都跟着它走。
/// </remarks>
internal static class WhisperingEarringPatch
{
    private const string TriggerName = nameof(SimulatedCombatState.TriggerWhisperingEarring);
    private const string EnergySpentName = nameof(PowerLifecycleSupport.AfterEnergySpent);
    private const string CardPlayedLateName = nameof(AfterCardPlayedMirrors.InvokeLate);

    /// <summary>放行自己那次再入 —— 前缀平时会把原版的第一回合连打整个拦掉。</summary>
    [ThreadStatic]
    private static bool _reentering;

    public static void RegisterState()
    {
        ModelPredictionStateMirrors.RegisterRelic<WhisperingEarring, WhisperingEarringState>(
            "auto-rebalanced-spire-whispering-earring-v1",
            static (_, relic) => new WhisperingEarringState
            {
                EnergyUsed = relic.DisplayAmount,
                Armed = IsArmed(relic),
            },
            static (WhisperingEarring relic, ref ModelPredictionStateWriter writer) =>
            {
                writer.Add("used", (long)relic.DisplayAmount);
                writer.Add("armed", IsArmed(relic));
            },
            static (WhisperingEarringState state, ref ModelPredictionStateWriter writer) =>
            {
                writer.Add("used", state.EnergyUsed);
                writer.Add("armed", state.Armed);
            });
    }

    /// <summary>实机把「蓄势待发」记在遗物的 <c>Status</c> 上，置 1 就是待发。</summary>
    private static bool IsArmed(RelicModel relic) => (int)relic.Status == 1;

    public static MethodInfo ResolveTriggerTarget()
        => AccessTools.Method(typeof(SimulatedCombatState), TriggerName)
           ?? throw new MissingMethodException(nameof(SimulatedCombatState), TriggerName);

    public static MethodInfo ResolveEnergySpentTarget()
        => AccessTools.Method(typeof(PowerLifecycleSupport), EnergySpentName)
           ?? throw new MissingMethodException(nameof(PowerLifecycleSupport), EnergySpentName);

    public static MethodInfo ResolveCardPlayedLateTarget()
        => AccessTools.Method(typeof(AfterCardPlayedMirrors), CardPlayedLateName)
           ?? throw new MissingMethodException(
               nameof(AfterCardPlayedMirrors), CardPlayedLateName);

    /// <summary>原版的第一回合连打：改版关掉了，这里拦住。自己再入时放行。</summary>
    public static bool TriggerPrefix(ref bool __result)
    {
        if (_reentering || !AdapterSettings.Current.WhisperingEarring)
            return true;
        __result = true;
        return false;
    }

    /// <summary>花能量：累计到 13 点就蓄势，之后不再累计。</summary>
    public static void EnergySpentPostfix(
        CombatPredictionSimulator simulator,
        SimulatedCombatState combat,
        PredictedCard card,
        int amount)
    {
        if (amount <= 0 || !AdapterSettings.Current.WhisperingEarring)
            return;
        if (card.Preview.Owner is not { } owner)
            return;

        foreach (WhisperingEarring relic in combat.RelicsOf(owner)
                     .OfType<WhisperingEarring>()
                     .Where(static relic => !relic.IsMelted))
        {
            WhisperingEarringState state =
                ModelPredictionStateMirrors.Get<WhisperingEarringState>(simulator, relic);
            if (state.EnergyUsed < 0)
                continue;
            state.EnergyUsed += amount;
            if (state.EnergyUsed < relic.DynamicVars["TotalEnergy"].IntValue)
                continue;
            state.EnergyUsed = -1;
            state.Armed = true;
        }
    }

    /// <summary>蓄势之后打出的第一张牌结算完，触发那一轮连打。</summary>
    /// <remarks>
    /// 挂在 <c>InvokeLate</c> 的后缀上而不是登记进注册表：注册表要求类型真的重写了
    /// <c>AfterCardPlayedLate</c>，而改版是补在 <c>AbstractModel</c> 上的，耳环自己没有重写。
    ///
    /// 连打里那些牌自己也会走到这里，但触发的那一刻就已经把标记清了，不会递归。
    /// </remarks>
    public static void CardPlayedLatePostfix(
        AbstractModel listener,
        AfterCardPlayedMirrorContext context)
    {
        if (listener is not WhisperingEarring relic || relic.IsMelted)
            return;
        if (!AdapterSettings.Current.WhisperingEarring)
            return;
        if (context.PreviewCard.Owner != relic.Owner)
            return;
        if (context.CombatState is not SimulatedCombatState combat)
            return;

        WhisperingEarringState state =
            ModelPredictionStateMirrors.Get<WhisperingEarringState>(context.Simulator, relic);
        if (!state.Armed)
            return;
        state.Armed = false;

        // 复用求解器自己那段连打：回合号传 1 是为了绕过它「只在第一回合」的门槛，
        // 张数上限两边都是 13，选牌和目标策略照它的走。
        _reentering = true;
        try
        {
            combat.TriggerWhisperingEarring(context.Simulator, relic.Owner, 1, new HashSet<uint>());
        }
        finally
        {
            _reentering = false;
        }
    }
}
