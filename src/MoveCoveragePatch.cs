using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using CombatSolver;
using RebalancedSpire.Core.Configs;

namespace AutoRebalancedSpire;

/// <summary>
/// 让求解器认得改版**新加的招式**，并多捕获几个新招式要读的怪物静态值。
/// </summary>
/// <remarks>
/// 求解器判断一条出招能不能模拟，看的是 <c>MonsterMoveEffects.Supports</c> 里那张写死的
/// 「怪物类型名 + 招式 id」表。改版给 26 条出招换了新 id（拜尔多尼斯的发怒、信众祭司整套、
/// 寄生蛙的增殖二三、组装师的逃跑……），这些 id 不在表里，于是整条出招被标成「不支持」，
/// 红字提示、路线不敢用。招式本身的效果在 <see cref="MonsterMirrors"/> 里已经逐条镜像了，
/// 这里只是把它们补进那张表。
///
/// 三件事一起做：
/// <list type="bullet">
///   <item><c>Supports</c>：告诉求解器这几条我们接管了。</item>
///   <item><c>RemovesOwner</c>：逃跑类的出招要让预测停止继续推这只怪的后续回合。</item>
///   <item><c>CaptureStaticIntValues</c>：新招式要读的怪物字段不在求解器的捕获清单里。
///     按它自己的办法在建根时捕获，而不是在工作线程上去读实机模型。</item>
/// </list>
///
/// 全部按开关门控：玩家把某只怪的改动关掉时，招式会退回原版实现，那时求解器原本的口径才是对的。
/// </remarks>
internal static class MoveCoveragePatch
{
    public static MethodInfo ResolveSupportsTarget()
        => AccessTools.Method(typeof(MonsterMoveEffects), nameof(MonsterMoveEffects.Supports))
           ?? throw new MissingMethodException(
               nameof(MonsterMoveEffects), nameof(MonsterMoveEffects.Supports));

    public static MethodInfo ResolveRemovesOwnerTarget()
        => AccessTools.Method(typeof(MonsterMoveEffects), nameof(MonsterMoveEffects.RemovesOwner))
           ?? throw new MissingMethodException(
               nameof(MonsterMoveEffects), nameof(MonsterMoveEffects.RemovesOwner));

    public static MethodInfo ResolveStaticCaptureTarget()
        => AccessTools.Method(
               typeof(MonsterMoveEffects),
               nameof(MonsterMoveEffects.CaptureStaticIntValues))
           ?? throw new MissingMethodException(
               nameof(MonsterMoveEffects), nameof(MonsterMoveEffects.CaptureStaticIntValues));

    public static void SupportsPostfix(MonsterModel monster, string moveId, ref bool __result)
    {
        if (__result)
            return;
        RebalancedSpireSettings settings = AdapterSettings.Current;
        __result = (monster.GetType().Name, moveId) switch
        {
            ("Aeonglass", "WITHERING_MOVE") => settings.Aeonglass,
            ("TestSubject", "GROWL_MOVE") => settings.TestSubject,
            ("MagiKnight", "PREP_2_MOVE") => settings.Knights,
            ("SoulNexus", "SOUL_MARK_MOVE") => settings.SoulNexus,
            ("Fabricator", "ESCAPE_MOVE") => settings.Fabricator,
            ("LivingShield", "SHIELD_UP_MOVE") => settings.TurretOperator,
            ("HunterKiller", "WEAK_GOOP_MOVE") => settings.HunterKiller,
            ("ThievingHopper", "ATTACK_MOVE") => settings.ThievingHopper,
            ("CeremonialBeast", "FIRST_STAMP_MOVE" or "SECOND_STAMP_MOVE") => settings.CeremonialBeast,
            ("KinFollower", "GUARD_MOVE" or "REVENGE_DANCE_MOVE" or "GUARD_FAKE_MOVE"
                or "POWER_DANCE_FAKE_MOVE" or "ESCAPE_MOVE") => settings.TheKin,
            ("KinPriest", "GUARD_MOVE" or "POWER_UP_MOVE" or "SHIELD_UP_MOVE"
                or "BREAK_UP_MOVE" or "HEAL_UP_MOVE") => settings.TheKin,
            ("Byrdonis", "ANGRY_MOVE") => settings.Byrdonis,
            ("PhrogParasite", "PROLIFERATION_MOVE" or "PROLIFERATION_2_MOVE"
                or "PROLIFERATION_3_MOVE" or "INFECT_2_MOVE") => settings.PhrogParasite,
            ("PunchConstruct", "FIGHT_WITH_ME") => settings.PunchOff,
            // 下面九条原版下求解器也不支持，补掉之后改版反而比原版算得准。
            ("BygoneEffigy", "SLASHES_MOVE") => settings.BygoneEffigy,
            ("Crusher", "ENLARGING_STRIKE_MOVE") => settings.KaiserCrab,
            ("Rocket", "TARGETING_RETICLE_MOVE" or "LASER_MOVE") => settings.KaiserCrab,
            ("DecimillipedeSegment", "BULK_MOVE") => settings.Decimillipede,
            ("SkulkingColony", "ZOOM_MOVE") => settings.SkulkingColony,
            ("SpectralKnight", "SOUL_FLAME" or "SOUL_SLASH") => settings.Knights,
            ("Vantom", "INK_BLOT_MOVE") => settings.Vantom,
            // 只换了 id、实现还是原版的四条。
            ("CeremonialBeast", "FIRST_PLOW_MOVE" or "SECOND_PLOW_MOVE") => settings.CeremonialBeast,
            ("DecimillipedeSegment", "CONSTRICT_MOVE" or "REATTACH_MOVE") => settings.Decimillipede,
            _ => false,
        };
    }

    public static void RemovesOwnerPostfix(MonsterModel monster, string moveId, ref bool __result)
    {
        if (__result)
            return;
        RebalancedSpireSettings settings = AdapterSettings.Current;
        __result = (monster.GetType().Name, moveId) switch
        {
            ("Fabricator", "ESCAPE_MOVE") => settings.Fabricator,
            ("KinFollower", "ESCAPE_MOVE") => settings.TheKin,
            _ => false,
        };
    }

    /// <summary>新招式要读、但求解器没捕获的怪物静态字段。</summary>
    /// <remarks>
    /// <c>Aeonglass.IncreasingIntensityTotalStrength</c> 本来就是原版字段，只是原版那一招
    /// 不需要它，求解器没列进捕获清单；改版的渐强要按它给力量。
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string[]> ExtraStaticIntMembers =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Aeonglass"] = ["IncreasingIntensityTotalStrength"],
            ["CeremonialBeast"] = ["MaxInitialHp"],
            ["PunchConstruct"] = ["FastPunchDamage"],
        };

    public static void StaticCapturePostfix(
        MonsterModel monster,
        ref IReadOnlyDictionary<string, int> __result)
    {
        if (!ExtraStaticIntMembers.TryGetValue(monster.GetType().Name, out string[]? members))
            return;
        Dictionary<string, int> merged = new(__result.Count + members.Length, StringComparer.Ordinal);
        foreach ((string key, int value) in __result)
            merged[key] = value;
        foreach (string member in members)
            merged[member] = MonsterValueReader.ReadInt(monster, member);
        __result = merged;
    }
}
