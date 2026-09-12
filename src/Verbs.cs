using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;
using CombatSolver.Engine.InCombat.Simulation;

namespace AutoRebalancedSpire;

/// <summary>
/// 所有镜像共用的取值与效果动词。
/// </summary>
/// <remarks>
/// 和 AutoWatcher 的 <c>WatcherVerbs</c> 同一套路：RebalancedSpire 的改动牌绝大多数是
/// 「原版命令 + 一个新 Power」，所以动词对了，每张牌的镜像就只剩几行声明式组合，
/// 可以逐行对照反编译出来的 OnPlay 替换来审。
/// </remarks>
internal static class Verbs
{
    public static SimulatedCombatState Combat(CardOnPlayMirrorContext context)
        => context.CombatState as SimulatedCombatState
            ?? throw new InvalidOperationException("镜像需要可写的模拟战斗状态。");

    public static ICombatPredictionEffectSink Effects(CardOnPlayMirrorContext context)
        => (ICombatPredictionEffectSink)Combat(context);

    public static Player Owner(CardOnPlayMirrorContext context) => context.PreviewCard.Owner;

    public static Creature Self(CardOnPlayMirrorContext context) => context.PreviewCard.Owner.Creature;

    /// <summary>读牌上的动态变量。键不存在时报出这张牌和它实际有哪些键。</summary>
    /// <remarks>
    /// RebalancedSpire 经常在替换 OnPlay 的同时换掉 CanonicalVars，键名跟原版不一样
    /// （例如纺纱换成了 SpinnerPlusPower）。直接索引字典的话，键写错只会在结算到这张牌时
    /// 抛一个不带上下文的 KeyNotFound。
    /// </remarks>
    public static DynamicVar RequireVar(CardModel card, string key)
    {
        if (card.DynamicVars.TryGetValue(key, out DynamicVar? value))
            return value;
        throw new KeyNotFoundException(
            $"镜像在 {card.Id.Entry} 上读不到动态变量 {key}。该牌实际有："
            + string.Join("、", card.DynamicVars.Select(pair => pair.Key)));
    }

    public static decimal Var(CardModel card, string key) => RequireVar(card, key).BaseValue;

    public static int VarInt(CardModel card, string key) => RequireVar(card, key).IntValue;

    // ---------- 格挡、抽牌、能量 ----------

    /// <summary>按牌自己的格挡变量给自己加格挡，沿用该变量的 ValueProp。</summary>
    public static void Block(CardOnPlayMirrorContext context) => context.GainBlock(Self(context));

    public static void Draw(CardOnPlayMirrorContext context, int count)
    {
        if (count > 0)
            context.Simulator.Draw(Owner(context), count);
    }

    public static void GainEnergy(CardOnPlayMirrorContext context, decimal amount)
    {
        if (amount > 0)
            context.Simulator.GainEnergy(Owner(context), amount);
    }

    public static void GainStars(CardOnPlayMirrorContext context, decimal amount)
    {
        if (amount > 0)
            context.Simulator.GainStars(Owner(context), amount);
    }

    // ---------- Power ----------

    /// <summary>施加任意 PowerModel，包括 RebalancedSpire 自己新加的那 33 个。</summary>
    public static void Power(CardOnPlayMirrorContext context, Type powerType, int amount)
        => Effects(context).ApplyPower(powerType, Self(context), amount, Self(context));
}
