# AutoRebalancedSpire

[简体中文](README.md)

Makes the **Slay the Spire 2 Combat Solver** work with **RebalancedSpire**.

With RebalancedSpire installed, a deck holding any of the 33 cards it replaces makes the solver
refuse the whole fight at root capture — it plans nothing at all. This mod restores it.
It changes no game behaviour and no numbers (`affects_gameplay` is `false` in the manifest).

## Requirements

| Dependency | Version | Where |
|---|---|---|
| Combat Solver | **0.38.2 or newer** | [Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3790899961) / [GitHub](https://github.com/Torch1230/CombatSolver) |
| RebalancedSpire (by ty / Oroboro) | **v0.3.10-beta** | [Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3747498062) |
| RitsuLib | 0.6.0+ | [Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3747602295) |

**When versions do not match:**

- Solver below 0.38.2, or missing a registration entry point → this mod **refuses to load
  cleanly** and says in the log what is missing. The solver then stops at its own third-party
  check, exactly as if this mod were not installed.
- A different RebalancedSpire build → this mod still loads, but warns that it is running against
  a build nobody verified card by card. **Take that warning seriously**: every changed card is a
  Harmony prefix that replaces `OnPlay` outright, so a new build means new implementations, and
  the structural self-check cannot catch a numeric drift.

## Two release tracks

The solver is under active development and this adapter follows it. It is written against the
solver's internal interfaces, so a solver change can require an adapter change.

| Track | Targets | Where |
|---|---|---|
| **Workshop** | the solver's **released** builds | subscribe |
| **GitHub** | one specific solver version, possibly a development build | [Releases](https://github.com/bingyang1132/AutoRebalancedSpire/releases) |

## Coverage

- All **33 cards whose OnPlay was replaced** are registered through the solver's adapted-OnPlay
  entry point, each written against the decompiled implementation.
- **32 of the 33 new Powers** are mirrored or confirmed not to need mirroring.
- **26 renamed monster moves**, plus 10 move implementations that would otherwise be computed
  silently wrong.
- **Relics**: Booming Conch, Diamond Diadem, Crossbow, Choices Paradox, Fiddle, History Course,
  Whispering Earring.
- **Potions, status cards, afflictions, enchantments, new cards**: the three teas, both rewrites
  of Wither, four new afflictions, two new enchantments, Corpse Explosion and Limit Break.
- **Global rules**: Plating decay, dynamic max hand size, turn-end retain and discard choices.

## Known gap: Doormaker

The act 3 boss **Doormaker** that RebalancedSpire adds is not adapted. While "closed" it sets its
own max and current HP to 999999999 and blocks targeting with a fake health bar, then moves its
parked powers back when it opens. The solver has no concept of a fake health bar; mirroring this
would need an HP-mask mechanism inside the solver itself, not a patch bolted on from outside.

**That fight is unusable, not wrong**: the solver marks the moves unsupported (red text) and does
not produce a plausible-looking bad route. Turning "Doormaker" off in RebalancedSpire's settings
removes the gap entirely. This mod only says so once at load; it does not change your settings.

## Verification

`tools/run-rebalanced-matrix.ps1` holds 30 end-to-end fixtures running on the solver's own
headless harness. Every case must fail unless the mirror is right: arithmetic where arithmetic
works (block values, the exact lethal turn), otherwise "prediction reused on turn N with zero
unexpected replans" or "zero unmirrored effects on the initial route".

## Licence

MIT (see [LICENSE](LICENSE)). Third-party assembly relationships are documented in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). No third-party binaries are included in this
repository.
