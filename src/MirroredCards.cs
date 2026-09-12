using MegaCrit.Sts2.Core.Models;
using CombatSolver.Engine.Common.Mirrors;
using CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;
using RebalancedSpire.Core.Configs;

namespace AutoRebalancedSpire;

/// <summary>一张被镜像的牌：牌的类型、它在 RebalancedSpire 设置里的开关、以及怎么登记。</summary>
/// <param name="ReplacesBuiltIn">
/// 求解器自己是不是也给这张牌登记了 bespoke 镜像。是的话要先摘掉再登记。
/// 这个值和实际情况对不上就是自检失败：说明上游动了它的镜像表，我们对着的前提变了。
/// </param>
internal sealed record MirroredCard(
    Type CardType,
    Func<RebalancedSpireSettings, bool> Toggle,
    Action<MethodMirrorRegistry<CardModel, CardOnPlayMirrorContext>> Register,
    bool ReplacesBuiltIn = false)
{
    public static MirroredCard For<TCard>(
        Func<RebalancedSpireSettings, bool> toggle,
        Action<MethodMirrorRegistry<CardModel, CardOnPlayMirrorContext>> register,
        bool replacesBuiltIn = false)
        where TCard : CardModel
        => new(typeof(TCard), toggle, register, replacesBuiltIn);
}

/// <summary>
/// 本适配层已经镜像了哪些牌 —— 登记和放行共用这一份名单。
/// </summary>
/// <remarks>
/// 两件事必须用同一份名单，否则会出现最坏的一种错：求解器放行了一张牌，却没有它的镜像，
/// 于是按原版语义规划一条实机根本不会那样打的路线，而且一声不吭。
///
/// **开关是在加载时读一次的。** RebalancedSpire 把补丁无条件装上，补丁体里再判开关，所以
/// 「开关关掉」等于「这张牌回到原版语义」。我们在加载时按开关决定登不登记；如果玩家在运行中
/// 改了开关，放行那一步会发现和登记时的状态不一致，于是**不放行**——求解器会停在第三方 mod
/// 检查上，而不是拿着一份过期的镜像继续算。
/// </remarks>
internal static class MirroredCards
{
    private static readonly List<MirroredCard> Active = [];

    /// <summary>登记时开关是开着的那些牌。放行只认这一份。</summary>
    public static IReadOnlyList<MirroredCard> ActiveCards => Active;

    public static IReadOnlyCollection<Type> ActiveTypes { get; private set; } = [];

    public static int RegisterAll(MethodMirrorRegistry<CardModel, CardOnPlayMirrorContext> registry)
    {
        RebalancedSpireSettings settings = RebalancedSpireSettingsStore.Settings;
        foreach (MirroredCard card in CardMirrors.All())
        {
            if (!card.Toggle(settings))
                continue;
            bool dropped = RegistryOverride.DropRegistration(registry, card.CardType);
            if (dropped != card.ReplacesBuiltIn)
                throw new InvalidOperationException(
                    $"{card.CardType.Name}：求解器"
                    + (dropped ? "登记了" : "没有登记")
                    + "自己的 OnPlay 镜像，和本适配层记的相反。上游的镜像表变了，要重新核对。");
            card.Register(registry);
            Active.Add(card);
        }
        ActiveTypes = Active.Select(card => card.CardType).ToHashSet();
        return Active.Count;
    }

    /// <summary>这张牌现在是不是仍然由我们镜像着（登记过，而且开关没被改掉）。</summary>
    public static bool StillMirrors(Type cardType)
    {
        if (!ActiveTypes.Contains(cardType))
            return false;
        RebalancedSpireSettings settings = RebalancedSpireSettingsStore.Settings;
        return Active.Where(card => card.CardType == cardType).All(card => card.Toggle(settings));
    }
}
