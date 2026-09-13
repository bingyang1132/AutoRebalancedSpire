using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Cards;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using RebalancedSpire.Core.Configs;

namespace AutoRebalancedSpire;

/// <summary>
/// 改写求解器算「计算变量」时用的乘数。
/// </summary>
/// <remarks>
/// 求解器不敢在分支里调那个绑在实时战斗图上的乘数委托（分支一旦分叉，委托读到的是实机状态
/// 而不是这条分支的状态），所以把每张计算牌的乘数在 <c>CalculatedVarSpecRegistry</c> 里
/// 用分支局部的写法重写了一遍 —— 一个写死的 switch，internal，没有第三方入口。
///
/// RebalancedSpire 改了公式的只有一张：**ExpectAFight**。原版乘数是力量，改版换成了
/// **手牌里攻击牌的张数**（`PileType.Hand` = 2，`CardType.Attack` = 1，枚举顺序核过）。
/// 不改这里的话，镜像了 <c>OnPlay</c> 也没用：值还是按力量算出来的。
///
/// Synchronize 原本也在那张表里，但改版把它的 <c>CalculatedVar</c> 整个从 CanonicalVars 里
/// 去掉了（换成一个普通 PowerVar），所以那一条不用管。
///
/// 只接管我们认得的牌，其余一律放行原实现。
/// </remarks>
internal static class CalculatedVarPatch
{
    private const string TargetName = "TryMultiplier";

    public static MethodInfo ResolveTarget()
        => AccessTools.Method(typeof(CalculatedVarSpecRegistry), TargetName)
           ?? throw new MissingMethodException(nameof(CalculatedVarSpecRegistry), TargetName);

    public static bool Prefix(
        CombatPredictionSimulator simulator,
        PredictedCard card,
        Creature? target,
        ref decimal multiplier,
        ref bool __result)
    {
        _ = target;
        if (card.Preview is not ExpectAFight)
            return true;
        if (!AdapterSettings.Current.ExpectAFight)
            return true;

        multiplier = simulator.State.GetPlayerCombatState(card.Preview.Owner)
            .Hand.Cards.Count(candidate => candidate.Preview.Type == CardType.Attack);
        __result = true;
        return false;
    }
}
