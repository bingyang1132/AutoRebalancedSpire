# 适配覆盖普查（2026-09-14）

一次把 RebalancedSpire 改了什么、我们镜像了什么、差在哪里全部枚举一遍，而不是一条一条补。
纯静态分析：反编译 + 读源码，没有运行游戏，也没有跑 harness。

## 核对基准

| 东西 | 版本 | 说明 |
| --- | --- | --- |
| RebalancedSpire | v0.3.10-beta，DLL SHA256 `cce7a199…dcd2b` | 和 `PinnedTargets.VerifiedRebalancedSpireVersion` 一致 |
| CombatSolver | 0.38.6（仓库分支 `local/deploy-0386`） | 问题包 `environment.json` 里也是 0.38.6.0 |
| 游戏 | v0.111.0（public-beta） | |
| 适配层 | AutoRebalancedSpire 0.1.0.0 | 问题包里确实加载了 |

枚举出来的量：RebalancedSpire 一共 534 条 `[HarmonyPatch]` 属性，落在 190 多个补丁类上。
其中 33 条打在卡牌 `OnPlay`、44 条打在怪物 `GenerateMoveStateMachine`、22 条打在
`AfterAddedToRoom`、5 条打在单独的怪物招式方法，其余大部分是数据层的属性取值器
（`Description` / `CanonicalVars` / `CanonicalEnergyCost` / `Rarity` …），求解器读活的模型，
自动跟随，不用镜像。

## 结论

| 后果分级 | 条数 |
| --- | --- |
| 会拒绝整场战斗 | **1** |
| 会静默算错 | **10** |
| 只会多打一条红字 | **2** |
| 无影响（核过一致，或求解器本来就没模拟） | 其余 |

最高优先级的三件事：

1. 放行补丁挂在一个实机不再调用的方法上 —— **现在每一场带这 33 张牌的战斗都被拒绝**，
   今晚那份化石追踪者的问题包就是这个。
2. 幽灵骑士「咒缚」的招式 id 在适配层里写错了（`HEX_MOVE`，实际是 `HEX`），镜像是死代码。
3. 瀑布巨人的两招（`RAM_MOVE`、`PRESSURE_GUN_MOVE`）改版去掉了蒸汽层，求解器照旧每次加 3 层。

---

## 一、卡牌 OnPlay

**镜像覆盖是完整的。** RebalancedSpire 一共替换了 33 张牌的 `OnPlay`，
`CardMirrors.All()` 里正好是同样这 33 个类型，一一对应，没有多也没有少：

```
Afterlife ConsumingShadow EternalArmor ExpectAFight ForegoneConclusion ForgottenRitual Fuel
Glasswork Glow GraveWarden HandTrick HeirloomHammer HiddenDaggers InfiniteBlades Leap
MasterPlanner NeutronAegis PoisonedStab PullAggro ReaperForm Refract Salvo Seance Shatter
SicEm Spinner Spur Synchronize Tank Untouchable UpMySleeve WellLaidPlans Wisp
```

（查证：从反编译结果里抓 `[HarmonyPatch(typeof(X), "OnPlay")]`，和 `CardMirrors.All()` 的
`MirroredCard.For<T>` 列表求差集，两边都为空。）

### 缺口 1｜放行补丁挂错了方法 —— **会拒绝整场战斗**

- 适配层：`src/AuditFilter.cs` 把前缀挂在 `PredictionModPatchAudit.ValidateCardOnPlay` 上。
- 求解器 0.38.6：`ValidateCardOnPlay` 只剩一行 `=> CaptureCardOnPlay(cards)`，
  **全仓库只有 `src/Testing/UnattendedTestRunner.PrPolicyGuards.cs` 还在调它**。
  实机走的是 `PredictionModHookSubscriberCapture.Capture` → `CaptureCardOnPlay`，直接调内部方法。
- 所以我们的前缀从不触发，放行名单等于不存在，33 张牌里任意一张进场都会抛
  `IncompatibleGameplayModException`。

问题包里的调用栈就是这条路径，一个字都不用猜：

