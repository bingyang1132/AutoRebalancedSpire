using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Modding;
using CombatSolver.Engine.Common.Mirrors;
using CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;
using STS2RitsuLib;
using RebalancedSpire.Core.Configs;

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

        // 开关的缓存要在任何补丁装上去之前建好：热路径上每读一次未缓存的开关
        // 都会新建一个缓存对象并挂一个事件订阅，见 AdapterSettings 的说明。
        AdapterSettings.Initialize();

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
            WhisperingEarringPatch.RegisterState();
            registeredOther = EnchantmentMirrors.RegisterAll()
                + AfflictionMirrors.RegisterAll()
                + MonsterMirrors.RegisterAll()
                + OrbPowerMirrors.RegisterAll()
                + NewCardMirrors.RegisterAll()
                + EncounterPowerMirrors.RegisterAll()
                + TaintedPlusMirrors.RegisterAll()
                + MonsterReactionMirrors.RegisterAll()
                + PotionMirrors.RegisterAll()
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
            // 改版新 Power 上的字符串变量：求解器那张分类白名单认不出来就抛，
            // 一抛整场战斗就算不出来。七个新 Power 都带，七场战斗都会中招。
            StringFieldPolicyPatch.Initialize(_logger);
            harmony.Patch(
                StringFieldPolicyPatch.ResolveTarget(),
                prefix: new HarmonyMethod(
                    typeof(StringFieldPolicyPatch), nameof(StringFieldPolicyPatch.Prefix)));
            harmony.Patch(
                AuditFilter.ResolveTarget(),
                prefix: new HarmonyMethod(typeof(AuditFilter), nameof(AuditFilter.Prefix)));
            // 我们接管了 OnPlay 的牌，求解器那一层按原版语义写的「补偿」要一起关掉。
            harmony.Patch(
                OnPlayCompensationPatch.ResolveTarget(),
                prefix: new HarmonyMethod(
                    typeof(OnPlayCompensationPatch), nameof(OnPlayCompensationPatch.Prefix)));
            // 求解器算计算变量时用的是它自己那张写死的乘数表，改版换了公式的牌要接管。
            harmony.Patch(
                CalculatedVarPatch.ResolveTarget(),
                prefix: new HarmonyMethod(typeof(CalculatedVarPatch), nameof(CalculatedVarPatch.Prefix)));
            // 镀甲的衰减规则改了：玩家首回合也减，但有永恒护甲时完全不减。
            harmony.Patch(
                PlatingDecayPatch.ResolveTarget(),
                prefix: new HarmonyMethod(typeof(PlatingDecayPatch), nameof(PlatingDecayPatch.Prefix)));
            // 低语耳环整个换了触发条件：原版第一回合连打，改版改成攒满 13 点能量后的下一张牌。
            harmony.Patch(
                WhisperingEarringPatch.ResolveTriggerTarget(),
                prefix: new HarmonyMethod(
                    typeof(WhisperingEarringPatch), nameof(WhisperingEarringPatch.TriggerPrefix)));
            harmony.Patch(
                WhisperingEarringPatch.ResolveEnergySpentTarget(),
                postfix: new HarmonyMethod(
                    typeof(WhisperingEarringPatch), nameof(WhisperingEarringPatch.EnergySpentPostfix)));
            harmony.Patch(
                WhisperingEarringPatch.ResolveCardPlayedLateTarget(),
                postfix: new HarmonyMethod(
                    typeof(WhisperingEarringPatch), nameof(WhisperingEarringPatch.CardPlayedLatePostfix)));
            // 战史课程重放的范围从「攻击」放宽到「攻击或技能」。
            harmony.Patch(
                HistoryCoursePatch.ResolveRecordTarget(),
                prefix: new HarmonyMethod(
                    typeof(HistoryCoursePatch), nameof(HistoryCoursePatch.RecordPrefix)));
            harmony.Patch(
                HistoryCoursePatch.ResolveLookupTarget(),
                prefix: new HarmonyMethod(
                    typeof(HistoryCoursePatch), nameof(HistoryCoursePatch.LookupPrefix)));
            // 流星锤每次自己飞回手里伤害永久 +3；回手那一半求解器本来就模拟。
            harmony.Patch(
                BolasIncrementPatch.ResolveTarget(),
                prefix: new HarmonyMethod(typeof(BolasIncrementPatch), nameof(BolasIncrementPatch.Prefix)),
                postfix: new HarmonyMethod(typeof(BolasIncrementPatch), nameof(BolasIncrementPatch.Postfix)));
            // 知识恶魔三选一里崩解的层数从 6/7/8 改成了 4/6/8。
            harmony.Patch(
                KnowledgeCursePatch.ResolveTarget(),
                prefix: new HarmonyMethod(typeof(KnowledgeCursePatch), nameof(KnowledgeCursePatch.Prefix)),
                postfix: new HarmonyMethod(typeof(KnowledgeCursePatch), nameof(KnowledgeCursePatch.Postfix)));
            // 寄生蛙精英死后生几只蠕虫由「寄生+」的层数决定，求解器认不出这个新 Power。
            harmony.Patch(
                DeathSpawnPatch.ResolveSpawnsPrimaryTarget(),
                postfix: new HarmonyMethod(
                    typeof(DeathSpawnPatch), nameof(DeathSpawnPatch.SpawnsPrimaryPostfix)));
            harmony.Patch(
                DeathSpawnPatch.ResolveTriggerTarget(),
                postfix: new HarmonyMethod(
                    typeof(DeathSpawnPatch), nameof(DeathSpawnPatch.TriggerPostfix)));
            // 改版新加的招式 id 求解器那张表里没有，不补会被整条标成「不支持」。
            harmony.Patch(
                MoveCoveragePatch.ResolveSupportsTarget(),
                postfix: new HarmonyMethod(
                    typeof(MoveCoveragePatch), nameof(MoveCoveragePatch.SupportsPostfix)));
            harmony.Patch(
                MoveCoveragePatch.ResolveRemovesOwnerTarget(),
                postfix: new HarmonyMethod(
                    typeof(MoveCoveragePatch), nameof(MoveCoveragePatch.RemovesOwnerPostfix)));
            harmony.Patch(
                MoveCoveragePatch.ResolveStaticCaptureTarget(),
                postfix: new HarmonyMethod(
                    typeof(MoveCoveragePatch), nameof(MoveCoveragePatch.StaticCapturePostfix)));
            // 被换掉实现的敌人招式：求解器那张「怪物 + 招式 id」的大表不是注册表。
            harmony.Patch(
                MonsterMirrors.ResolveApplyTarget(),
                prefix: new HarmonyMethod(typeof(MonsterMirrors), nameof(MonsterMirrors.ApplyPrefix)));
            harmony.Patch(
                EncounterPowerMirrors.ResolveEnergySpentTarget(),
                postfix: new HarmonyMethod(
                    typeof(EncounterPowerMirrors), nameof(EncounterPowerMirrors.EnergySpentPostfix)));
            harmony.Patch(
                PowerMirrors.ResolveHandDrawTarget(),
                postfix: new HarmonyMethod(typeof(PowerMirrors), nameof(PowerMirrors.HandDrawPostfix)));
            // 周密计划+ 换成了「回合结束挑几张保留」，求解器整个 BeforeFlush 时点都没有。
            harmony.Patch(
                TurnEndRetainPatch.ResolveTarget(),
                postfix: new HarmonyMethod(
                    typeof(TurnEndRetainPatch), nameof(TurnEndRetainPatch.Postfix)));
            harmony.Patch(
                MonsterMirrors.ResolveReviveTarget(),
                postfix: new HarmonyMethod(typeof(MonsterMirrors), nameof(MonsterMirrors.RevivePostfix)));
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
            // 污染+ 的病症不是生命火花打的，别让求解器按原版口径当成残留清掉。
            harmony.Patch(
                TaintedPlusMirrors.ResolveNormalizeTarget(),
                prefix: new HarmonyMethod(
                    typeof(TaintedPlusMirrors), nameof(TaintedPlusMirrors.NormalizePrefix)),
                postfix: new HarmonyMethod(
                    typeof(TaintedPlusMirrors), nameof(TaintedPlusMirrors.NormalizePostfix)));
            // 侧回合结束：饥饿/审视的衰减、死神形态+ 提前收割末日。
            harmony.Patch(
                SideTurnEndDispatch.ResolveTarget(),
                postfix: new HarmonyMethod(
                    typeof(SideTurnEndDispatch), nameof(SideTurnEndDispatch.Postfix)));
            // 饥饿/审视施加与消失时，对已经在场的牌整批感染、整批清除。
            harmony.Patch(
                PowerAfflictionPatch.ResolveTarget(),
                postfix: new HarmonyMethod(
                    typeof(PowerAfflictionPatch), nameof(PowerAfflictionPatch.Postfix)));
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
            // 手牌上限求解器建根时冻结；改版有两个会在战斗中变的来源。
            harmony.Patch(
                MaxHandSizePatch.ResolveCaptureTarget(),
                postfix: new HarmonyMethod(
                    typeof(MaxHandSizePatch), nameof(MaxHandSizePatch.CapturePostfix)));
            harmony.Patch(
                MaxHandSizePatch.ResolveMaxHandSizeTarget(),
                postfix: new HarmonyMethod(
                    typeof(MaxHandSizePatch), nameof(MaxHandSizePatch.MaxHandSizePostfix)));
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

        WarnAboutUnadaptedContent();
    }

    /// <summary>本适配层覆盖不到的那部分内容，加载时提醒一次。</summary>
    /// <remarks>
    /// 门匠（Doormaker）是 RebalancedSpire 新加的第三章 Boss，不是对原版内容的改动，
    /// 本适配层没有为它写模拟。它「关着」的时候会把自己的最大和当前生命都设成 999999999、
    /// 用假血条挡住选中，开门时再把暂存的 Power 搬回来 —— 求解器没有「血条是假的」这个概念，
    /// 要镜像得先在求解器里造一套生命遮罩机制，不是适配层能钉在外面的补丁。
    ///
    /// 求解器遇到它会把出招标成「不支持」（红字），不会给出看似可信的错路线，所以这不是安全
    /// 问题，只是那一场用不了。建议在 RebalancedSpire 的设置里把「门匠」关掉：关掉之后那个
    /// Boss 不进第三章的 Boss 池，连带的随机目标改写和「全能」也一起不生效，整块空白就没了。
    ///
    /// 这里只提醒，不替玩家改设置 —— 本 mod 声明了 <c>affects_gameplay: false</c>，
    /// 自己去动别人的开关会让这句话变成假的。
    /// </remarks>
    private static void WarnAboutUnadaptedContent()
    {
        if (!AdapterSettings.Current.Doormaker)
            return;
        _logger?.Warn(
            "RebalancedSpire 的「门匠」Boss 当前是开着的，本适配层没有为它写模拟。"
            + "遇到那一场时求解器会把出招标成不支持（红字），不会给错路线，但那一场用不了。"
            + "建议在 RebalancedSpire 的设置里关掉「Doormaker」。");
    }
}
