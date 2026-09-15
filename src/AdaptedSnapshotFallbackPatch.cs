using System.Collections;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.Common.Mirrors;

namespace AutoRebalancedSpire;

/// <summary>
/// 战斗中生成的牌（刀刃、灼烧、伤口……）不在建根审计表里，打出来会让整条搜索炸掉。
/// 这里给它们一条安全的回退。
/// </summary>
/// <remarks>
/// <para><b>上游那两半对不上。</b>建根时 <c>PredictionModHookSubscriberCapture.EnumerateAuditableCards</c>
/// 只枚举战斗牌堆和跑局牌库里的牌，<c>PredictionModPatchAudit.CaptureCardOnPlay</c> 的注释也
/// 明写了「只在战斗中生成的牌类型在建根时看不到，这里不审」。但
/// <c>AdaptedOnPlaySnapshot.TryInvoke</c> 对**任何**不在审计表里的类型直接抛
/// <c>PredictionUnsupportedException</c>。</para>
///
/// <para>于是只要存在任何一条适配登记（也就是本适配层一加载），战斗里第一张生成牌被打出来
/// 就会 <c>搜索动作回放失败</c>。验收用例 <c>RS-INFINITE-BLADES-HAND-SIZE</c> 撞的就是这个：
/// 无限刀刃生成的 <c>SHIV</c> 在第二回合被打出，整场算不出来。</para>
///
/// <para><b>回退的判据。</b>上游那一抛是有道理的——没审过的牌可能挂着第三方补丁，那时按原版
/// 语义算就是静默算错。所以这里不是无条件放行，而是**只在这张牌的 <c>OnPlay</c> 上确实
/// 一个第三方补丁都没有时**才回退到普通镜像表。没有第三方补丁，原版镜像本来就是对的。
/// 有的话仍然让它抛，保持上游的安全边界。</para>
///
/// <para>这一条应该回报上游。修好之前本地留着。</para>
/// </remarks>
internal static class AdaptedSnapshotFallbackPatch
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const string TargetName = nameof(AdaptedOnPlaySnapshot.TryInvoke);

    private static readonly string[] TrustedAssemblies =
        [PinnedTargets.RebalancedSpireModId, Entry.ModId];

    public static MethodInfo ResolveTarget()
        => AccessTools.Method(typeof(AdaptedOnPlaySnapshot), TargetName)
           ?? throw new MissingMethodException(nameof(AdaptedOnPlaySnapshot), TargetName);

    /// <summary>自检用：确认那本审计表还找得到，而且还是个字典。</summary>
    public static string? Probe()
        => FindSelections(null) == null
            ? "求解器 AdaptedOnPlaySnapshot 里找不到那本按类型索引的审计表。"
            : null;

    public static bool Prefix(
        AdaptedOnPlaySnapshot __instance,
        PredictedCard card,
        ref MirrorDispatchResult result,
        ref bool __result)
    {
        Type cardType = card.Preview.GetType();
        if (FindSelections(__instance) is not { } selections || selections.Contains(cardType))
            return true;

        // 审过的牌照常走上游那条路；到这里说明这张牌建根时根本不存在（战斗中生成的）。
        MethodInfo? onPlay = AccessTools.Method(
            cardType, "OnPlay", [typeof(PlayerChoiceContext), typeof(CardPlay)]);
        if (onPlay == null)
            return true;
        if (Harmony.GetPatchInfo(onPlay) is { } patches && HasForeignPatch(patches))
            return true; // 挂着第三方补丁，照上游的判断抛，不要在这里放行。

        result = default;
        __result = false; // 交回给普通镜像表。
        return false;
    }

    private static bool HasForeignPatch(Patches patches)
        => patches.Prefixes
            .Concat(patches.Postfixes)
            .Concat(patches.Transpilers)
            .Concat(patches.Finalizers)
            .Any(patch =>
            {
                string? owner = patch.PatchMethod.DeclaringType?.Assembly.GetName().Name;
                return owner == null
                    || !TrustedAssemblies.Contains(owner, StringComparer.OrdinalIgnoreCase);
            });

    /// <summary>
    /// 找到快照里那本 <c>Dictionary&lt;Type, Registration?&gt;</c>。
    /// </summary>
    /// <remarks>
    /// 它是主构造函数捕获的参数，字段名由编译器生成（形如 <c>&lt;selections&gt;P</c>），
    /// 写死名字太脆。这里按「键类型是 Type 的字典」找，唯一一本。
    /// 传 <c>null</c> 时只做结构探测，不取值。
    /// </remarks>
    private static IDictionary? FindSelections(AdaptedOnPlaySnapshot? snapshot)
    {
        foreach (FieldInfo field in typeof(AdaptedOnPlaySnapshot).GetFields(Instance))
        {
            if (!typeof(IDictionary).IsAssignableFrom(field.FieldType))
                continue;
            Type[] args = field.FieldType.GetGenericArguments();
            if (args.Length != 2 || args[0] != typeof(Type))
                continue;
            if (snapshot == null)
                return EmptyProbe;
            return field.GetValue(snapshot) as IDictionary;
        }
        return null;
    }

    private static readonly IDictionary EmptyProbe = new Dictionary<Type, object?>();
}