```
at CombatSolver.PredictionModPatchAudit.CaptureCardOnPlay(IEnumerable`1 cards)
at CombatSolver.PredictionModHookSubscriberCapture.Capture_Patch1(RunState runState, CombatState combat)
at CombatSolver.CombatRootSnapshot.Capture(CombatState state)
at CombatSolver.SolverController.RequestSearch(...)
```

**从哪一版开始坏的**：上游提交 `bce222b feat: register exact adapted OnPlay patch compositions`。
`git describe` 给的是 `v0.36.3-2-gbce222b`，但那只说明它是在 `v0.36.3` 之后写的，**不等于
它进了 0.36.4**。逐个 tag 核过之后：`v0.36.4`/`v0.36.5`/`v0.37.0`/`v0.38.0`/`v0.38.1` 都**不含**
这个提交，`v0.38.2` 才含（随 PR #87 合并发版，2026-09-14）。

```
v0.38.1  src/Prediction/PredictionModHookSubscriberCapture.cs:60  ValidateCardOnPlay(...)   ← 实机还在调
v0.38.6  只剩 PredictionModPatchAudit 里的定义和 UnattendedTestRunner 里的测试
```

也就是说**是 2026-09-14 把本地从 0.38.1 跟到 0.38.6 这一步打断的**，不是积压很久的旧账。
`git describe` 的输出不能当版本归属用，这一条记进教训。适配层的版本下限写的是 0.36.0，
所以自检拦不住；`AuditFilter.ResolveTarget()` 找的那个方法名还在，自检也照样通过。
这正是「方法还在、但没人调它了」这类漂移，按方法名做的自检抓不到。

**修法不是把名字改成 `CaptureCardOnPlay`。** 上游在同一个提交里开了正式的第三方登记入口：

- `CombatSolver.AdaptedCardOnPlayMirrors.Register<TCard>(schema, target, patches, handler)`
- `patches` 要写清这张牌 `OnPlay` 上**期望的那套补丁组合**（种类、方法、owner、优先级、
  before/after）。实机上对不上就抛 `PredictionUnsupportedException`，
  这比我们现在「只要都来自可信程序集就放行」严得多，正好能抓住 RebalancedSpire 换版本后
  补丁改了名或加了一条的情况。
- 登记在第一次建根时封盘（`Seal()`），必须在任何一场战斗之前做完 —— 和现在的时机一样。
- 命中登记之后，求解器走 `AdaptedOnPlaySnapshot.TryInvoke`，**整条配方由我们接管，
  它自己的 `CardEffectSpecRegistry.Apply` 不再跑**。也就是说
  `src/OnPlayCompensationPatch.cs` 那个补丁在新路径下是多余的，迁移时要一起清掉，
  否则会两头都关。

这也意味着 memory 里「求解器没有第三方登记入口」那一条，在卡牌 `OnPlay` 这一格已经过时了 ——
上游补上了，而且是个比我们当初提的方案更严的入口。

---

## 二、怪物招式

求解器对敌人出招的处理分三层，只有第三层需要我们管：

1. **出招表顺序**（44 个 `GenerateMoveStateMachine` 被替换）：求解器读怪物身上活的那张表，
   换了顺序自动跟随，不用管。
2. **伤害数值 / 意图**：从活的 `AbstractIntent` 读，改版用自己的常量建意图，也自动跟随。
3. **招式里攻击以外的效果**：写死在 `MonsterMoveEffects.Apply` 的一张
   「怪物类型名 + 招式 id」大表里（175 对）。这一层改了就必须镜像。

适配层现状：`MoveCoveragePatch` 往 `Supports` 补了 39 对，`MonsterMirrors.ApplyPrefix` 有 69 条
招式镜像。

枚举办法：把改版每张出招表里的 `new MoveState("ID", handler, intents…)` 全抓出来
（共 180 条），按 handler 是 `__instance.某方法`（还是原版实现）还是补丁类自己的方法
（被重写了）分开，再和求解器的 `Supports` 表、适配层的两张表求交。

- handler 是原版方法 → 求解器那条处理还是对的，不用管。
- handler 是补丁类自己的方法，而求解器 **不** 支持这个 id → 最多一条红字。
- handler 是补丁类自己的方法，而求解器 **支持** 这个 id，我们又没镜像 → **静默算错**，
  下面这 10 条就是这么筛出来的。

### 会静默算错的 10 条

每一条都逐句比过改版方法体和 `sts2.dll` 里的原版方法体，以及
`MonsterMoveEffects.Apply` 里对应的那个 case。

| # | 怪物 / 招式 id | 改版实际做的 | 求解器照旧算的 | 放行 | 镜像 |
| --- | --- | --- | --- | --- | --- |
| 1 | `SpectralKnight` / `HEX` | 每个目标 1 层诅咒 + 自己 1 层虚无 | 每个目标 **2 层**诅咒，不给虚无 | n/a | ~~写成了 `HEX_MOVE`，死代码~~ **已修** |
| 2 | `Aeonglass` / `EBB_MOVE` | 只打一下，**不再加甲** | 仍加 `EbbBlock` 点格挡 | n/a | **已修** |
| 3 | `Crusher` / `BUG_STING_MOVE` | 只打一下，**不再上虚弱脆弱** | 仍给玩家 2 虚弱 + 2 脆弱 | n/a | **已修** |
| 4 | `Entomancer` / `PHEROMONE_SPIT_MOVE` | 没蜂巢→只给 1 层蜂巢；<3→蜂巢 +2 且力量 +1；≥3→力量 +2 | 没蜂巢或 ≥3→力量 +2；否则蜂巢 +1、力量 +1 | n/a | **已修** |
| 5 | `SoulFysh` / `GAZE_MOVE` | 只打一下，**不再塞召唤牌** | 仍往弃牌堆塞 `GazeMoveAmount` 张 `Beckon` | n/a | **已修** |
| 6 | `VineShambler` / `GRASPING_VINES_MOVE` | 不打伤害了，缠绕 1 层 **外加自己加甲** | 只上缠绕，看不到那份格挡 | n/a | **已修** |
| 7 | `WaterfallGiant` / `RAM_MOVE` | 只打一下，**不再给蒸汽** | 仍给自己 3 层 `SteamEruptionPower` | n/a | **已修** |
| 8 | `WaterfallGiant` / `PRESSURE_GUN_MOVE` | 打一下并把自己的压力枪伤害累加，**不再给蒸汽** | 累加之外还给 3 层蒸汽 | n/a | **已修** |
| 9 | `LivingFog` / `BLOAT_MOVE` | 每只生出来的气弹上 1 层「乒乓」，并且 `BloatAmount` 每次 +1（上限 5） | 按建根时冻结的 `BloatAmount` 生气弹，不上乒乓、不递增 | n/a | **已修（`BloatSpawnPatch`，乒乓 + 递增）** |
| 10 | `TestSubject` / `BURNING_GROWL_MOVE` | 灼烧 4/3 张、力量 +2/+1（高难/普通） | 读原版字段：灼烧 **5/3** 张、力量 **+3/+2** | n/a | **已修** |

几条要说明的：

- 第 1 条是纯拼写问题。`MonsterMirrors.cs` 写的是 `case ("SpectralKnight", "HEX_MOVE")`，
  但原版和改版的出招表里这一招的 id 都是 `"HEX"`（求解器 `Supports` 表里也是 `"HEX"`）。
  镜像从来没被命中过，注释里写的「改版每个目标 1 层 + 自己一层虚无」是对的，只是没生效。
- 第 6 条改版把 `GRASPING_VINES_MOVE` 的意图换成了 `DefendIntent` + `CardDebuffIntent`，
  格挡量不在意图里，所以求解器看不到。
- 第 10 条差的是数值，但**不是**能自动跟随的那种数值：改版在补丁类里用自己的
  `BurnCount` / `StrengthPowerAmount` 常量重写了方法体，而求解器读的是原版怪物身上的
  `BurningGrowlBurnCount` / `BurningGrowlStrengthGain` 字段，那两个字段没被补丁动过。
  灼烧张数会进意图（`StatusIntent(BurnCount)`）所以红字不会有，但实际塞进弃牌堆的张数
  和力量层数都是错的。
- 第 9 条的 `BloatAmount` 递增，求解器是按「建根时捕获的静态整数」用的，一场战斗内不变；
  多回合的计划会越推越偏。

### 已经比过、确认一致的（不用动）

`KinFollower/POWER_DANCE_MOVE`、`Nibbit/SLICE_MOVE`、`SkulkingColony/INERTIA_MOVE`
（改版去掉了攻击，但意图也同步去掉了，求解器的伤害是从意图来的，所以对得上）、
`Vantom/DISMEMBER_MOVE`、`ThievingHopper/THIEVERY_MOVE`、
`TestSubject` 的 `RESPAWN_MOVE` / `SKULL_BASH_MOVE` / `MULTI_CLAW_MOVE` /
`PHASE3_LACERATE_MOVE`（都是逐字照抄，差的只是伤害常量，而伤害走意图）。

`docs/monster-move-audit.md` 里写「瀑布巨人除加压之外那几条一致」和「求解器有镜像的那 30 条
已经逐条比完了」，这两句是错的 —— 上面第 6、7、8 条就在那批里。那份表是脚本生成的，
它自己也提示过可能有漏判；这次是手工逐句比的，以这份为准。

### 只会多打一条红字（不算错，只是那一招不敢用）

改版给 26 条出招换了新 id，`MoveCoveragePatch.SupportsPostfix` 已经补了其中有效果的那些。
剩下没补的都是「求解器原版下也不支持」的招式，改版改了它们不会让预测更错。
还有一整块是门匠（Doormaker）那个新 Boss，`Entry.WarnAboutUnadaptedContent` 已经明确
声明不做，加载时会提醒玩家去关掉开关。

---

## 三、Power

改版新增 33 个 Power。适配层引用到了其中 31 个。差的两个：

| Power | 情况 | 后果分级 |
| --- | --- | --- |
| `OmnidynamicsPower` | 门匠专属（「全能」） | 无影响 —— 门匠整块已声明不做 |
| `ToItsOriginOwnerPower` | 打出被标记的拜尔多尼斯之卵之后给玩家的那层，作用是战后清卵和加一次牌选择 | 无影响 —— 全是战斗外的事 |

改版对**原版** Power 的行为改动只有三处，全部已镜像：

- `PlatingPower.AfterSideTurnStart` → `PlatingDecayPatch`
- `SkittishPower.AfterAttack` → `MonsterMirrors.Skittish`
- `IllusionPower.ReviveMove` → `MonsterMirrors.RevivePostfix`

（`HardToKillPowerPatch` 和 `RollingBoulderPowerPatch` 只改了属性取值器，数据层，自动跟随。）

---

## 四、遗物

改版动了 28 个遗物。战斗内会结算的那些，覆盖情况：

| 遗物 | 改版动了什么 | 适配层 |
| --- | --- | --- |
| `BoomingConch` | 整套机制换掉（打牌计数、改费用、改抽牌） | `RelicStatefulMirrors` |
| `DiamondDiadem` | 整套机制换掉 | `RelicStatefulMirrors` |
| `Crossbow` | 生成的牌变成「本场免费 + 消耗」 | `RelicMirrors.GenerateRelicCardsPrefix` |
| `ChoicesParadox` | 备选牌换了 | `RelicMirrors` |
| `Fiddle` | `ShouldDraw` / `AfterPreventingDraw` | `RelicMirrors`（换掉求解器那条登记） |
| `HistoryCourse` | 重放范围从「攻击」放宽到「攻击或技能」 | `HistoryCoursePatch` |
| `WhisperingEarring` | 触发条件整个换了 | `WhisperingEarringPatch` |
| `LordsParasol` | `ModifyMaxEnergy` +1 | 不用做：走游戏自己的监听链，自动跟随 |
| `WarHammer` / `AlchemicalCoffer` 等 | 战斗内钩子 | 不用做：求解器本来就没镜像它们 |

其余 17 个（`BigMushroom`、`PreservedFog`、`SwordOfStone`、`Regalite`、`LavaRock`、
`Pomander`、`SereTalon`、`SwordOfJade`、`NeowsTalisman` …）改的是战斗外的事或纯数据层。
**无影响。**

---

## 五、药水、状态牌、病症、附魔、新牌

| 类别 | 改版有什么 | 适配层 | 分级 |
| --- | --- | --- | --- |
| 新药水 | `BoneTeaPotion` / `EmberTeaPotion` / `TeaOfDiscourtesyPotion` | `PotionMirrors` 三个都登记了 | 无影响 |
| 原版药水 | 没有被替换行为的 | — | 无影响 |
| 状态牌 | `Wither`（`OnTurnEndInHand` + `FakeUpgrade` 都换了） | `StatusCardMirrors` | 无影响 |
| 状态牌 | `Soot`（只改了费用和关键字） | 不用做，数据层 | 无影响 |
| 新病症 | `Withering` | `AfflictionMirrors` | 无影响 |
| 新病症 | `Devoured` / `Weighted` | `CardEnteredCombatPatch` + `PowerAfflictionPatch` | 无影响 |
| 新病症 | `ToItsOriginOwner` | 只在 `MonsterMirrors` 里施加，**它自己的 `OnPlay` 没镜像** | **只会多打一条红字** |
| 新附魔 | `Energetic` / `Poisonous` | `EnchantmentMirrors` | 无影响 |
| 原版附魔 | `Inky.EnchantDamageAdditive` | 不用做（`EnchantmentMirrors` 注释里记了理由） | 无影响 |
| 新牌 | `CorpseExplosion` / `LimitBreak` | `NewCardMirrors` | 无影响 |
| 原版牌的钩子 | `RightHandHand` / `RocketPunch` | `CardHookMirrors` | 无影响 |
| 原版牌的钩子 | `Bolas.BeforeHandDraw` | 求解器**从来不分发牌的这个钩子**，原版下也一样缺 | 无影响（不是改版带来的） |

### 缺口 2｜`ToItsOriginOwner.OnPlay` 没镜像 —— 只会多打一条红字

拜尔多尼斯的「发怒」会给玩家牌组里的卵标上这个病症（`MonsterMirrors` 已经镜像了这一步），
之后打出那张卵会：给所有玩家一层 `ToItsOriginOwnerPower`，然后**把拜尔多尼斯直接移出战斗**。

`AfflictionOnPlayMirrors.Registry` 只登记了 `Withering`。碰到没登记的重写，
`MethodMirrorRegistry.Invoke` 走的是 `RecordMethodNotMirroredRisk()` —— 记一条风险、不结算，
**不会静默算错**。但代价是求解器看不到「打一张卵直接结束这场精英战」这条线，
遇到拜尔多尼斯的那一场会一直挂着红字。

补它不难（就是一次移除敌人），属于第二优先级之后的事。

---

## 六、怎么重跑这次普查

下次换了 RebalancedSpire 或 CombatSolver 的版本，照这个顺序走一遍。
中间产物一律放 scratchpad，不要写进仓库。

### 0. 先核对基准

```bash
sha256sum "D:/Sponsored/Steam/steamapps/common/Slay the Spire 2/mods/RebalancedSpire/RebalancedSpire.dll"
cat "D:/Sponsored/Steam/steamapps/common/Slay the Spire 2/mods/RebalancedSpire/RebalancedSpire.json"
```

和 `src/PinnedTargets.cs` 里的 `VerifiedRebalancedSpireVersion` 对一下。

### 1. 整包反编译（约 2 分钟）

**`-r` 是必须的。** 不给引用路径的话，`[HarmonyPatch(typeof(X), "Y", MethodType.Getter)]`
这种带枚举参数的属性会被解成 `Could not decode attribute arguments`，
整整 130 多条属性读不出目标，枚举就是假的。

```bash
ilspycmd --disable-updatecheck -p \
  -r "D:/Sponsored/Steam/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64" \
  -o <scratchpad>/rs \
  "D:/Sponsored/Steam/steamapps/common/Slay the Spire 2/mods/RebalancedSpire/RebalancedSpire.dll"
