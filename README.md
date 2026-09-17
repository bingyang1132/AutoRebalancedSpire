# 平衡尖塔适配 · AutoRebalancedSpire

[English](README.en.md)

让**杀戮尖塔2自动战斗求解器**适用于**平衡尖塔（RebalancedSpire）**。

装了平衡尖塔之后，牌组里只要有它改过的 33 张牌里的任意一张，求解器在建根时就会拒绝整场战斗，
一步都不规划。本 Mod 装上之后求解器照常工作。不改变任何游戏行为和数值
（清单里 `affects_gameplay` 是 `false`）。

## 需要装什么

| 依赖 | 版本要求 | 说明 |
|---|---|---|
| 自动战斗求解器（CombatSolver） | **至少 0.38.2** | [创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3790899961) ／ [GitHub](https://github.com/Torch1230/CombatSolver) |
| 平衡尖塔（RebalancedSpire，作者 ty / Oroboro） | **v0.3.10-beta** | [创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3747498062) |
| RitsuLib | 0.6.0 起 | [创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3747602295) |

**版本不对会怎样：**

- 求解器低于 0.38.2，或者缺了某个登记入口 → 本 Mod **干净地拒绝加载**，日志里写明缺什么。
  求解器会照常停在它自己那道第三方检查上，也就是回到没装本 Mod 的样子。
- 平衡尖塔换版本 → 本 Mod 仍然加载，但日志里会警告「正在对着一份没有逐条核对过的平衡尖塔运行」。
  **这个警告要当真**：它的每一张改动牌都是一个 Harmony 前缀，直接替换 `OnPlay`，
  换版本等于换实现，数值漂移结构自检抓不到。

## 两个版本

**求解器还在持续开发，本 Mod 也会相应持续更新。** 适配层是贴着求解器的内部接口写的，
求解器一改，适配层就可能得跟一次。所以这里分两条线发布：

| 版本 | 对标 | 从哪拿 |
|---|---|---|
| **创意工坊版** | 求解器的**发布版** | 订阅即可 |
| **GitHub 版** | 某一个确定的求解器版本，可能是开发版 | [Releases](https://github.com/bingyang1132/AutoRebalancedSpire/releases) |

## 覆盖了什么

- **被换掉打出效果的 33 张牌**全部登记进求解器的适配入口，按反编译出的实现逐张写。
  这 33 张里有 11 张是「原版效果 + 一个新 Power」，8 张是原版命令的重新组合，
  4 张带选牌分支（降灵、传承之锤、手上技法、隐秘匕首），4 张牵扯召唤和奥斯提。
- **33 个新 Power** 里 32 个有镜像或确认无需镜像。
- **26 条换了 id 的怪物招式**，外加 10 条原本会静默算错的招式数值。
- **遗物**：轰鸣海螺、钻石头冠、十字弓、选择悖论、小提琴、历史课、低语耳环。
- **药水、状态牌、病症、附魔、新加的牌**：三瓶新茶、凋萎的两处改写、四个新病症、
  两个新附魔、尸爆术与突破极限。
- **全局规则**：覆甲衰减、会变的手牌上限、回合结束的保留与弃牌选择。

## 已知缺口：门扉缔造者

平衡尖塔新加的第三章 Boss **门扉缔造者（Doormaker）** 没有适配。它「关着」的时候会把自己的
最大和当前生命都设成 999999999、用假血条挡住选中，开门时再把暂存的 Power 搬回来 ——
求解器没有「血条是假的」这个概念，要镜像得先在求解器里造一套生命遮罩机制，
不是适配层能钉在外面的补丁。

**后果是那一场用不了，不是算错**：求解器会把它的出招标成「不支持」（红字），
不会给出看起来可信的错路线。建议在平衡尖塔的设置里关掉「门扉缔造者」——关掉之后它不进第三章
Boss 池，连带的随机目标改写和「万物动力学」也一起不生效。本 Mod 只在加载时提醒一次，
不替你改设置。

## 验收

`tools/run-rebalanced-matrix.ps1` 是 30 条端到端夹具，跑在求解器自带的无头 harness 上。
每条都要求「只有镜像正确才能通过」：能用算术验的验算术（格挡数值、正好击杀的回合），
其余用「第 N 回合复用预测状态、意外重算 0 次」或者「初始路线未镜像效果 0 条」。

```powershell
pwsh -NoProfile -File tools\run-rebalanced-matrix.ps1 -Only RS-BYRDONIS-EGG-RETURN
```

**不要跑全量矩阵，除非你确实需要**：它会杀掉正在运行的游戏进程。

## 开发文档

- [docs/coverage-gaps.md](docs/coverage-gaps.md)：逐类普查（牌、怪物招式、Power、遗物、
  药水／状态牌／病症／附魔／新牌），每条标了后果分级，以及怎么重跑这次普查。
- [docs/monster-move-audit.md](docs/monster-move-audit.md)：怪物招式的脚本比对（已被上一份取代，
  冲突处以 `coverage-gaps.md` 为准）。
- [PLAN.md](PLAN.md)：这个适配层是怎么一步步做出来的。

## 授权

MIT（见 [LICENSE](LICENSE)）。第三方程序集的引用关系见
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。本仓库不包含任何第三方二进制。
