using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Afflictions;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Card;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.TurnEnd;
using CombatSolver.Engine.InCombat.Simulation;
using RebalancedSpire.Core.Powers;

namespace AutoRebalancedSpire;

/// <summary>
/// 污染+：寄生棱镜精英战里替换生命火花的那套机制。
/// </summary>
/// <remarks>
/// 原版寄生棱镜给玩家 <c>VitalSparkPower</c>，把牌组里所有技能牌一次性污染掉；改版换成
/// <c>TaintedPlusPower</c>，每个玩家回合的前 3 张攻击/技能各触发一次，每次随机污染手里一张
/// 还没有病症的攻击/技能牌，被污染的牌本回合免费、并临时获得「消耗」，回合结束全部还原。
///
/// 三处要做的事：
/// <list type="number">
///   <item><c>AfterCardPlayedLate</c> 和 <c>BeforeSideTurnEnd</c> 都有注册表，直接登记。
///     费用归零那条是取值钩子（<c>TryModifyEnergyCostInCombatLate</c>），没登记会回落到
///     Power 自己的实现，自动跟随，不用做。</item>
///   <item>「本回合还剩几次」是 Power 的一个私有字段，不在动态变量里，进不了指纹。
///     按求解器自己的办法放进 <c>StateStore</c>，再登记成隐藏状态，让它进指纹。</item>
///   <item>求解器的 <c>NormalizePowerAfflictions</c> 会把**所有**污染病症在
///     「场上没有生命火花」时清掉 —— 那是原版的正确做法，因为原版污染只可能来自生命火花。
///     改版的来源换了，这条清除会把我们刚打上的污染抹掉，所以要在它前后把污染护住。</item>
/// </list>
/// </remarks>
internal static class TaintedPlusMirrors
{
    public static int RegisterAll()
    {
        AfterCardPlayedMirrors.LateRegistry.Register<TaintedPlusPower>(AfterCardPlayedLate);
        BeforeSideTurnEndMirrors.Registry.Register<TaintedPlusPower>(BeforeSideTurnEnd);
        PowerHiddenStateMirrors.Register<TaintedPlusPower>(
            "CardsPlayed",
            static (simulator, power) => State(simulator, power).Value);
        return 2;
    }

    private static CounterPredictionState State(
        CombatPredictionSimulator simulator,
        TaintedPlusPower power)
        => simulator.StateStore.Get(power, () => new CounterPredictionState(power.DisplayAmount));

    /// <summary>打出攻击或技能之后：随机污染手里一张干净的攻击/技能牌。</summary>
    private static void AfterCardPlayedLate(
        TaintedPlusPower power,
        AfterCardPlayedMirrorContext context)
    {
        if (power.Owner.Player is not { } owner)
            return;
        if (!ReferenceEquals(context.PreviewCard.Owner, owner))
            return;
        if (context.PreviewCard.Type is not (CardType.Attack or CardType.Skill))
            return;

        CounterPredictionState state = State(context.Simulator, power);
        if (state.Value <= 0)
            return;
        state.Value--;

        PredictedCard[] options = context.Simulator.State.GetPlayerCombatState(owner).Hand.Cards
            .Where(static card => !card.Preview.Keywords.Contains(CardKeyword.Unplayable)
                && card.Preview.Type is CardType.Attack or CardType.Skill
                && card.Preview.Affliction == null)
            .ToArray();
        if (options.Length == 0)
            return;

        if (context.Simulator.Rng.CombatCardSelection.NextItem(options) is not { } selected)
            return;
        context.Simulator.Afflict<Tainted>(selected, 1m);
        if (!selected.Preview.Keywords.Contains(CardKeyword.Exhaust))
            selected.MutablePreview.AddKeyword(CardKeyword.Exhaust);
    }

    /// <summary>自己这一侧回合结束：次数补满，本回合打上的污染和「消耗」全部还原。</summary>
    /// <remarks>
    /// 实机用一张挂在病症实例上的表记住「消耗是我加的」。这里改成看牌的原始模型有没有消耗 ——
    /// 病症在分支之间会被克隆，挂在它身上的表跟着克隆走一份是另一套要维护的状态，
    /// 而污染只可能是本回合这里打上的，「原始模型上没有消耗」和「消耗是我加的」在这里等价。
    /// 唯一对不上的情形是同一回合里别的效果先给同一张牌加了消耗，目前没有这样的来源。
    /// </remarks>
    private static void BeforeSideTurnEnd(
        TaintedPlusPower power,
        BeforeSideTurnEndMirrorContext context)
    {
        if (!context.Participants.Contains(power.Owner))
            return;
        if (power.Owner.Player is not { } owner)
            return;

        State(context.Simulator, power).Value = power.DynamicVars.Cards.IntValue;
        foreach (PredictedCard card in context.Simulator.State
                     .GetPlayerCombatState(owner).AllCards.ToArray())
        {
            if (card.Preview.Affliction is not Tainted)
                continue;
            if (!card.Original.Keywords.Contains(CardKeyword.Exhaust))
                card.MutablePreview.RemoveKeyword(CardKeyword.Exhaust);
            card.ClearAffliction();
        }
    }

    // ---------- 护住污染，不让求解器按原版口径清掉 ----------

    private const string NormalizeName = "NormalizePowerAfflictions";

    [ThreadStatic]
    private static List<(PredictedCard Card, decimal Amount)>? _protected;

    public static MethodInfo ResolveNormalizeTarget()
        => AccessTools.Method(typeof(SimulatedCombatState), NormalizeName)
           ?? throw new MissingMethodException(nameof(SimulatedCombatState), NormalizeName);

    public static void NormalizePrefix(
        SimulatedCombatState __instance,
        CombatPredictionSimulator simulator)
    {
        _protected = null;
        foreach (Player player in __instance.Players)
        {
            if (__instance.GetAmount<TaintedPlusPower>(player.Creature) <= 0)
                continue;
            foreach (PredictedCard card in simulator.State.GetPlayerCombatState(player).AllCards)
            {
                if (card.Preview.Affliction is Tainted tainted)
                    (_protected ??= []).Add((card, tainted.Amount));
            }
        }
    }

    public static void NormalizePostfix(CombatPredictionSimulator simulator)
    {
        if (_protected is not { Count: > 0 } saved)
            return;
        _protected = null;
        foreach ((PredictedCard card, decimal amount) in saved)
        {
            // 只在真被清掉的那些上补回来。求解器那一段清完就 continue，不会再打别的病症，
            // 所以「现在是空的」和「刚才被清掉了」等价。
            if (card.Preview.Affliction == null)
                simulator.Afflict<Tainted>(card, amount);
        }
    }
}