grep -rc "Could not decode" <scratchpad>/rs   # 必须全是 0
```

### 2. 抽出全部补丁点

从每个 `.cs` 里抓 `[HarmonyPatch(typeof(目标), "成员"[, MethodType.X])]` 加紧随其后的
`[HarmonyPrefix|Postfix|Transpiler|Finalizer]`。校验：抓到的条数要等于
`grep -rho "\[HarmonyPatch(typeof" | wc -l`，这次是 534。

按成员名分两堆：

- **数据层**（`Description` `CanonicalVars` `CanonicalEnergyCost` `CanonicalKeywords`
  `Rarity` `Type` `TargetType` `ExtraHoverTips` `MinInitialHp` … 以及各种 `*Damage` 取值器）：
  求解器读活模型，自动跟随，**不用管**。
- **行为层**（`OnPlay`、`GenerateMoveStateMachine`、`*Move`、各种 `After*` / `Before*` /
  `Modify*` / `Try*` 钩子）：逐条核。

### 3. 卡牌 OnPlay

`{typeof(X), "OnPlay"} 的 X 集合` 对 `CardMirrors.All()` 里的 `MirroredCard.For<T>` 集合
求双向差集，两边都必须为空。多出来的是缺镜像，少掉的是过期镜像。

### 4. 怪物招式

1. 从改版每个 `GenerateMoveStateMachine` 前缀里抓
   `new MoveState("ID", handler, intents…)`，记下 handler 是
   `__instance.某方法`（原版实现）还是补丁类自己的方法（被重写）。
2. 从 `CombatSolver/src/Prediction/MonsterMoveEffects.cs` 的 `Supports` 里抓
   `("怪物", "招式")` 全集。
3. 从 `src/MoveCoveragePatch.cs` 的 `SupportsPostfix` 和 `src/MonsterMirrors.cs` 的
   `case ("怪物", "招式")` 抓适配层的两张表。
4. 筛出 **「handler 是补丁类自己的方法」∩「求解器 Supports 为真」∩「适配层没有 case」**，
   这一批就是候选。
5. 候选逐个和原版比，原版方法体这样取：
   ```bash
   ilspycmd --disable-updatecheck -t "MegaCrit.Sts2.Core.Models.Monsters.<怪物>" \
     "D:/Sponsored/Steam/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64/sts2.dll"
   ```
   只看攻击以外的效果和常量来源；伤害走意图，可以跳过。
6. **顺手对一遍招式 id 的字面量**。第 1 条缺口就是 id 拼错，前面四步全都发现不了 ——
   适配层的 `case` 字符串必须逐个在「改版出招表里出现过的 id 全集」里找得到，
   找不到的就是死代码。

### 5. 新内容

`RebalancedSpire.Core.{Powers,Potions,Cards,Afflictions,Enchantments,Monsters}` 下的
类型名全集，对 `src/` 下 `grep` 到的类型名求差集。差出来的逐个判断是不是战斗内的东西。

### 6. 求解器那一侧的入口

这次最贵的一条缺口不在 RebalancedSpire 那边，而在求解器改了调用路径。所以每次跟版本还要做：

- `AdapterSelfCheck.Run()` 里 `Resolve*Target()` 找的每个方法，去求解器仓库里
  `grep` 一遍**调用点**，确认实机路径真的会经过它，而不只是「方法还在」。
- `git log --oneline <上一次核对的版本>..HEAD -- src/Prediction/ src/Engine/InCombat/Mirrors/`
  扫一遍，重点看有没有新的第三方登记入口（这次就是 `AdaptedCardOnPlayMirrors`）。


---

## 七、这一轮改了什么（2026-09-14 当晚）

缺口 1（会拒绝整场战斗）已修：新增 `src/AdaptedOnPlayRegistrar.cs`，33 条登记语句一句没动，
改的是它们登记到哪里——从普通镜像表加登记进上游 `AdaptedCardOnPlayMirrors`。删掉
`src/AuditFilter.cs`。求解器版本下限抬到 `0.38.2`。
验收 `RS-WELL-LAID-PLANS-RETAIN` 通过，反向对照做过（注掉那句登记立刻挂）。

十条静默算错里补了九条，都在 `src/MonsterMirrors.cs`：`SpectralKnight/HEX`（改掉写错的
招式 id）、`Aeonglass/EBB_MOVE`、`Crusher/BUG_STING_MOVE`、`Entomancer/PHEROMONE_SPIT_MOVE`、
`SoulFysh/GAZE_MOVE`、`VineShambler/GRASPING_VINES_MOVE`、`WaterfallGiant/RAM_MOVE`、
`WaterfallGiant/PRESSURE_GUN_MOVE`、`TestSubject/BURNING_GROWL_MOVE`。
`LivingFog/BLOAT_MOVE` 的乒乓另开了 `src/BloatSpawnPatch.cs`（那一段在
`ApplyBeforeAttack` 里，`MonsterMirrors` 的前缀够不着）。

**十条静默算错现在全部补完。** `LivingFog` 的 `BloatAmount` 递增最后也做了：计数挂在
`simulator.StateStore` 上、按怪物模型索引。那个存储的条目实现 `IPredictionStateForkable`，
搜索分叉时跟着分支各复制一份，正是需要的语义。走不了 `ModelPredictionStateMirrors` 那条
登记点——它只对遗物和修饰器开放（`CaptureRootState` 只在 `SimulatedCombatState` 里对
`_rootRelicSources` 和 `_modifiers` 调），怪物模型根本不会被捕获。

**核对基准**：以本文件为准，`docs/monster-move-audit.md` 里和这里冲突的结论作废
（那份是脚本生成的，这次是逐句比的）。

---

## 八、迁移过程中撞出来的上游缺陷：战斗中生成的牌会让整条搜索炸掉

改完之后跑全量矩阵，`RS-INFINITE-BLADES-HAND-SIZE` 挂了：

```
搜索动作回放失败：turn=2 action_count=5 kind=PlayCard card=SHIV
  ---> PredictionUnsupportedException: Card type was not audited in this adapted OnPlay root.
       at CombatSolver.AdaptedOnPlaySnapshot.TryInvoke(...)
