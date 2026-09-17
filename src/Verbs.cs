using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Enchantments;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
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
    /// （例如旋转工艺换成了 SpinnerPlusPower）。直接索引字典的话，键写错只会在结算到这张牌时
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

    public static void PowerOn(
        CardOnPlayMirrorContext context, Type powerType, Creature target, int amount)
        => Effects(context).ApplyPower(powerType, target, amount, Self(context));

    // ---------- 攻击 ----------

    /// <summary>按牌自己的伤害变量打全体敌人。</summary>
    public static void AttackAllEnemies(CardOnPlayMirrorContext context, int hits = 1)
        => context.AttackAllOpponents(hits);

    // ---------- 说不清的地方 ----------

    /// <summary>这一处没能完整镜像，显式记一条风险，让求解器把它显示成红色。</summary>
    /// <remarks>
    /// 宁可红字也不要静默算错 —— 这是整个项目的底线。放行名单里有这张牌、但效果没补全时，
    /// 必须走这里。
    /// </remarks>
    public static void Unmirrored(CardOnPlayMirrorContext context, string what)
    {
        EngineDiagnostics.Warn($"[AutoRebalancedSpire] 未镜像：{what}");
        context.History.RecordRisk(PredictionRiskReason.MethodMirrorIncomplete);
    }

    /// <summary>这一处需要玩家在结算中做选择，而我们还没为它开分支。</summary>
    public static void PlayerChoice(CardOnPlayMirrorContext context, string what)
    {
        EngineDiagnostics.Warn($"[AutoRebalancedSpire] 未建模的结算内选择：{what}");
        context.History.RecordRisk(PredictionRiskReason.UnresolvedPlayerChoice);
    }

    // ---------- 造牌 ----------

    /// <summary>造若干匕首进手牌，可选附魔与升级。</summary>
    /// <remarks>
    /// 对应原版 <c>Shiv.CreateInHand</c>。附魔走求解器的 <c>Enchant</c> 扩展，它内部会先判
    /// <c>CanEnchant</c>，和原版 <c>CardCmd.Enchant</c> 一致。
    /// </remarks>
    public static void ShivsInHand(
        CardOnPlayMirrorContext context,
        int count,
        EnchantmentModel? enchantment = null,
        bool upgrade = false)
    {
        if (count <= 0)
            return;
        var added = context.Simulator
            .CreateAndAddGeneratedCardsToCombat<Shiv>(Owner(context), PileType.Hand, count, Owner(context));
        foreach (var result in added)
        {
            if (enchantment != null)
                result.CardAdded.Enchant(enchantment.ToMutable(), 1m);
            if (upgrade)
                context.Simulator.Upgrade(result.CardAdded);
        }
    }

    /// <summary>造若干魂进指定牌堆，可选升级。</summary>
    public static void SoulsInto(
        CardOnPlayMirrorContext context,
        PileType pile,
        int count,
        bool upgrade = false)
    {
        if (count <= 0)
            return;
        var added = context.Simulator.CreateAndAddGeneratedCardsToCombat<Soul>(
            Owner(context), pile, count, Owner(context), CardPilePosition.Random);
        if (!upgrade)
            return;
        foreach (var result in added)
            context.Simulator.Upgrade(result.CardAdded);
    }
}
