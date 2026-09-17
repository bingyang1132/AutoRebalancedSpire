# 创意工坊页面文案

创意工坊的标题和正文是**分语言**的：Steam 的 `SetItemUpdateLanguage` 可以给同一个条目上传
多份语言版本，玩家看到哪一份由他自己的 Steam 语言决定。所以这里中英各写一份**纯**该语言的
正文，不再中英混排。

- 简体中文（`schinese`）：下面「## 正文 · 简体中文」一节
- English（`english`，同时作为其他语言的回退）：下面「## 正文 · English」一节

改完跑 `python publish/build-workshop-json.py` 生成 `publish/workshop.json`，别手工改 JSON。

正文是 BBCode，不是 markdown —— 写 `**粗体**` 会原样显示出来，要写 `[b]粗体[/b]`。

## 写作约定

和自动观者那份同一套，照抄过来，免得两个 Mod 的页面长得不像一家的：

1. **正文只说玩家能感觉到的结果，不解释机制。** 开头两句话说完「这是什么、解决什么、不改什么」
   就够了。`affects_gameplay`、「补上模拟镜像」这类实现说法不进正文。
2. **能靠常见问题承载的，正文不重复。**
3. **常见问题只放玩家真会遇到、而且需要自己动手的问题。** 设计理由和开发史不进对外文案，
   它们的位置在 `docs/coverage-gaps.md`。
4. **已知缺口要点名具体是哪一处**，玩家才知道自己会不会碰到。只给数字没有用。
5. **玩家看得见的每一个名字都必须是游戏里的官方译名**，从 pck 的 `localization/zhs/*.json`
   核过再写。这一条在本 Mod 上踩过一次，一口气错了四十多个：往世／长距离／恐惧蛛／仓皇逃窜／
   黏液狂战士／魂枢／寄生棱镜／迷雾 全是自译，官方是 来生／遥远距离／寄生惧魔／狂乱逃离／
   史莱姆狂战士／灵魂枢纽／感染棱柱／活雾。
   读法见 `CombatSolver/tools/read-game-localization.ps1`；平衡尖塔自己的名字在
   `mods/RebalancedSpire/RebalancedSpire.pck`，键前缀 `RebalancedSpire/localization/zhs/`，
   它的 `mod.title` 就是「平衡尖塔」。

英文正文照中文的结构和取舍走，不要自己多加段落。

## 标题

```
平衡尖塔适配 | AutoRebalancedSpire
```

中英两版共用这一个标题。

## 正文 · 简体中文

---

让[b]杀戮尖塔2自动战斗求解器[/b]适用于[b]平衡尖塔[/b]。

装了平衡尖塔之后，你的牌组里只要有它改过的 33 张牌里的任意一张，求解器就会拒绝规划整场战斗。装上本 Mod 之后求解器照常工作。不改变任何游戏行为和数值。

[h2]需要装什么[/h2]

[list]
[*]自动战斗求解器，[b]至少 0.38.2[/b]
[*]平衡尖塔（作者 ty / Oroboro），[b]v0.3.10-beta[/b]
[*]RitsuLib
[/list]

[h2]求解器还在持续开发，本 Mod 也会相应持续更新[/h2]

适配层是贴着求解器的内部接口写的，求解器一改，适配层就可能得跟一次。所以本 Mod 分两条线发布：

[list]
[*][b]创意工坊版[/b]，也就是你现在看的这个 —— 对标求解器的[b]发布版[/b]。订阅即可，普通情况用这个就行。
[*][b]GitHub 版[/b] —— 对标某一个确定的求解器版本，可能是还没发布的开发版。每个 release 会写明它对标哪一个求解器版本并给出链接。
[/list]

本 Mod 的 GitHub：https://github.com/bingyang1132/AutoRebalancedSpire
求解器的创意工坊：https://steamcommunity.com/sharedfiles/filedetails/?id=3790899961
求解器的 GitHub：https://github.com/Torch1230/CombatSolver

自动求解器作者Torch的塔2mod交流群：1106541324 （QQ）

[h2]覆盖了什么[/h2]

[list]
[*][b]被换掉打出效果的 33 张牌[/b]逐张核对镜像，按反编译出的实现写，不是照着卡面文字猜
[*][b]带选牌的那几张[/b]是真正的搜索分支：降灵、传承之锤、手上技法、隐秘匕首，选哪几张由求解器自己搜
[*][b]怪物招式[/b]：改版换了 id 的 26 条里，有效果的都接管了；另外 10 条数值或效果被改写、不跟就会算错的也补齐了
[*][b]平衡尖塔新加的 Power[/b] 33 个里 32 个有镜像，新病症、新附魔、新牌、三瓶新茶也都覆盖了
[*][b]7 个战斗内遗物[/b]：轰鸣海螺、钻石头冠、十字弓、选择悖论、小提琴、历史课、低语耳环
[*][b]全局规则[/b]：覆甲衰减、会变的手牌上限、回合结束的保留与弃牌选择
[/list]

[h2]有一处没适配：门扉缔造者[/h2]

平衡尖塔新加的第三章 Boss[b]门扉缔造者[/b]没有适配。它关着门的时候用一条假血条挡住选中，求解器没有「血条是假的」这个概念，要跟上得先改求解器本身。

[b]那一场会用不了，但不会算错[/b]：求解器把它的出招标成「不支持」，只打红字，不会给一条看起来可信的错路线。建议在平衡尖塔的设置里关掉[b]门扉缔造者[/b]，关掉之后它不进第三章 Boss 池，这块空白就没了。

