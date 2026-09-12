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
        int registeredOther;
        try
        {
            MethodMirrorRegistry<CardModel, CardOnPlayMirrorContext> onPlay = CardOnPlayMirrors.Registry;
            registered = MirroredCards.RegisterAll(onPlay);
            registeredPowers = PowerMirrors.RegisterAll();
            registeredOther = EnchantmentMirrors.RegisterAll()
                + AfflictionMirrors.RegisterAll()
                + MirroredCards.ReplaceHooks();
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
            // 求解器算计算变量时用的是它自己那张写死的乘数表，改版换了公式的牌要接管。
            harmony.Patch(
                CalculatedVarPatch.ResolveTarget(),
                prefix: new HarmonyMethod(typeof(CalculatedVarPatch), nameof(CalculatedVarPatch.Prefix)));
            // 镀甲的衰减规则改了：玩家首回合也减，但有永恒护甲时完全不减。
            harmony.Patch(
                PlatingDecayPatch.ResolveTarget(),
                prefix: new HarmonyMethod(typeof(PlatingDecayPatch), nameof(PlatingDecayPatch.Prefix)));
            // 钻石冠冕和轰鸣海螺整个换了机制：先把它们从求解器的回合开始名单里摘掉。
            harmony.Patch(
                RelicStatefulMirrors.ResolveParticipatingTarget(),
                postfix: new HarmonyMethod(
                    typeof(RelicStatefulMirrors), nameof(RelicStatefulMirrors.ParticipatingPostfix)));
            harmony.Patch(
                RelicStatefulMirrors.ResolveHandDrawTarget(),
                postfix: new HarmonyMethod(
                    typeof(RelicStatefulMirrors), nameof(RelicStatefulMirrors.HandDrawPostfix)));
            harmony.Patch(
                RelicStatefulMirrors.ResolveTurnEndPowerTarget(),
                postfix: new HarmonyMethod(
                    typeof(RelicStatefulMirrors), nameof(RelicStatefulMirrors.TurnEndPowerPostfix)));
            harmony.Patch(
                RelicStatefulMirrors.ResolvePrepareTurnEndTarget(),
                postfix: new HarmonyMethod(
                    typeof(RelicStatefulMirrors), nameof(RelicStatefulMirrors.PrepareTurnEndPostfix)));
            harmony.Patch(
                RelicStatefulMirrors.ResolveEnergyCostTarget(),
                prefix: new HarmonyMethod(
                    typeof(RelicStatefulMirrors), nameof(RelicStatefulMirrors.EnergyCostPrefix)));
            harmony.Patch(
                RelicStatefulMirrors.ResolveStarCostTarget(),
                prefix: new HarmonyMethod(
                    typeof(RelicStatefulMirrors), nameof(RelicStatefulMirrors.StarCostPrefix)));
            // 十字弩生成的牌、选择悖论的备选牌，求解器都写在遗物那个大 switch 里。
            harmony.Patch(
                RelicMirrors.ResolveGenerateTarget(),
                prefix: new HarmonyMethod(
                    typeof(RelicMirrors), nameof(RelicMirrors.GenerateRelicCardsPrefix)));
            harmony.Patch(
                RelicMirrors.ResolveGeneratedToHandTarget(),
                prefix: new HarmonyMethod(
                    typeof(RelicMirrors), nameof(RelicMirrors.ResolveGeneratedToHandPrefix)));
            // 牌进场时两个新病症要自查源头 Power 还在不在，求解器那个时点也是写死的 switch。
            harmony.Patch(
                CardEnteredCombatPatch.ResolveTarget(),
                postfix: new HarmonyMethod(
                    typeof(CardEnteredCombatPatch), nameof(CardEnteredCombatPatch.Postfix)));
            // 额外回合求解器只认遗物给的，Power 给的看不见。
            harmony.Patch(
                ExtraTurnPatch.ResolvePrepareTarget(),
                postfix: new HarmonyMethod(typeof(ExtraTurnPatch), nameof(ExtraTurnPatch.PreparePostfix)));
            harmony.Patch(
                ExtraTurnPatch.ResolveLivePrepareTarget(),
                postfix: new HarmonyMethod(typeof(ExtraTurnPatch), nameof(ExtraTurnPatch.PreparePostfix)));
            harmony.Patch(
                ExtraTurnPatch.ResolveConsumeTarget(),
                postfix: new HarmonyMethod(typeof(ExtraTurnPatch), nameof(ExtraTurnPatch.ConsumePostfix)));
            // 回合开始晚段求解器只跑一个遗物，Power 一个都不发，只能挂在它后面自己分发。
            harmony.Patch(
                AfterEnergyResetLateDispatch.ResolveTarget(),
                postfix: new HarmonyMethod(
                    typeof(AfterEnergyResetLateDispatch), nameof(AfterEnergyResetLateDispatch.Postfix)));
        }
        catch (Exception ex)
        {
            _logger.Error($"装放行补丁失败，求解器仍会拒绝带这些牌的战斗：{ex}");
            return;
        }

        _logger.Info($"已注册 {registered} 张 RebalancedSpire 改动牌、"
            + $"{registeredPowers + AfterEnergyResetLateDispatch.HandlerCount} 个新 Power、"
            + $"{registeredOther} 处附魔与钩子的镜像。{check.Detail}");
    }
}
