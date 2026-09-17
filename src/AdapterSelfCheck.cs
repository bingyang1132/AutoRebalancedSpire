using System.Reflection;
using System.Text;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using RebalancedSpire.Core.Configs;

namespace AutoRebalancedSpire;

/// <summary>
/// 加载时的结构自检。缺什么就干净地拒绝加载，不要半套。
/// </summary>
/// <remarks>
/// 半套适配比不适配糟得多：放行了牌却没有镜像，求解器会按原版语义给出一条看起来可信、实机
/// 根本不会那样打的路线。所以这里任何一条不过，整个适配层都不注册，也不装放行补丁 ——
/// 那时求解器会照常停在第三方 mod 检查上，玩家看到的是「不兼容」，而不是错的路线。
/// </remarks>
internal static class AdapterSelfCheck
{
    public readonly record struct Result(bool Ok, string Detail);

    public static Result Run()
    {
        if (PinnedTargets.SolverAssembly is not { } solver)
            return new Result(false, "没有找到战斗路线求解器（CombatSolver）。");
        if (PinnedTargets.RebalancedSpireAssembly is not { } rebalanced)
            return new Result(false, "没有找到 RebalancedSpire。");

        Version? solverVersion = solver.GetName().Version;
        if (solverVersion == null || solverVersion < PinnedTargets.CombatSolverMinimumVersion)
            return new Result(false,
                $"求解器版本 {solverVersion?.ToString() ?? "未知"} 低于下限 "
                + $"{PinnedTargets.CombatSolverMinimumVersion}。请更新求解器。");

        try
        {
            _ = AdaptedOnPlayRegistrar.ResolveRegisterTarget();
        }
        catch (MissingMethodException ex)
        {
            return new Result(false, $"求解器缺少 OnPlay 的第三方适配入口：{ex.Message}。");
        }

        try
        {
            _ = CalculatedVarPatch.ResolveTarget();
            _ = OnPlayCompensationPatch.ResolveTarget();
            _ = AfterEnergyResetLateDispatch.ResolveTarget();
            _ = PlatingDecayPatch.ResolveTarget();
            _ = CardEnteredCombatPatch.ResolveTarget();
            _ = RelicMirrors.ResolveGenerateTarget();
            _ = RelicMirrors.ResolveGeneratedToHandTarget();
            _ = RelicStatefulMirrors.ResolveParticipatingTarget();
            _ = RelicStatefulMirrors.ResolveHandDrawTarget();
            _ = RelicStatefulMirrors.ResolveTurnEndPowerTarget();
            _ = RelicStatefulMirrors.ResolvePrepareTurnEndTarget();
            _ = RelicStatefulMirrors.ResolveEnergyCostTarget();
            _ = RelicStatefulMirrors.ResolveStarCostTarget();
            _ = MonsterMirrors.ResolveApplyTarget();
            _ = PowerMirrors.ResolveHandDrawTarget();
            _ = EncounterPowerMirrors.ResolveEnergySpentTarget();
            _ = ExtraTurnPatch.ResolvePrepareTarget();
            _ = ExtraTurnPatch.ResolveLivePrepareTarget();
            _ = ExtraTurnPatch.ResolveConsumeTarget();
            _ = StringFieldPolicyPatch.ResolveTarget();
            _ = BranchConditionalPatch.ResolveTarget();
            _ = BloatSpawnPatch.ResolveTarget();
            _ = AdaptedSnapshotFallbackPatch.ResolveTarget();
            _ = HiddenDaggersShivPatch.ResolveTarget();
            _ = PowerAmountChangedDispatch.ResolveTarget();
            _ = DeathPowerRetentionPatch.ResolveTarget();
        }
        catch (MissingMethodException ex)
        {
            return new Result(false, $"求解器的内部方法找不到了：{ex.Message}。换了求解器版本要重新核对。");
        }

        try
        {
            _ = RebalancedSpireSettingsStore.Settings;
        }
        catch (Exception ex)
        {
            return new Result(false, $"读不到 RebalancedSpire 的设置：{ex.Message}。");
        }

        if (RegistryOverride.Probe(CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay.CardOnPlayMirrors.Registry)
            is { } probeError)
            return new Result(false, probeError);

        if (AdaptedSnapshotFallbackPatch.Probe() is { } snapshotError)
            return new Result(false, snapshotError);

        if (ValidateMirroredCards() is { } cardError)
            return new Result(false, cardError);

        var detail = new StringBuilder();
        detail.Append("求解器 ").Append(solverVersion)
            .Append("，RebalancedSpire ").Append(rebalanced.GetName().Version?.ToString() ?? "未知版本")
            .Append("（核对基准 ").Append(PinnedTargets.VerifiedRebalancedSpireVersion).Append("）。");
        return new Result(true, detail.ToString());
    }

    /// <summary>
    /// 每一张我们打算镜像的牌，此刻必须真的挂着 RebalancedSpire 的 <c>OnPlay</c> 补丁。
    /// </summary>
    /// <remarks>
    /// 挂不上说明那张牌的改动在这一版被删了或改名了 —— 那时我们的镜像是过期的，不能登记。
    /// 这一条抓的正是「换了 RebalancedSpire 版本却没重新核对」这种情况。
    /// </remarks>
    private static string? ValidateMirroredCards()
    {
        foreach (MirroredCard card in CardMirrors.All())
        {
            MethodInfo? onPlay = AccessTools.Method(
                card.CardType, "OnPlay", [typeof(PlayerChoiceContext), typeof(CardPlay)]);
            if (onPlay == null)
                return $"{card.CardType.Name} 没有 OnPlay(PlayerChoiceContext, CardPlay)。";

            Patches? patches = Harmony.GetPatchInfo(onPlay);
            bool patchedByRebalancedSpire = patches != null && patches.Prefixes
                .Concat(patches.Postfixes)
                .Concat(patches.Transpilers)
                .Concat(patches.Finalizers)
                .Any(patch => string.Equals(
                    patch.PatchMethod.DeclaringType?.Assembly.GetName().Name,
                    PinnedTargets.RebalancedSpireModId,
                    StringComparison.OrdinalIgnoreCase));
            if (!patchedByRebalancedSpire)
                return $"{card.CardType.Name} 上没有 RebalancedSpire 的 OnPlay 补丁，"
                    + "本适配层对着的那一版改动可能已经不在了。";
        }
        return null;
    }
}