[h2]常见问题[/h2]

[b]装了本 Mod，求解器还是停在「检测到不兼容的第三方 Mod」？[/b]
说明适配层没有加载。日志里 AutoRebalancedSpire 开头那几行会写明原因，最常见的是求解器版本低于 0.38.2。适配层是全有或全无：前提不成立就一个镜像都不注册，绝不装一半。

[b]平衡尖塔更新之后还能用吗？[/b]
它的每一张改动牌都是直接替换打出效果的补丁，换一版就等于换一份实现。所以本 Mod 钉的是 v0.3.10-beta，对着别的版本运行时日志里会写「这一份平衡尖塔不是逐条核对过的那一版」。看到那句话又觉得路线不对，请报 Bug，我会跟版本。

[b]在平衡尖塔的设置里把某张牌关掉了，求解器会怎么算？[/b]
关掉等于那张牌回到原版语义，求解器按原版算，是对的。但开关是在进游戏时读一次的：中途改了开关，本 Mod 会发现登记时的状态和现在对不上，于是不放行——求解器停在第三方检查上，而不是拿着一份过期的镜像继续算。改完开关重开游戏即可。

[h2]报 Bug[/h2]

求解器自带问题包导出。导出以后可以发到 GitHub Issues，附上看到的现象。或者在交流群讨论。欢迎捉虫！

[h2]致谢[/h2]

平衡尖塔作者 ty 和 Oroboro；自动战斗求解器作者 Torch1230 及各位贡献者。
感谢 Claude（Anthropic）在开发上的帮助。本 Mod 以 MIT 授权。

## 正文 · English

---

Makes the [b]Slay the Spire 2 combat route solver[/b] work with [b]RebalancedSpire[/b].

With RebalancedSpire installed, a deck holding any of the 33 cards it changes makes the solver refuse to plan the fight at all. With this mod installed the solver works as usual. It changes no game behaviour and no numbers.

[h2]Requirements[/h2]

[list]
[*]Combat Solver, [b]0.38.2 or newer[/b]
[*]RebalancedSpire (by ty / Oroboro), [b]v0.3.10-beta[/b]
[*]RitsuLib
[/list]

[h2]The solver is under active development, and so is this mod[/h2]

This adapter is written against the solver's internal interfaces, so a solver change can require an adapter change. It therefore ships on two tracks:

[list]
[*][b]Workshop[/b] — the one you are looking at. Targets the solver's [b]released[/b] builds. Just subscribe; this is the one you want.
[*][b]GitHub[/b] — targets one specific solver version, possibly an unreleased development build. Every release states which solver version it targets and links to it.
[/list]

This mod on GitHub: https://github.com/bingyang1132/AutoRebalancedSpire
The solver on the Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id=3790899961
The solver on GitHub: https://github.com/Torch1230/CombatSolver

[h2]Coverage[/h2]

[list]
[*][b]All 33 cards whose play effect was replaced[/b], each written against the decompiled implementation rather than guessed from the card text
[*][b]The ones that make you pick cards[/b] are real search branches: Seance, Heirloom Hammer, Hand Trick, Hidden Daggers — the solver searches which cards to pick
[*][b]Monster moves[/b]: of the 26 given new ids, every one that carries an effect is taken over; plus 10 whose numbers or effects were rewritten
[*][b]32 of the 33 new Powers[/b], plus the new afflictions, enchantments, cards and the three new teas
[*][b]7 in-combat relics[/b]: Booming Conch, Diamond Diadem, Crossbow, Choices Paradox, Fiddle, History Course, Whispering Earring
[*][b]Global rules[/b]: Plating decay, dynamic max hand size, turn-end retain and discard choices
[/list]

[h2]One thing is not adapted: Doormaker[/h2]

The act 3 boss [b]Doormaker[/b] that RebalancedSpire adds is not adapted. While closed it blocks targeting with a fake health bar, and the solver has no concept of a fake health bar — following it would mean changing the solver itself.

[b]That fight is unusable, but nothing is computed wrong[/b]: the solver marks its moves unsupported and prints red text instead of producing a plausible-looking bad route. Turning [b]Doormaker[/b] off in RebalancedSpire's settings keeps it out of the act 3 boss pool and removes the gap.

[h2]FAQ[/h2]

[b]I installed this mod and the solver still says it found an incompatible third-party mod.[/b]
The adapter did not load. The first few AutoRebalancedSpire lines in the log say why; the usual cause is a solver older than 0.38.2. The adapter is all or nothing: if its assumptions do not hold it registers nothing at all rather than half a mirror.

[b]Will it still work after RebalancedSpire updates?[/b]
Every card it changes is a patch that replaces the play effect outright, so a new build means new implementations. This mod is pinned to v0.3.10-beta; against any other build the log says that this RebalancedSpire is not the one that was verified card by card. If you see that line and a route looks wrong, please report it — I follow their versions.

[b]What happens if I turn one of RebalancedSpire's cards off in its settings?[/b]
Off means that card is back to vanilla, and the solver computes vanilla, which is correct. But the switches are read once when the game starts: if you flip one mid-session, this mod notices the mismatch and withholds the card, so the solver stops at its third-party check instead of planning with a stale mirror. Restart the game after changing settings.

[h2]Reporting bugs[/h2]

The solver exports bug packages. Attach one to a GitHub issue along with what you saw. Bug reports welcome!

[h2]Credits[/h2]

RebalancedSpire by ty and Oroboro; Combat Solver by Torch1230 and contributors.
Thanks to Claude (Anthropic) for development help. This mod is MIT licensed.
