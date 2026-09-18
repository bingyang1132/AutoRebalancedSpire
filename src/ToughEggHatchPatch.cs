using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using CombatSolver;

namespace AutoRebalancedSpire;

/// <summary>
/// 直飞产卵虫下的结实的卵，改版的孵化计数比原版多 1。
/// </summary>
/// <remarks>
/// <para>原版 <c>ToughEgg.AfterAddedToRoom</c>：<c>CurrentSide != Enemy ? 1 : 2</c>；
/// 改版 <c>ToughEggPatch.AfterAddedToRoom</c>：<c>CurrentSide != Enemy ? 2 : 3</c> ——
/// 两个分支都 +1，也就是卵要多待一回合才孵。改版同时给它的出招表前面加了一个空的 <c>STUN_MOVE</c>
/// （原版是 <c>HATCH_MOVE → NIBBLE_MOVE</c>），这一半求解器是照实机的状态机现读的，跟得上；
/// 差的只有层数。</para>
///
/// <para><b>为什么是补丁而不是登记。</b>求解器给召唤出来的怪物挂入场 Power 的地方是
/// <c>MonsterSpawnSupport.ApplyNativeEntrancePowers</c> —— 一个私有静态方法里的 <c>switch</c>，
/// 没有第三方登记点（<c>ModelPredictionStateMirrors</c> 只对遗物和修饰器开放，
/// 怪物招式那条路也够不着：挂层数发生在 <c>Spawn</c> 里面，不在 <c>MonsterMoveEffects</c> 的招式体里）。
/// 开局就在场的那些怪是建根时照实机捕获的，所以只有「战斗中新下的蛋」会走到这里。</para>
///
/// <para><b>为什么是后缀加 1、而不是重写整个分支。</b>照抄改版那个三元表达式，等于把原版的基准
/// 也复制一份到这里；上游哪天改了自己的基准，这里就会静默偏掉。后缀只做「在求解器算出来的值上 +1」，
/// 表达的正是改版和原版的差量。</para>
///
/// <para>不修的后果：每下一个蛋，预测的 <c>HatchPower</c> 就比实机少 1，回合边界上 Power 列表对不上，
/// <b>整场重算</b> —— 2026-09-18 直飞产卵虫那个问题包里一场战斗重算了 3 次，
/// 每一次都是 <c>HATCH_POWER expected=1 actual=2</c>。</para>
/// </remarks>
internal static class ToughEggHatchPatch
{
    private const string TargetName = "ApplyNativeEntrancePowers";

    /// <summary>改版比原版多的那一层。</summary>
    private const int ExtraHatchTurns = 1;

    public static MethodInfo ResolveTarget()
        => AccessTools.Method(typeof(MonsterSpawnSupport), TargetName)
           ?? throw new MissingMethodException(nameof(MonsterSpawnSupport), TargetName);

    public static void Postfix(SimulatedCombatState combat, Creature creature)
    {
        if (creature.Monster is not ToughEgg || !AdapterSettings.Current.Ovicopter)
            return;

        // 已经孵过的卵不挂这一层（改版和原版都只在 !IsHatched 时挂），所以按「求解器挂了没有」判，
        // 而不是无条件 +1。
        int amount = combat.GetAmount<HatchPower>(creature);
        if (amount > 0)
            combat.SetAmount<HatchPower>(creature, amount + ExtraHatchTurns);
    }
}
