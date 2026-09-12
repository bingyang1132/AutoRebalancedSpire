using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using CombatSolver;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;
using RebalancedSpire.Core.Afflictions;
using RebalancedSpire.Core.Powers;

namespace AutoRebalancedSpire;

/// <summary>
/// 饥饿和审视施加/消失时，对**已经在场**的牌整批感染、整批清除。
/// </summary>
/// <remarks>
/// 这两个 Power 的 <c>AfterApplied</c> 会把持有者当时的每一张牌都感染一遍（饥饿跳过能力牌，
/// 审视来者不拒，两者都只碰身上还没有病症的牌），<c>AfterRemoved</c> 再把自己那种病症全部清掉。
/// <see cref="CardEnteredCombatPatch"/> 只管**新进场**的牌，管不到这两批。
///
/// 挂在 <c>SimulatedCombatState.NormalizeCardAfflictions</c> 后面，而不是去追 Power 的施加时点：
/// 求解器自己处理诅咒、缠绕、耳鸣就是这个办法 —— 不追事件，只在每个结算点把
/// 「牌上的病症」和「身上的 Power」重新对齐一次。施加和移除这样就一起覆盖了，
/// 而且分支合并、续接复用时也不会漏。
///
/// 一处已知的近似：两个 Power 同时在，其中一个被移除时，被清掉的那批牌在下一次归一化里会被
/// 另一个补上病症，实机不会（实机只在牌进场时感染）。求解器自己那条诅咒/缠绕/耳鸣的链就是
/// 这个形状，这里跟着它走，不另起一套。
///
/// 层数按 Power 当前层数给，和实机一致。这两种病症的层数本身不参与任何结算
/// （吞噬只是让饥饿给牌加「消耗」，称重只影响手牌上限），但层数进续接戳，
/// 对不上会让玩家每回合被强制重算。
/// </remarks>
internal static class PowerAfflictionPatch
{
    private const string TargetName = nameof(SimulatedCombatState.NormalizeCardAfflictions);

    public static MethodInfo ResolveTarget()
        => AccessTools.Method(typeof(SimulatedCombatState), TargetName)
           ?? throw new MissingMethodException(nameof(SimulatedCombatState), TargetName);

    public static void Postfix(
        SimulatedCombatState __instance,
        CombatPredictionSimulator simulator)
    {
        foreach (Player player in __instance.Players)
        {
            int hunger = __instance.GetAmount<HungerPower>(player.Creature);
            int scrutiny = __instance.GetAmount<ScrutinyPower>(player.Creature);
            if (hunger <= 0 && scrutiny <= 0 && !HasEitherAffliction(simulator, player))
                continue;

            foreach (PredictedCard card in simulator.State.GetPlayerCombatState(player).AllCards)
            {
                switch (card.Preview.Affliction)
                {
                    case Devoured when hunger <= 0:
                    case Weighted when scrutiny <= 0:
                        card.ClearAffliction();
                        break;
                    // 已经带着别的病症（包括我们刚补上的那一种），谁都不再覆盖。
                    case not null:
                        continue;
                }

                if (card.Preview.Affliction != null)
                    continue;
                if (hunger > 0 && card.Preview.Type != CardType.Power)
                    simulator.Afflict<Devoured>(card, hunger);
                else if (scrutiny > 0)
                    simulator.Afflict<Weighted>(card, scrutiny);
            }
        }
    }

    private static bool HasEitherAffliction(CombatPredictionSimulator simulator, Player player)
    {
        foreach (PredictedCard card in simulator.State.GetPlayerCombatState(player).AllCards)
        {
            if (card.Preview.Affliction is Devoured or Weighted)
                return true;
        }
        return false;
    }
}
