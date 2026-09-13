using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Card;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Damage;
using CombatSolver.Engine.InCombat.Simulation;
using RebalancedSpire.Core.Configs;
using RebalancedSpire.Core.Powers;

namespace AutoRebalancedSpire;

/// <summary>
/// 三个具体遭遇战里的新 Power：黏液狂战士的拥抱、魂枢的枯魂、永世沙漏的凋零之威。
/// </summary>
/// <remarks>
/// 剩下那些新 Power（门匠 Boss 的全能、织机的制造者、拜尔多尼斯的归还等）没有镜像，
/// 走求解器的未镜像风险 —— 它们的钩子都是动作类，没登记会记一条风险显示成红字，
/// 不会静默算错。
///
/// 取值类的钩子（伤害倍率、能不能被选中、费用修正）**通常**会回落到 Power 自己的实现，
/// 自动跟随改版 —— 但只在那份实现读的全是求解器喂给它的东西时才成立。
/// 实现里一旦去读实机模型（<c>CombatState.Enemies</c>、<c>Creature.IsAlive</c> 之类），
/// 它在搜索里看到的就是**做计划那一刻**的实机局面，整条计划都按那个局面算。
/// 组装师的减伤就是这么栽的，见 <see cref="MonsterReactionMirrors"/>。
/// </remarks>
internal static class EncounterPowerMirrors
{
    private const string EnergySpentName = nameof(PowerLifecycleSupport.AfterEnergySpent);

    public static MethodInfo ResolveEnergySpentTarget()
        => AccessTools.Method(typeof(PowerLifecycleSupport), EnergySpentName)
           ?? throw new MissingMethodException(nameof(PowerLifecycleSupport), EnergySpentName);

    public static int RegisterAll()
    {
        AfterCardPlayedMirrors.Registry.Register<LeechingHugPower>(LeechingHug);
        AfterDamageGivenMirrors.Registry.Register<SoulWitherPower>(SoulWither);
        PowerHiddenStateMirrors.Register<SoulWitherPower>(
            "HitCount",
            static (simulator, power) => Hits(simulator, power).Value);
        return 2;
    }

    /// <summary>吸取拥抱：玩家每打出一张黏液，所有黏液狂战士加力量并回血。</summary>
    /// <remarks>
    /// 治疗量按玩家人数放大，单人局就是一份。不镜像的话求解器会把「打黏液」当成纯粹的空过，
    /// 看不到它其实在养对面。
    /// </remarks>
    private static void LeechingHug(LeechingHugPower power, AfterCardPlayedMirrorContext context)
    {
        if (context.PreviewCard is not Slimed)
            return;
        if (context.CombatState is not SimulatedCombatState combat)
            return;

        int scale = combat.Players.Count;
        Creature applier = context.PreviewCard.Owner.Creature;
        foreach (Creature enemy in combat.Enemies.Where(static c => c.Monster is SlimedBerserker).ToArray())
        {
            combat.Apply<StrengthPower>(enemy, power.DynamicVars.Strength.IntValue, applier);
            if (context.Simulator.HasPendingChoice)
                return;
            context.Simulator.Heal(enemy, power.DynamicVars.Heal.BaseValue * scale);
        }
    }

    /// <summary>枯魂：挂着它的怪每打中玩家一次强化攻击就记一次，满 12 次它那边有别的用处。</summary>
    /// <remarks>
    /// 计数本身不改变结算，但它进指纹 —— 不记的话「已经打了 11 次」和「一次没打」会被当成
    /// 同一个局面，续接和剪枝都会错。
    /// </remarks>
    private static void SoulWither(SoulWitherPower power, AfterDamageGivenMirrorContext context)
    {
        if (context.Dealer != power.Owner
            || !context.Props.IsPoweredAttack()
            || context.Result.UnblockedDamage <= 0)
        {
            return;
        }
        if (context.Target.Player is null && context.Target.PetOwner is null)
            return;
        Hits(context.Simulator, power).Value++;
    }

    /// <summary>凋零之威：玩家每花掉一点能量就扣一点计量，扣到零就把手上的枯萎全部假升级一级。</summary>
    /// <remarks>
    /// 求解器这个时点（<c>PowerLifecycleSupport.AfterEnergySpent</c>）只处理原版的轨道能力，
    /// 第三方 Power 进不去，所以挂在后面。
    ///
    /// 计量本身是 Power 的普通动态变量，会进指纹，不用另开隐藏状态。
    /// 扣到零之后按原样「每补 12 点算一级」，补几级就把每张枯萎假升级几次。
    /// </remarks>
    public static void EnergySpentPostfix(
        CombatPredictionSimulator simulator,
        SimulatedCombatState combat,
        PredictedCard card,
        int amount)
    {
        if (amount <= 0 || !AdapterSettings.Current.Aeonglass)
            return;
        if (card.Preview.Type == CardType.Status)
            return;

        foreach (WitheringPresencePlusPower power in combat.EffectivePowers()
                     .OfType<WitheringPresencePlusPower>()
                     .ToArray())
        {
            if (power.Target?.Player != card.Preview.Owner)
                continue;

            power.DynamicVars.Energy.BaseValue -= amount;
            if (power.DynamicVars.Energy.IntValue > 0)
                continue;

            int levels = 0;
            while (power.DynamicVars.Energy.IntValue <= 0)
            {
                levels++;
                power.DynamicVars.Energy.BaseValue += 12m;
            }

            foreach (PredictedCard candidate in simulator.State
                         .GetPlayerCombatState(card.Preview.Owner).AllCards.ToArray())
            {
                if (candidate.MutablePreview is not Wither wither)
                    continue;
                for (int i = 0; i < levels; i++)
                    wither.FakeUpgrade();
            }
        }
    }

    /// <summary>枯魂已经记了多少次。<see cref="BranchConditionalPatch"/> 判分支也要读它。</summary>
    internal static CounterPredictionState Hits(CombatPredictionSimulator simulator, PowerModel power)
        => simulator.StateStore.Get(power, () => new CounterPredictionState(power.DisplayAmount));
}
