# Third-Party Notices · 第三方声明

Last updated: 2026-09-16

平衡尖塔适配（AutoRebalancedSpire）本身以 MIT 授权（见 `LICENSE`）。本文件说明它和四个第三方
程序集的关系。

AutoRebalancedSpire itself is MIT licensed (see `LICENSE`). This file documents its relationship
to four third-party assemblies.

## 本仓库不包含任何第三方二进制

No third-party binaries are included in this repository.

`sts2.dll`、`CombatSolver.dll`、`RebalancedSpire.dll`、`STS2-RitsuLib.dll` 全部在构建时从本机的
游戏安装目录读取（路径可由 `local.props` 覆盖），并且都以 `Private="false"` 引用——它们不会被
复制进构建产物，也不会随发布包分发。发布包只含 `AutoRebalancedSpire.dll`、
`AutoRebalancedSpire.json` 和本文件。

All four are read at build time from the local game installation (paths overridable in
`local.props`) and referenced with `Private="false"` — they are never copied into build output and
never redistributed. A release package contains only `AutoRebalancedSpire.dll`,
`AutoRebalancedSpire.json` and this file.

## 依赖

### Slay the Spire 2 · `sts2.dll`

- 版权归 Mega Crit。
- 仅作编译期引用。本 Mod 是非官方社区作品，与 Mega Crit 无关。

### Combat Solver（自动战斗求解器）· `CombatSolver.dll`

- 作者：Torch1230 及各位贡献者
- GitHub: https://github.com/Torch1230/CombatSolver
- 本 Mod 是它的适配层：向它的镜像注册表登记平衡尖塔改过的牌与效果，并在它没有注册表的位置
  用 Harmony 打补丁。运行期依赖，编译期引用。
- 求解器自身的第三方声明随求解器分发，不在本仓库重复。

### RebalancedSpire（平衡尖塔）· `RebalancedSpire.dll`

- 作者：ty / Oroboro
- Steam Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id=3747498062
- 本 Mod **不修改、不重分发**平衡尖塔的任何内容。它只读取平衡尖塔的类型与实现，在求解器一侧
  建立等价的模拟镜像。
- 核对是对着 v0.3.10-beta（DLL SHA256 `cce7a199…dcd2b`）逐张牌做的。构建期
  `VerifyRebalancedSpireHash` 只发警告不让构建失败，运行期 `AdapterSelfCheck` 兜底。
  钉死是为了正确性，不是授权限制：它的每一张改动牌都是一个直接替换 `OnPlay` 的 Harmony 前缀，
  一个没升版本号的实现改动会让某张牌变成「有镜像但算的是旧语义」，哈希是唯一能提示这种情况的
  检查。

### RitsuLib · `STS2-RitsuLib.dll`

- Steam Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id=3747602295
- 用于 mod 加载与日志。仅作编译期引用。

## 关于 publicizer

构建使用 `Krafs.Publicizer` 对 `sts2`、`CombatSolver`、`RebalancedSpire` 三个程序集放开内部成员
访问。这个过程只发生在**本机构建期**，产生的中间程序集写在 `obj/PublicizedAssemblies/` 下，被
`.gitignore` 排除，不进仓库也不进发布包。

- `CombatSolver`：适配层要登记进求解器的内部镜像注册表。
- `RebalancedSpire`：它的新 Power 和病症把状态放在内部成员上，镜像要读同一份状态。
- `sts2`：镜像要按原版的具体实现逐字对照。

The publicizer runs at build time on the developer's machine only. Its output lives under
`obj/PublicizedAssemblies/`, is git-ignored, and is neither committed nor redistributed.

## 美术资源

`publish/icon.png` 及由它派生的创意工坊图片为 bingyang1132 原创，随本仓库以 MIT 授权。

`publish/icon.png` and the Workshop images derived from it are original work by bingyang1132,
MIT licensed along with the rest of this repository.
