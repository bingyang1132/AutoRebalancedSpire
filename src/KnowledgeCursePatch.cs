using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;
using CombatSolver;
using RebalancedSpire.Core.Configs;

namespace AutoRebalancedSpire;

/// <summary>
/// 知识恶魔三选一里「崩解」的数值。
/// </summary>
/// <remarks>
/// 原版三次分别给 6、7、8 层崩解（求解器写成 <c>6 + 计数</c>）；改版换成 4、6、8。
/// 第一次差 2 层、第二次差 1 层 —— 崩解是回合结束按层数扣真实生命的，几层的误差直接落在
/// 「这条路线会不会把自己打死」上。
///
/// 求解器那段结算（<c>KnowledgeDemonChoiceSupport.Resolve</c>）把选项判定、计划记录、
/// 计数推进写在一起，整个替换掉不划算。这里只在它跑完之后按差额补一次：
/// 前缀记下计数和玩家身上原有的层数，后缀看这次实际加了多少，认出是崩解那一支就补差。
/// 没做出选择（挂起等分支）时计数不会推进，后缀什么都不做。
/// </remarks>
internal static class KnowledgeCursePatch
{
    /// <summary>改版三次分别给的崩解层数。</summary>
    private static readonly int[] DisintegrationAmounts = [4, 6, 8];

    private readonly record struct Snapshot(int Counter, int Amount);

    [ThreadStatic]
    private static Snapshot? _before;

    public static MethodInfo ResolveTarget()
        => AccessTools.Method(
               typeof(KnowledgeDemonChoiceSupport),
               nameof(KnowledgeDemonChoiceSupport.Resolve))
           ?? throw new MissingMethodException(
               nameof(KnowledgeDemonChoiceSupport), nameof(KnowledgeDemonChoiceSupport.Resolve));

    public static void Prefix(SimulatedCombatState combat, Creature source, Creature player)
    {
        _before = null;
        if (!AdapterSettings.Current.KnowledgeDemon)
            return;
        int counter = combat.GetKnowledgeDemonCurseCounter(source);
        if ((uint)counter >= (uint)DisintegrationAmounts.Length)
            return;
        _before = new Snapshot(counter, combat.GetAmount<DisintegrationPower>(player));
    }

    public static void Postfix(SimulatedCombatState combat, Creature source, Creature player)
    {
        if (_before is not { } before)
            return;
        _before = null;
        // 挂起了选择：这一次什么都没结算，计数也没推进。
        if (combat.GetKnowledgeDemonCurseCounter(source) == before.Counter)
            return;

        int solverAmount = 6 + before.Counter;
        if (combat.GetAmount<DisintegrationPower>(player) - before.Amount != solverAmount)
            return;
        int correction = DisintegrationAmounts[before.Counter] - solverAmount;
        if (correction != 0)
            combat.Apply<DisintegrationPower>(player, correction, player);
    }
}
