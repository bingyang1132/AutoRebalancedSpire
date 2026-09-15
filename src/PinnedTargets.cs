using System.Reflection;

namespace AutoRebalancedSpire;

/// <summary>
/// 钉死的依赖版本与核对基准。
/// </summary>
/// <remarks>
/// 逐张牌核对是在 RebalancedSpire v0.3.10-beta（Workshop 3747498062，
/// DLL SHA256 cce7a199…dcd2b）上做的。它的每一张改动牌都是一个 Harmony 前缀，
/// 直接替换 <c>OnPlay</c>，所以换版本必须重新核对——数值漂移结构自检抓不到。
/// </remarks>
internal static class PinnedTargets
{
    public const string RebalancedSpireModId = "RebalancedSpire";

    public const string VerifiedRebalancedSpireVersion = "v0.3.10-beta";

    /// <summary>求解器的程序集版本下限。低于这一版没有本适配需要的登记入口。</summary>
    /// <remarks>
    /// <c>0.38.2</c> 是第一个把 <c>AdaptedCardOnPlayMirrors</c> 放进发布产物的版本
    /// （上游 <c>bce222b</c>，PR #87）。本适配层的 33 张改动牌全部登记在那个入口上。
    ///
    /// <para>下限从 <c>0.36.0</c> 抬到这里，是因为同一个提交把实机建根审查从
    /// <c>PredictionModPatchAudit.ValidateCardOnPlay</c> 改走
    /// <c>PredictionModHookSubscriberCapture.Capture</c> → <c>CaptureCardOnPlay</c>。
    /// 装在 <c>0.38.2</c> 以下的求解器上，新的登记入口根本不存在。</para>
    /// </remarks>
    public static readonly Version CombatSolverMinimumVersion = new(0, 38, 2, 0);

    public static Assembly? SolverAssembly => FindAssembly("CombatSolver");

    public static Assembly? RebalancedSpireAssembly => FindAssembly(RebalancedSpireModId);

    private static Assembly? FindAssembly(string name)
        => AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(
                assembly.GetName().Name, name, StringComparison.OrdinalIgnoreCase));
}
