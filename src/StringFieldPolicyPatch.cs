using System.Collections.Concurrent;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using CombatSolver;

namespace AutoRebalancedSpire;

/// <summary>
/// 告诉求解器：改版新 Power 上那些字符串变量只是用来拼提示文字的，不参与结算。
/// </summary>
/// <remarks>
/// 求解器给每个模型的动态变量算指纹时，遇到 <c>StringVar</c> 会去问
/// <c>SemanticStateFieldPolicy.ClassifyString</c> 这个字段算不算「影响结算」。那是一张按
/// **(类型, 字段名)** 写死的白名单，认不出来就**抛异常**——上游这么写是为了自己加新 Power
/// 时不会漏分类，但对第三方 Power 来说，后果是整场战斗直接算不出来。
///
/// 这就是「感染棱柱识别不了」的真正原因，而且不止那一场。改版有七个新 Power 带字符串变量，
/// 每一个都会让对应的那场战斗炸掉：
///
/// <list type="bullet">
///   <item>污染+（感染棱柱精英）</item>
///   <item>守护（信众）</item>
///   <item>寄生+（寄生蛙精英）</item>
///   <item>吸取拥抱（黏液狂战士）</item>
///   <item>长距离（贪食者）</item>
///   <item>乒乓（活体迷雾）</item>
///   <item>枯魂（魂枢）</item>
/// </list>
///
/// 这七个字段全是「某张牌／某只怪的名字」，拿去填提示文字用的，一个都不参与结算 ——
/// 和上游自己已经列进白名单的 <c>VitalSparkPower.AfflictionTitle</c>、
/// <c>GalvanicPower.AfflictionTitle</c> 是同一类东西。
///
/// 名单外的字段：如果来自 RebalancedSpire，**按只用于显示处理并记一条警告**，不跟着抛。
/// 判断依据是这类变量的用途 —— 字符串在这套模型里只进本地化插值，没有任何结算读它；
/// 而抛出去的代价是整场战斗用不了。真出现一个靠字符串驱动结算的 Power，警告会把它露出来。
/// 不是 RebalancedSpire 的字段一律放行给上游，该抛还是抛。
/// </remarks>
internal static class StringFieldPolicyPatch
{
    private const string TargetName = nameof(SemanticStateFieldPolicy.ClassifyString);

    /// <summary>逐个读过源码确认只用于显示的字段。</summary>
    private static readonly HashSet<(string Type, string Field)> KnownPresentationOnly =
    [
        ("TaintedPlusPower", "AfflictionTitle"),
        ("GuardPower", "MasterName"),
        ("InfestedPlusPower", "PhrogParasite"),
        ("LeechingHugPower", "Slimed"),
        ("LeechingHugPower", "SlimedBerserker"),
        ("LongDistancePower", "TheInsatiable"),
        ("PingPongPower", "LivingFog"),
        ("SoulWitherPower", "SoulNexus"),
    ];

    private static readonly ConcurrentDictionary<(string, string), bool> Warned = new();

    private static Logger? _logger;

    public static void Initialize(Logger logger) => _logger = logger;

    public static MethodInfo ResolveTarget()
        => AccessTools.Method(typeof(SemanticStateFieldPolicy), TargetName)
           ?? throw new MissingMethodException(nameof(SemanticStateFieldPolicy), TargetName);

    public static bool Prefix(Type modelType, string fieldName, ref SemanticStateFieldRole __result)
    {
        if (!string.Equals(
                modelType.Assembly.GetName().Name,
                PinnedTargets.RebalancedSpireModId,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!KnownPresentationOnly.Contains((modelType.Name, fieldName))
            && Warned.TryAdd((modelType.Name, fieldName), true))
        {
            _logger?.Warn(
                $"RebalancedSpire 的 {modelType.Name}.{fieldName} 是个没核对过的字符串变量，"
                + "已按「只用于显示」处理。如果它其实影响结算，求解器的局面等价判断会漏掉它 —— "
                + "请把这个字段报回来。");
        }

        __result = SemanticStateFieldRole.PresentationOnly;
        return false;
    }
}