```

上游那两半对不上：

- 建根只审**建根时存在**的牌。`PredictionModHookSubscriberCapture.EnumerateAuditableCards`
  枚举的是战斗牌堆 + 跑局牌库；`PredictionModPatchAudit.CaptureCardOnPlay` 的注释也明写了
  「只在战斗中生成的牌类型在建根时看不到，这里不审」。
- 但 `AdaptedOnPlaySnapshot.TryInvoke` 对**任何**不在那本表里的类型直接抛。

结果：**只要存在任何一条适配登记**（也就是本适配层一加载），战斗里第一张生成牌被打出来就炸。
刀刃、灼烧、伤口、虚无这类牌到处都是，所以这不是边角情况。

本地的补法是 `src/AdaptedSnapshotFallbackPatch.cs`：类型不在审计表里时，**只在这张牌的
`OnPlay` 上确实一个第三方补丁都没有**的前提下回退到普通镜像表；有第三方补丁仍然照上游抛。
没有第三方补丁的话原版镜像本来就是对的，所以这条回退不放宽任何语义。

**这一条应该回报上游**（他们自己也会撞上：任何第三方适配一旦用了这个入口就中招）。
上游修好之后本地这个补丁可以撤掉。

---

## 九、藏匿匕首：镜像顺序错，实机操作不了（2026-09-14 第二份问题包）

问题包 `CombatSolver-0.38.6-INFESTED_PRISMS_ELITE-63ecb4be…`，报的是

```
NativeChoicePlanMismatchException：原生选牌页面找不到 SHIV+0#0；
当前候选=BACKFLIP+0|…,DEFEND_SILENT+0|…
```

适配层自己在日志里喊了八遍 `未建模的结算内选择：藏匿匕首要弃掉几张手牌`。

**根因是顺序，不是漏建模。** 这张牌的效果被一个玩家选择劈成两段：先从手牌选几张弃掉，
**选完之后**才造匕首。求解器本来就把这两段分开处理——弃牌走 `CardChoiceSupport.GetSpec`
开成搜索分支（张数读活的 `Cards` 变量，改版改成 3、升级 −1 会自动跟随），造匕首走
`CardChoiceSupport.ApplyPostChoiceEffects`。我们的镜像却在出牌那一刻就把匕首造进手牌，
于是模拟里手牌提前多出两张匕首，求解器计划「把匕首弃掉」，实机那个页面里没有匕首。

**改版和原版的实际差别只有一处**：造出来的匕首挂 1 层「充能」，而不是随本牌升级。

修法：镜像改成空操作（两段都由求解器的选择通道负责），只在造匕首那一层补附魔，
见 `src/HiddenDaggersShivPatch.cs`。

**顺带一条教训**：这张牌在此之前**一条验收用例都没有**——镜像写了，覆盖没跟上，所以顺序错
一直没人发现。新加了 `RS-HIDDEN-DAGGERS-DISCARD-ORDER`，但要说清楚它**锁不住那个顺序**：
顺序错的那一版是「更宽松」的，它的弃牌候选里多了两张匕首却完全可以不选它们，从而得到和正确
顺序一样的动作数、伤害、格挡。要逼它必须弃匕首，就得让手牌数少于要弃的张数；可那样弃完手里
只剩匕首，而「充能」匕首伤害是 0、只给能量，没地方花，求解器根本不会打这张牌。两个条件互斥。
所以那条用例是端到端覆盖：真把路线打出去，确认这张牌能打、弃牌页面应答得上、整场走得完。
