using System.Reflection;
using System.Text;
using CombatSolver;
using CombatSolver.Engine.Common.Mirrors;
using CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace AutoRebalancedSpire;

/// <summary>一张牌的 OnPlay 往哪里登记。33 条登记语句共用这一个接口，换实现不用改它们。</summary>
internal interface IOnPlayRegistrar
{
    void Register<TCard>(Action<TCard, CardOnPlayMirrorContext> handler) where TCard : CardModel;
}

/// <summary>
/// 把一张牌的 OnPlay 登记进求解器的**适配入口** <c>AdaptedCardOnPlayMirrors</c>，
/// 同时照旧登记进普通镜像表。
/// </summary>
/// <remarks>
/// <para><b>为什么要有这个类。</b>求解器建根时会审牌组：哪张牌的 <c>OnPlay</c> 上挂着第三方
/// Harmony 补丁，就抛 <c>IncompatibleGameplayModException</c>，整场战斗不规划。求解器
/// <c>0.38.2</c> 起开了正式的第三方入口——在 <c>AdaptedCardOnPlayMirrors</c> 里登记过的牌
/// 不再被拒，而且出牌时**整条配方由我们接管**（<c>CardEffectSpecRegistry.Apply</c> 那层
/// 按原版语义写的补偿不再跑）。</para>
///
/// <para><b>这替掉了什么。</b>在那之前我们只能打补丁：给 <c>PredictionModPatchAudit</c> 加一个
/// 前缀，把已镜像的牌从待审名单里摘掉。那个前缀挂在 <c>ValidateCardOnPlay</c> 上，而
/// <c>0.38.2</c> 之后实机根本不走那个方法（它只剩一行转调，只有测试还在用），
/// 实机走的是 <c>PredictionModHookSubscriberCapture.Capture</c> → <c>CaptureCardOnPlay</c>。
/// 于是放行名单等于不存在，33 张改动牌任意一张进场都会被拒——2026-09-14 那份化石追踪者的
/// 问题包就是这么来的。<b>方法名还在、但没人调它了</b>，按方法名做的自检抓不到这种漂移。</para>
///
/// <para><b>补丁组合是现场读的，不是写死的。</b>上游要求登记时声明这张牌 <c>OnPlay</c> 上
/// 期望的整套补丁组合（种类、方法、owner、优先级、before/after），建根时对不上就抛。
/// 我们在加载时从 <c>Harmony.GetPatchInfo</c> 把当下的组合读出来当作声明。这样能抓住的是
/// <b>加载之后到进战斗之前组合变了</b>；抓不住的是「换了一版 RebalancedSpire，补丁改了名或多了一条」
/// ——那种要靠 <see cref="AdapterSelfCheck"/> 里的核对基准版本来管。组合指纹会打进日志，
/// 换版本重新核对时可以直接比。</para>
///
/// <para><b>只认可信程序集。</b>组合里出现第三方补丁就不登记这张牌，让求解器照常拒绝整场战斗。
/// 两个 mod 同时改同一张牌时谁先跑不确定，我们的镜像未必对得上，必须停在门口。</para>
/// </remarks>
internal sealed class AdaptedOnPlayRegistrar(
    MethodMirrorRegistry<CardModel, CardOnPlayMirrorContext> ordinary) : IOnPlayRegistrar
{
    private const string Schema = "AutoRebalancedSpire/OnPlay/v1";

    private static readonly string[] TrustedAssemblies =
        [PinnedTargets.RebalancedSpireModId, Entry.ModId];

    private readonly StringBuilder _fingerprint = new();

    /// <summary>登记进了适配入口的牌。没进去的说明组合里有第三方补丁。</summary>
    public List<Type> Adapted { get; } = [];

    /// <summary>组合里出现了第三方补丁、因而没有登记适配入口的牌。</summary>
    public List<string> Rejected { get; } = [];

    public void Register<TCard>(Action<TCard, CardOnPlayMirrorContext> handler) where TCard : CardModel
    {
        // 普通镜像表照旧登记：CardOnPlayMirrors.CanMirror 读的是它，
        // 而且适配入口万一没命中时还有一条正确的退路。
        ordinary.Register(handler);

        MethodInfo target = AdaptedCardOnPlayMirrors.ResolveOnPlay(typeof(TCard))
            ?? throw new MissingMethodException(typeof(TCard).FullName, "OnPlay");
        if (!TryDescribeComposition(target, out List<AdaptedOnPlayPatch> patches, out string? rejection))
        {
            Rejected.Add($"{typeof(TCard).Name}：{rejection}");
            return;
        }

        AdaptedCardOnPlayMirrors.Register(Schema, target, patches, handler);
        Adapted.Add(typeof(TCard));
        Append(typeof(TCard), patches);
    }

    /// <summary>把这一轮登记的补丁组合压成一个短指纹，换 RebalancedSpire 版本时用来比。</summary>
    public string Fingerprint()
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(_fingerprint.ToString()));
        return Convert.ToHexString(hash)[..12];
    }

    private void Append(Type cardType, List<AdaptedOnPlayPatch> patches)
    {
        _fingerprint.Append(cardType.FullName).Append('');
        foreach (AdaptedOnPlayPatch patch in patches)
        {
            _fingerprint.Append(patch.Kind).Append('')
                .Append(patch.Method.DeclaringType?.FullName).Append('.').Append(patch.Method.Name)
                .Append('').Append(patch.Owner)
                .Append('').Append(patch.Priority).Append('');
        }
    }

    private static bool TryDescribeComposition(
        MethodInfo target, out List<AdaptedOnPlayPatch> patches, out string? rejection)
    {
        patches = [];
        rejection = null;

        Patches? info = Harmony.GetPatchInfo(target);
        if (info == null)
        {
            rejection = "OnPlay 上读不到补丁信息";
            return false;
        }

        foreach ((HarmonyPatchType kind, Patch[] group) in Groups(info))
        {
            foreach (Patch patch in group)
            {
                string? owner = patch.PatchMethod.DeclaringType?.Assembly.GetName().Name;
                if (owner == null || !TrustedAssemblies.Contains(owner, StringComparer.OrdinalIgnoreCase))
                {
                    rejection = $"OnPlay 上有来自 {owner ?? "未知程序集"} 的补丁";
                    return false;
                }
                patches.Add(new AdaptedOnPlayPatch(
                    kind, patch.PatchMethod, patch.owner, patch.priority, patch.before, patch.after));
            }
        }

        if (patches.Count == 0)
        {
            rejection = "OnPlay 上一个补丁都没有";
            return false;
        }
        return true;
    }

    private static IEnumerable<(HarmonyPatchType Kind, Patch[] Patches)> Groups(Patches info)
    {
        yield return (HarmonyPatchType.Prefix, info.Prefixes.ToArray());
        yield return (HarmonyPatchType.Postfix, info.Postfixes.ToArray());
        yield return (HarmonyPatchType.Transpiler, info.Transpilers.ToArray());
        yield return (HarmonyPatchType.Finalizer, info.Finalizers.ToArray());
    }

    /// <summary>自检用：确认求解器带着适配入口。</summary>
    public static MethodInfo ResolveRegisterTarget()
        => typeof(AdaptedCardOnPlayMirrors)
               .GetMethods(BindingFlags.Public | BindingFlags.Static)
               .FirstOrDefault(method => method.Name == nameof(AdaptedCardOnPlayMirrors.Register)
                                         && method.IsGenericMethodDefinition)
           ?? throw new MissingMethodException(
               nameof(AdaptedCardOnPlayMirrors), nameof(AdaptedCardOnPlayMirrors.Register));
}
