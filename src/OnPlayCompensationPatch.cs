using System.Reflection;
using HarmonyLib;
using CombatSolver;
using CombatSolver.Engine.Common;

namespace AutoRebalancedSpire;

/// <summary>
/// 我们接管了 <c>OnPlay</c> 的牌，要把求解器那一层「补偿」也一起关掉。
/// </summary>
/// <remarks>
/// 求解器打一张牌时做两件事：先跑 <c>CardOnPlayMirrors</c> 的镜像，**然后无条件**再跑一遍
/// <c>CardOnPlaySupport.Apply</c> —— 那里面是三张按牌型写死的表（施加 Power、生成牌、
/// 其余零碎），专门补镜像不管的那部分。
///
/// 对被 RebalancedSpire 换过实现的牌，这一层补偿是**按原版语义写的**，结果有两种：
/// <list type="bullet">
///   <item>轻的是重复结算 —— 比如永恒护甲，镜像给一次镀甲，补偿再给一次。</item>
///   <item>重的是直接抛异常 —— 纺纱的变量已经从 <c>SpinnerPower</c> 换成了
///     <c>SpinnerPlusPower</c>，补偿那句按老键取值，<c>KeyNotFound</c> 直接把整条搜索打断。
///     验收矩阵第一次跑就是这么挂的。</item>
/// </list>
/// 所以只要这张牌在我们的名单里，就整段跳过：我们的镜像写的是改版 <c>OnPlay</c> 的完整语义，
/// 本来就不需要补。
/// </remarks>
internal static class OnPlayCompensationPatch
{
    private const string TargetName = nameof(CardOnPlaySupport.Apply);

    public static MethodInfo ResolveTarget()
        => AccessTools.Method(typeof(CardOnPlaySupport), TargetName)
           ?? throw new MissingMethodException(nameof(CardOnPlaySupport), TargetName);

    public static bool Prefix(PredictedCard playedCard)
        => !MirroredCards.StillMirrors(playedCard.Preview.GetType());
}
