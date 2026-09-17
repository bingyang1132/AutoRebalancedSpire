# AutoRebalancedSpire · 平衡尖塔适配

[简体中文](README.md)

Makes the **Slay the Spire 2 combat route solver** work with **RebalancedSpire**.

With RebalancedSpire installed, a deck holding any of the 33 cards it changes makes the solver
refuse to plan the fight at all. With this mod installed the solver works as usual. It changes no
game behaviour and no numbers (the manifest sets `affects_gameplay` to `false`).

## Requirements

| Dependency | Version | Where |
|---|---|---|
| Combat Solver | **0.38.2 or newer** | [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3790899961) / [GitHub](https://github.com/Torch1230/CombatSolver) |
| RebalancedSpire (by ty / Oroboro) | **v0.3.10-beta** | [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3747498062) |
| RitsuLib | 0.6.0 or newer | [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3747602295) |

**What happens on a version mismatch:**

- Solver older than 0.38.2, or missing a registration point → this mod **refuses to load cleanly**
  and logs what is missing. The solver then stops at its own third-party check, as it would
  without this mod.
- A different RebalancedSpire build → this mod still loads, but the log says that this build is
  not the one that was verified card by card. **Take that seriously**: every card it changes is a
  patch that replaces the play effect outright, so a new build means new implementations, and the
  structural self-check cannot catch a numeric or semantic drift.

## Two release channels

**The solver is under active development, and this mod will keep being updated alongside it.** The
adapter is written against the solver's internal interfaces, so when the solver changes the adapter
may have to be brought back in line. Hence two channels:

| Channel | Targets | Where |
|---|---|---|
| **Steam Workshop** | *released* solver builds (0.38.2 and up) | [subscribe](https://steamcommunity.com/sharedfiles/filedetails/?id=3803035225) |
| **GitHub** | one specific solver version, possibly a development build | [Releases](https://github.com/bingyang1132/AutoRebalancedSpire/releases) |

Torch's Slay the Spire 2 modding group (Chinese-language, QQ): 1106541324

When something breaks, check which pair you have installed first.

## What is covered

- **All 33 cards whose play effect was replaced**, mirrored one by one against the decompiled
  implementation rather than guessed from the card text
- **The four that make you pick cards** are real search branches: Seance, Heirloom Hammer, Hand
  Trick and Hidden Daggers — the solver searches which cards to take
- **Changed monster moves**: of the 26 given new ids, every one that carries an effect is taken
  over, plus 10 whose numbers or effects were rewritten — The Kin, Fabricator, Soul Nexus, The
  Insatiable, The Obscura, Decimillipede and Phrog Parasite among them
- **32 of the 33 new powers**, plus 4 new afflictions, 2 new enchantments, 2 new cards and the
  three new teas
- **7 in-combat relics**: Booming Conch, Diamond Diadem, Crossbow, Choices Paradox, Fiddle,
  History Course, Whispering Earring
- **Global rules**: Plating decay, dynamic max hand size, turn-end retain and discard choices

## Known gaps

One of them:

- **Doormaker**, the act 3 boss RebalancedSpire adds. While closed it blocks targeting with a fake
  health bar, and the solver has no concept of a fake health bar — following it would mean
  changing the solver itself. It **never silently miscalculates**: the solver marks its moves
  unsupported and prints red text instead of producing a plausible-looking bad route. Turning
  Doormaker off in RebalancedSpire's settings keeps it out of the act 3 boss pool and removes the
  gap entirely.

This mod says so once at load; it does not change your settings.

The full per-category audit (cards, monster moves, powers, relics, potions / status cards /
afflictions / enchantments / new cards) is in [docs/coverage-gaps.md](docs/coverage-gaps.md), with
a consequence rating on every line.

## FAQ

**I installed this mod and the solver still says it found an incompatible third-party mod.**
The adapter did not load. The `[AutoRebalancedSpire]` lines in the log say why; the usual cause is
a solver older than 0.38.2. The adapter is all or nothing: if its assumptions do not hold it
registers nothing at all — letting a card through without a mirror is far worse than refusing the
fight.

**Why does it need solver 0.38.2 or newer?**
The 33 changed cards have to be registered through the solver's adapted-OnPlay entry point, which
is how the solver knows someone replaced a card's implementation *and* wrote a mirror for the new
one. That entry point (`AdaptedCardOnPlayMirrors`) first shipped in `0.38.2`.

**Will it still work after RebalancedSpire updates?**
Every card it changes is a patch that replaces the play effect outright, so a new build means new
implementations. This mod is pinned to v0.3.10-beta and says so in the log when it sees anything
else. If you see that line and a route looks wrong, please report it — I follow their versions.

**What happens if I turn one of RebalancedSpire's cards off in its settings?**
Off means that card is back to vanilla, and the solver computes vanilla, which is correct. But the
switches are read once when the game starts: if you flip one mid-session, this mod notices the
mismatch and withholds the card, so the solver stops at its third-party check instead of planning
with a stale mirror. Restart the game after changing settings.

**Can I run the Workshop build and the GitHub build together?**
No. Nothing fails silently if you do — the structural self-check refuses to load on a mismatch —
but there is no reason to. When something breaks, check which pair you have first.

## Reporting bugs

The solver exports bug packages. Attach one to an
[issue](https://github.com/bingyang1132/AutoRebalancedSpire/issues) along with what you saw. Bug
reports welcome!

## Building from source

```bash
dotnet build AutoRebalancedSpire.csproj -c Release
```

The build copies the DLL and manifest into `<game>/mods/AutoRebalancedSpire/`. It needs
`mods/CombatSolver/CombatSolver.dll` and `mods/RebalancedSpire/RebalancedSpire.dll` to be present;
drop a `local.props` next to the csproj to override the paths. No third-party binaries are included
in this repository.

The acceptance matrix is `tools/run-rebalanced-matrix.ps1` (30 end-to-end fixtures on the solver's
own headless harness). **It kills the running game process**, so do not run it while playing.

## Credits and licence

- RebalancedSpire by **ty** and **Oroboro**
- Combat Solver by **Torch1230** and the solver's contributors
- Thanks to **Claude (Anthropic)** for development help

MIT licensed, see [LICENSE](LICENSE). Third-party assembly relationships are documented in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
