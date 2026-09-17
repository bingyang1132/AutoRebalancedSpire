# 发布清单

状态记于 2026-09-16。勾掉的是已完成，`BLOCK` 是硬阻塞。格式照 AutoWatcher 那份。

## 已定下来的

| 项 | 值 |
|---|---|
| 中文名 | 平衡尖塔适配 |
| 英文名 | AutoRebalancedSpire |
| `mod id` / 程序集 / 命名空间 / mods 目录 | `AutoRebalancedSpire` |
| 创意工坊标题 | `平衡尖塔适配 \| AutoRebalancedSpire` |
| GitHub 仓库 | `bingyang1132/AutoRebalancedSpire` |
| 授权 | MIT |
| 依赖 | 自动战斗求解器、平衡尖塔（ty / Oroboro）、RitsuLib |

「平衡尖塔」不是自译，是它自己 pck 里的 `mod.title`。

## 1.0.0

### 代码与覆盖

- [x] 被换掉打出效果的 33 张牌全部登记进 `AdaptedCardOnPlayMirrors`。
- [x] 普查（`docs/coverage-gaps.md`）列出的缺口全部补完：
      缺口 1（放行补丁挂错方法，会拒绝整场战斗）2026-09-14 修；
      十条静默算错 2026-09-14 补完；
      缺口 2（`ToItsOriginOwner.OnPlay`，多尼斯异鸟蛋打出后异鸟退场）2026-09-16 修。
- [x] 剩下的唯一一处是门扉缔造者，**声明不做**，理由写在
      `Entry.WarnAboutUnadaptedContent` 的注释里，加载时提醒玩家一次。
- [x] 注释和文档里的自译名全部按 pck 的官方译名改过一遍（约 250 行，四十余个名字）。
      文案里每出现一个新名字都要重新核一次，读法见 `publish/steam-description.md` 的写作约定 5。

### 验收

- [x] 夹具 30 条（`tools/run-rebalanced-matrix.ps1`）。
- [x] 本轮新增的两条各做过正反对照：`RS-LONG-DISTANCE-CARD-PLAYED-MIRROR`、
      `RS-BYRDONIS-EGG-RETURN`。
- [x] **全量 30 条在 0.40.1 上跑过一轮**（2026-09-16 22:05–22:41，约 36 分钟）：
      **29 通过、1 未通过**。未通过的是 `RS-KIN-SUMMON-HP`，查下来是夹具自己的问题不是镜像问题
      —— harness 的 `-EnemyCurrentHp` 默认是 1，那一场第一回合就打完了（`combatEndedTurn=1`），
      第 2 回合不存在，断言无从谈起。补上 `-EnemyCurrentHp 999` 后正向通过，
      注掉信徒血量缩放的反向对照如期挂掉。**改完只单独重跑了这一条，没有再跑一轮全量。**
- [ ] **来生的动态变量没有回归夹具。** 求解器不打不减掉血的牌，而奥斯提满血时它的治疗量是 0
      收益；无头 harness 的奥斯提旋钮又只在怪物招式检查那套里，且把最大生命和当前生命设成同
      一个值。这一处的证据只有三份问题包的数值对照和反编译。详见 `docs/coverage-gaps.md` §十一。
- [ ] 实机验证本轮三处修复：来生的动态变量、遥远距离涨层、胧光怪召唤的寄生惧魔（夹具只压住
      进场那半，复活扣 4 力量那半没覆盖）。

### 清单（`AutoRebalancedSpire.json`）

- [x] `id` = `AutoRebalancedSpire`，`name` = `平衡尖塔适配 | AutoRebalancedSpire`
- [x] `affects_gameplay: false` —— 这条要保持
- [x] `version` = `1.0.0`
- [x] `CombatSolver.min_version` = `0.38.2`，和 `PinnedTargets.CombatSolverMinimumVersion` 一致。
      **0.38.2 是结构下限不是验证下限**：`AdaptedCardOnPlayMirrors` 从那一版起进发布产物，
      本适配层引用到的其他求解器 API 在那一版也都在（逐个 `git grep v0.38.2` 核过）。
      实际验证过的只有 0.40.1。按 AutoWatcher 的先例，没有结构理由不抬门槛。
- [x] `RebalancedSpire.min_version` = `v0.3.10-beta`，和 `PinnedTargets` 与 csproj 的哈希同一版

### 打包

- [x] **图标和创意工坊预览图**（作者自制，2026-09-17）。`publish/icon.png` 1254×1254；
      预览图由它缩成 512×512（`publish/image.png`，502 KB）。Steam 上限 1 MB，640 缩图是
      761 KB、也在限内，选 512 是跟 AutoWatcher 保持一致并多留余量。
- [x] `publish/steam-description.md`（中英两份 BBCode 正文，含写作约定）
- [x] `publish/build-workshop-json.py` + `publish/workshop.json`。
      正文由脚本生成，别手工改 JSON。依赖三个 item id 都已填：
      RitsuLib `3747602295`、平衡尖塔 `3747498062`、求解器 `3790899961`
- [x] 上传工作区 `ModUploader-win-x64/AutoRebalancedSpireWorkshop/`：
      `workshop.json` + `content/`（`AutoRebalancedSpire.dll`、`AutoRebalancedSpire.json`、
      `THIRD_PARTY_NOTICES.md`、`image.png`）
- [x] 首次上传完成（2026-09-17）：**item id `3803035225`**，`visibility` 是 `private`，
      `mod_id.txt` 已在工作区，**别删** —— 以后更新靠它认条目。
      条目页 https://steamcommunity.com/sharedfiles/filedetails/?id=3803035225
      预览图、中英两份标题描述、三个依赖都上了。
- [ ] 作者自己订阅装一遍确认能加载，确认了把 `publish/workshop.json` 的 `visibility`
      改成 `public` 重传一次。

### GitHub 仓库

- [x] 面向用户的 `README.md`（中文）和 `README.en.md`（英文），互相链接
- [x] `LICENSE`（MIT）
- [x] `THIRD_PARTY_NOTICES.md`：publicizer 的用法，以及对 sts2 / CombatSolver /
      RebalancedSpire / RitsuLib 四个程序集的引用关系
- [x] `.gitignore` 复核：`local.props`、`bin`、`obj`、`.godot` 不进仓库；
      仓库里没有任何第三方二进制。（原来还忽略了 `**/publish/`，会把发布资料一起挡掉，已删）
- [x] 远端仓库 https://github.com/bingyang1132/AutoRebalancedSpire 已推（默认分支 `main`，
      本地分支从 `master` 改名成 `main` 对齐）。**目前是 private** —— 工坊正文里给了这个链接，
      发布前要改成 public，否则订阅者点过去是 404。
- [ ] tag `v1.0.0` 与 GitHub release。**`gh release create` 会被本地权限策略拦下，
      这一步每版都要交给作者自己跑**（AutoWatcher 1.0.4 那次撞过）。

## 下一版要记得的事

- `src/AdaptedSnapshotFallbackPatch.cs` 是等上游 [CombatSolver#105](https://github.com/Torch1230/CombatSolver/pull/105)
  的本地绕法。那个 PR 合并并发版之后可以整份删掉，并把求解器最低版本抬到那一版。
- 工坊的 `changeNote` 是 BBCode 不是 markdown。写 `**粗体**` 会原样显示出星号。
- 抬求解器最低版本之前，先查工坊上的求解器到了哪一版；抬到工坊还没有的版本会把订阅用户全挡在外面。
