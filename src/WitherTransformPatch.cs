using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Cards;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using RebalancedSpire.Core.Afflictions;

namespace AutoRebalancedSpire;

/// <summary>
/// 带「无法逃脱」的凋萎被<b>变形</b>掉之后，改版会往弃牌堆随机位置补发一张同级的凋萎。
/// </summary>
/// <remarks>
/// <para>改版把这一条注册在 RitsuLib 的 <c>ModCardTransformRegistry</c> 上
/// （<c>RebalancedSpireCardTransformationRegistry</c>）：源是 <c>Wither</c>、目标是任意牌，
/// 条件是这张凋萎身上挂着 <c>Withering</c>。补发的那张<b>保留原来的假升级层数</b>，重新挂上病症，
/// 然后 <c>AddGeneratedCardToCombat(…, Discard, Random)</c>。</para>
///
/// <para><b>为什么非补不可。</b>永世沙漏那场，把凋萎变形掉是玩家真会做的事 ——
/// 降灵会就是「从抽牌堆选几张变成魂」，而求解器<b>自己就会开这个分支</b>
/// （问题包里的 <c>choice_effect=Transform</c>）。不镜像的话它会认为「变掉就没了」，
/// 而实机立刻补回一张，于是既静默高估了这条线，又多取了一个洗牌随机数（随机位置那一步），
/// 下一个回合边界必然重算。</para>
///
/// <para><b>为什么是补丁。</b>求解器这一侧的变形收口在
/// <c>CardChoiceSupport.ReplaceTransformedCard</c> —— 私有静态方法，没有第三方登记点；
/// 卡牌变形也不在任何一份镜像注册表里。收口只有这一处，所以补它就够：
/// 降灵会、鬼火、双持之类全部经过这里。</para>
///
/// <para><b>为什么不挂开关。</b>改版这一条自己就没有开关（注册是无条件的，判据只有
/// 「这张凋萎带不带病症」），而病症只可能来自永世沙漏 —— 那只怪的开关关掉就没有带病症的凋萎，
/// 这一条自然不触发。照它的形状写，不额外造一层依赖。</para>
/// </remarks>
internal static class WitherTransformPatch
{
    private const string TargetName = "ReplaceTransformedCard";

    public static MethodInfo ResolveTarget()
        => AccessTools.Method(typeof(CardChoiceSupport), TargetName)
           ?? throw new MissingMethodException(nameof(CardChoiceSupport), TargetName);

    public static void Postfix(
        CombatPredictionSimulator simulator,
        PredictedCard original,
        bool __result)
    {
        // 原实现半路返回假的时候（起了未解析的选择），实机那一侧也还没走到补发，别抢跑。
        if (!__result)
            return;
        if (original.Preview is not Wither wither || wither.Affliction is not Withering)
            return;

        int fakeUpgrades = wither.FakeUpgradeLevel;
        foreach (var added in simulator.CreateAndAddGeneratedCardsToCombat<Wither>(
                     original.Preview.Owner,
                     PileType.Discard,
                     1,
                     null,
                     CardPilePosition.Random))
        {
            if (added.CardAdded.MutablePreview is Wither replacement)
            {
                for (int level = 0; level < fakeUpgrades; level++)
                    replacement.FakeUpgrade();
            }
            simulator.Afflict<Withering>(added.CardAdded, 1m);
        }
    }
}
