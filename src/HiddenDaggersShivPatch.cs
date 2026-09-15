using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Cards;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using RebalancedSpire.Core.Enchantments;

namespace AutoRebalancedSpire;

/// <summary>
/// 藏匿匕首造出来的匕首要挂「充能」附魔，而不是随本牌升级。
/// </summary>
/// <remarks>
/// <para><b>这张牌为什么不能照别的牌那样写镜像。</b>它的效果分两段，中间隔着一个玩家选择：
/// 先从手牌里选几张弃掉，<b>选完之后</b>才造匕首。求解器把这两段分开处理——
/// 弃牌那段走 <c>CardChoiceSupport.GetSpec</c> 开成搜索分支，造匕首那段在
/// <c>CardChoiceSupport.ApplyPostChoiceEffects</c>（在 <c>CardChoiceResolution.cs</c> 里） 里，等选择结算完才跑。</para>
///
/// <para>我们原来的 OnPlay 镜像在出牌那一刻就把匕首造进手牌了，顺序是反的。后果不是差一点数值：
/// 模拟里手牌提前多出两张匕首，求解器于是计划「把匕首弃掉」，而实机那个弃牌页面是在造匕首
/// <b>之前</b>弹的、里面根本没有匕首，部署时报
/// <c>NativeChoicePlanMismatchException：原生选牌页面找不到 SHIV+0#0</c>，整场操作不了。
/// 2026-09-14 那份感染棱柱的问题包就是这个。</para>
///
/// <para><b>改版和原版的实际差别只有一处。</b>张数（<c>Cards</c> 3、升级 −1）是数据层，
/// 求解器读活的变量、自动跟随，不用管。真正不同的是造出来的匕首：原版是本牌升级则匕首升级，
/// 改版是每张挂 1 层「充能」、永不升级。所以这里只接管造匕首那一段，弃牌那段照求解器原样走。</para>
/// </remarks>
internal static class HiddenDaggersShivPatch
{
    private const string TargetName = "ApplyPostChoiceEffects";

    public static MethodInfo ResolveTarget()
        => AccessTools.Method(typeof(CardChoiceSupport), TargetName)
           ?? throw new MissingMethodException(nameof(CardChoiceSupport), TargetName);

    public static bool Prefix(
        CombatPredictionSimulator simulator,
        SimulatedCombatState combat,
        PredictedCard playedCard,
        ref bool __result)
    {
        _ = combat;
        if (playedCard.Preview is not HiddenDaggers card)
            return true;
        if (!AdapterSettings.Current.HiddenDaggers)
            return true;

        // 原版这里是 GenerateShivs(..., upgraded: card.IsUpgraded)。改版不升级，改挂附魔。
        var shivs = CardPileOnPlaySupport.GenerateShivs(
            simulator, card.Owner, card.DynamicVars["Shivs"].IntValue, upgraded: false);
        foreach (PredictedCard shiv in shivs)
            shiv.Enchant(CanonicalModels.Enchantment<Energetic>().ToMutable(), 1m);

        __result = !simulator.HasPendingChoice;
        return false;
    }
}
