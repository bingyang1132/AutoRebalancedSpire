using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Modding;
using CombatSolver.Engine.Common.Mirrors;
using CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;
using STS2RitsuLib;

namespace AutoRebalancedSpire;

/// <summary>
/// 把 RebalancedSpire 改过的牌教给战斗路线求解器。本 mod 不改变任何游戏行为。
/// </summary>
/// <remarks>
/// 注册必须在任何一场战斗开始之前一次性同步做完。求解器的 <c>MethodMirrorRegistry</c> 会把
/// 查找结果缓存进快照，某个类型一旦被查过，之后再注册会被静默丢弃。
/// </remarks>
[ModInitializer(nameof(Initialize))]
public static class Entry
{
    public const string ModId = "AutoRebalancedSpire";

    private static Logger? _logger;

    public static void Initialize()
    {
        _logger = RitsuLibFramework.CreateLogger(ModId);

        AdapterSelfCheck.Result check = AdapterSelfCheck.Run();
        if (!check.Ok)
        {
            _logger.Error($"自检未通过，未注册任何镜像，求解器会照常停在第三方 mod 检查上。{check.Detail}");
            return;
        }

        int registered;
        int registeredPowers;
        try
        {
            MethodMirrorRegistry<CardModel, CardOnPlayMirrorContext> onPlay = CardOnPlayMirrors.Registry;
            registered = MirroredCards.RegisterAll(onPlay);
            registeredPowers = PowerMirrors.RegisterAll();
        }
        catch (Exception ex)
        {
            // 全有或全无：半套镜像配上放行补丁，等于让求解器拿着过期语义继续算。
            _logger.Error($"注册镜像时失败，未装放行补丁：{ex}");
            return;
        }

        try
        {
            var harmony = new Harmony(ModId);
            harmony.Patch(
                AuditFilter.ResolveTarget(),
                prefix: new HarmonyMethod(typeof(AuditFilter), nameof(AuditFilter.Prefix)));
        }
        catch (Exception ex)
        {
            _logger.Error($"装放行补丁失败，求解器仍会拒绝带这些牌的战斗：{ex}");
            return;
        }

        _logger.Info($"已注册 {registered} 张 RebalancedSpire 改动牌、{registeredPowers} 个新 Power 的镜像。{check.Detail}");
    }
}
