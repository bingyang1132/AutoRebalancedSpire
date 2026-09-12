using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using CombatSolver;

namespace AutoRebalancedSpire;

/// <summary>
/// 让求解器放行**我们已经镜像过**的那些被 RebalancedSpire 替换了 <c>OnPlay</c> 的牌。
/// </summary>
/// <remarks>
/// 求解器在建根时会审一遍牌组：任何一张牌的 <c>OnPlay</c> 上挂着第三方 Harmony 补丁，
/// 就抛 <c>IncompatibleGameplayModException</c>，整场战斗不规划。这是对的 —— 它的镜像是按
/// 原版语义写的，替换过的牌再按原版算就是静默算错。
///
/// 但求解器**没有给第三方留放行入口**（`PredictionModPatchAudit` 是 internal，豁免名单写死在
/// 里面）。所以这里只能打补丁：把我们确实镜像了的牌从待审名单里摘掉，其余的一张不动，照样
/// 响亮地拒绝。这正是「没有登记点只好打补丁」，和当初天人形态那一条一样；将来应该由上游开一个
/// 登记入口来替掉它。
///
/// 放行的判据故意收得很紧，三条全中才摘：
/// <list type="number">
///   <item>这张牌在我们的名单里，而且 RebalancedSpire 的开关此刻仍然是开的；</item>
///   <item>它的 <c>OnPlay</c> 上每一个补丁都来自 RebalancedSpire 或本适配层；</item>
///   <item>补丁信息读得出来。读不出来就不摘。</item>
/// </list>
/// 第二条挡的是「两个 mod 同时改同一张牌」：那时谁的前缀先跑都不确定，我们的镜像未必对得上，
/// 必须让求解器停在门口。
/// </remarks>
internal static class AuditFilter
{
    private static readonly string[] TrustedAssemblies =
        [PinnedTargets.RebalancedSpireModId, Entry.ModId];

    public static MethodInfo ResolveTarget()
        => AccessTools.Method(
               typeof(PredictionModPatchAudit),
               nameof(PredictionModPatchAudit.ValidateCardOnPlay))
           ?? throw new MissingMethodException(
               nameof(PredictionModPatchAudit),
               nameof(PredictionModPatchAudit.ValidateCardOnPlay));

    public static void Prefix(ref IEnumerable<CardModel> cards)
    {
        IEnumerable<CardModel> original = cards;
        cards = original.Where(card => !FullyMirrored(card.GetType()));
    }

    private static bool FullyMirrored(Type cardType)
    {
        if (!MirroredCards.StillMirrors(cardType))
            return false;

        MethodInfo? onPlay = AccessTools.Method(
            cardType, "OnPlay", [typeof(PlayerChoiceContext), typeof(CardPlay)]);
        if (onPlay == null)
            return false;

        Patches? patches = Harmony.GetPatchInfo(onPlay);
        if (patches == null)
            return false;

        foreach (Patch patch in patches.Prefixes
                     .Concat(patches.Postfixes)
                     .Concat(patches.Transpilers)
                     .Concat(patches.Finalizers))
        {
            Assembly? owner = patch.PatchMethod.DeclaringType?.Assembly;
            string? name = owner?.GetName().Name;
            if (name == null || !TrustedAssemblies.Contains(name, StringComparer.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }
}
