using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using CombatSolver.Engine.Common.Mirrors;
using CombatSolver.Engine.InCombat.Mirrors.Cards;
using RebalancedSpire.Core.Afflictions;
using RebalancedSpire.Core.Configs;

namespace AutoRebalancedSpire;

/// <summary>
/// 状态牌的镜像。目前只有一张：枯萎（<c>Wither</c>，永世沙漏那条线上的）。
/// </summary>
/// <remarks>
/// 枯萎在原版是「回合结束还在手上就吃 Damage 点伤害」，求解器登记的就是那个通用处理。
/// 改版把它整个换了：
/// <list type="bullet">
///   <item>变量换成 <c>Damage(0)</c> + <c>Fixed=6</c> + <c>PerLevel=3</c>，关键字清空；</item>
///   <item><b>没带「凋零」病症时</b>吃固定 6 点（<c>Fixed</c>），伤害属性是 Unpowered|Move；</item>
///   <item><b>带了病症时</b>，只有假升级层数不为 0 才吃 <c>Damage</c> 那份 —— 层数是 0 就完全
///     不吃伤害。假升级每一层给 <c>Damage</c> 加 <c>PerLevel</c>。</item>
/// </list>
/// 也就是说带上病症之后这张牌的伤害从「固定」变成「按层数」，求解器按原版算会一直算成
/// <c>Damage</c> 那一份（改版基数是 0），等于把一张会持续掉血的状态牌当成无害的。
/// </remarks>
internal static class StatusCardMirrors
{
    public static IEnumerable<MirroredHookReplacement> All()
    {
        yield return new MirroredHookReplacement(
            typeof(Wither),
            settings => settings.Aeonglass,
            () => RegistryOverride.DropRegistration(CardOnTurnEndInHandMirrors.Registry, typeof(Wither)),
            () => CardOnTurnEndInHandMirrors.Registry.Register<Wither>(Wither));
    }

    private static void Wither(Wither card, CardOnTurnEndInHandMirrorContext context)
    {
        if (card.Affliction is not Withering)
        {
            DamageOwner(context, card.DynamicVars["Fixed"].BaseValue, ValueProp.Unpowered | ValueProp.Move);
            return;
        }
        if (card.FakeUpgradeLevel != 0)
            DamageOwner(context, card.DynamicVars.Damage.BaseValue, card.DynamicVars.Damage.Props);
    }

    private static void DamageOwner(CardOnTurnEndInHandMirrorContext context, decimal amount, ValueProp props)
    {
        Creature owner = context.PreviewCard.Owner.Creature;
        context.Simulator.Damage([owner], amount, props, owner, context.Card, cardPlay: null);
    }
}

/// <summary>
/// 一处「求解器已经登记、我们要换掉」的钩子镜像：类型、开关、怎么摘、怎么登记。
/// </summary>
/// <remarks>
/// 和 <see cref="MirroredCard" /> 是一回事，只是那个专管 <c>OnPlay</c>（还要参与放行名单），
/// 这个管别的钩子 —— 别的钩子不进建根审查，所以不需要放行。
/// </remarks>
internal sealed record MirroredHookReplacement(
    Type ModelType,
    Func<RebalancedSpireSettings, bool> Toggle,
    Func<bool> Drop,
    Action Register);
