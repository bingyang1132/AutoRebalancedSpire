# AutoRebalancedSpire 总清单

对着 RebalancedSpire `v0.3.10-beta`（DLL sha256 `cce7a199…dcd2b`）全量反编译列出来的。
顺序是**先做影响全局的，再逐个角色**；每完成一个角色出一个可测的构建。

判据统一：**数据层改动（`CanonicalVars` / 关键字 / 费用 / 出招表）求解器读活模型，自动跟随，
不进这张清单。** 进清单的只有「求解器按原版语义算、而实机已经不那样了」的东西。

图例：`[x]` 已完成 · `[ ]` 待做 · `[?]` 待评估（先读代码再决定要不要做）

---

## 阶段 0：地基

- [x] **0.1 放行入口**。求解器建根审牌组，任何一张牌 `OnPlay` 上有第三方补丁就拒整场战斗，
  而它没有第三方放行入口。适配层打补丁把**已镜像**的牌摘出待审名单。
  规矩：放行名单 = 镜像名单（`src/MirroredCards.cs`）。
- [x] **0.2 改写求解器已登记的牌**。`MethodMirrorRegistry.Register` 用 `Dictionary.Add`，
  同一类型登记第二次直接抛。这 33 张里有 5 张求解器自己已经登记了 bespoke 镜像：
  **ConsumingShadow、Glasswork、Refract、Shatter、Spinner**。不解决这条，整个 Defect 批做不了。
  做法：`src/RegistryOverride.cs` 全程反射摘掉已有登记再登记自己的（注册表内部那个
  `LookupResult` 是私有嵌套类型，在这边连名字都写不出来，只能走非泛型 `IDictionary`）。
  摘不摘得到和 `MirroredCard.ReplacesBuiltIn` 对不上就抛 —— 上游动了镜像表就要重新核对。
  自检里加了一条字段探针，上游改字段名会变成加载时的干净失败。
  已用 **Spinner** 实跑验证：无头日志里自检通过、6 张牌 + 1 个 Power 登记成功。
- [x] **0.3 计算变量公式**。`CalculatedVarSpecRegistry` 把每张计算牌的乘数写死在 internal switch 里，
  没有第三方入口。`src/CalculatedVarPatch.cs` 用前缀接管我们认得的牌，其余放行原实现。
  受影响的**只有 ExpectAFight 一张**：原版乘数是力量，改版换成**手牌里攻击牌的张数**
  （`PileType.Hand` = 2、`CardType.Attack` = 1，枚举顺序核过）。
  Synchronize 原本也在那张表里，但改版把它的 `CalculatedVar` 整个从 CanonicalVars 里去掉了
  （换成普通 PowerVar），所以不用管 —— 这是读代码才发现的，先前按名字判断会多做一张。
  顺带把 ExpectAFight 的牌镜像也做了（走求解器自己的计算通道，保证和估值那边读到同一个数）。
- [x] **0.4 新 Power 的钩子镜像地基**。RebalancedSpire 新增 33 个 Power，共重写 22 种钩子。
  走注册表的那些直接登记（`src/PowerMirrors.cs`）。缺口补完了一个：
  `AfterEnergyResetLate` 求解器只跑一个遗物（`BoundPhylactery`），**Power 一个都不发也不记
  风险** —— `src/AfterEnergyResetLateDispatch.cs` 挂 postfix 自己分发，上游哪天开成注册表
  就把这个文件换成登记。已有两个 Power 走通：SpinnerPlusPower、AfterlifePower。
  剩下 31 个跟着各自的角色批做。
- [ ] **0.5 验收框架**。照 AutoWatcher 的做法搭 `tools/run-rebalanced-matrix.ps1`：
  一张牌一条算术判据 + 反向对照。用户负责小规模实测，矩阵只用于回归。

## 阶段 1：全局战斗规则（不分角色，任何一局都可能遇上）

- [x] **1.1 镀甲（PlatingPower）的衰减规则变了**。逐句比过原版：改版有两处差别 ——
  **玩家第一回合也衰减**（原版跳过那一次；改版里那道判断被写成了一个空的 if），
  **玩家身上有 `EternalArmorPower` 时完全不衰减**（那个 Power 是个纯标记，一个钩子都没重写）。
  敌人侧一致。求解器这段不在注册表里：`SimulatedCombatState.TriggerBaseSideTurnStart` 写死了
  「按 Decrement 减」，减不减由调用方一个布尔参数决定 —— 前缀改那个参数就够，敌人侧不动。
- [x] **1.2 附魔**。只用镜像 `OnPlay` 两处：**Energetic**（打出给一次能量后自己失效，和原版
  Sown 逐句同形）、**Poisonous**（给「目标 + 所有可命中敌人」各一份毒 —— 单体牌的目标会进名单
  两次，原版 `PowerCmd.Apply(IEnumerable)` 不去重，所以目标实际吃两份，看着像笔误但照它实际
  跑的结果镜像）。改伤害的那几个方法**不用镜像**：求解器算附魔伤害直接调附魔自己的
  `EnchantDamage*`，`ModifyDamageMultiplicative` 没登记也会回落到监听者自己的实现 ——
  所以 `Inky` 被改过的加伤公式、充能的「这张牌不造成伤害」都是自动跟上的。
- [ ] **1.3 病症**。`Tainted` 改成不可叠加（`IsStackable` 是取值方法，多半自动跟随，待确认）；
  新病症 **Devoured、Weighted**（`AfterCardEnteredCombat` 里按持有者有没有某个 Power 决定
  清不清掉自己 —— 求解器那个时点是写死的 switch，**不分发给病症**，又是一个要打补丁的缺口）、
  **Withering**（`OnPlay` 改假升级层数 + 上 `SandsOfTimePower`；`AfterCardExhausted` 把枯萎塞回
  弃牌堆）、**ToItsOriginOwner**（拜尔多尼斯专属，归怪物批）。
- [x] **1.4 状态牌 Wither**。求解器登记的是通用的「吃 Damage 点伤害」，用 0.2 那套换掉。
  改版：没带「凋零」病症时吃固定 6 点（新变量 `Fixed`，属性 Unpowered|Move）；带了病症则只有
  假升级层数不为 0 才吃 `Damage`（改版基数是 0，每层加 `PerLevel`=3）。按原版算会把一张会
  持续掉血的状态牌当成无害的。诅咒牌 Enthralled / Folly 是纯数据层，不用做。
- [ ] **1.5 遗物（战斗内的那批）**。`BoomingConch`（回合开始 + 改抽牌数）、`Crossbow`、
  `DiamondDiadem`、`ChoicesParadox`、`Fiddle`（改抽牌判定）、`HistoryCourse`、
  `WhisperingEarring`，以及 `AbstractModel` 级的 `ModifyMaxEnergy`、
  `TryModifyEnergyCostInCombat`、`TryModifyStarCost`、`AfterEnergySpent`、`BeforeSideTurnEnd`、
  `BeforeCombatStart`。逐个判断求解器是不是镜像了这个遗物。
- [?] **1.6 战斗外的遗物与奖励**（`AfterObtained`、`AfterCombatVictory`、`TryModifyRewards`、
  `AfterRoomEntered`、`AfterRewardTaken`）。不影响战斗模拟，只可能影响跨战斗估值。先记着。

## 阶段 2 起：逐角色

每个角色一批，做完给一个可测构建。括号里是这一批要镜像的牌。

- [ ] **2. Necrobinder（9 张）**：Afterlife、GraveWarden、PullAggro、ReaperForm、Seance、
  SicEm、Spur、Wisp、RightHandHand（`AfterCardPlayedLate`）。
  连带 Power：AfterlifePower、ReaperFormPlusPower、SicEmPlusPower、SoulWitherPower、
  GuardPower、MinionFakePower、LeechingHugPower。涉及召唤奥斯提和魂牌生成，最重的一批。
- [ ] **3. Silent（8 张）**：HandTrick、HiddenDaggers、InfiniteBlades、MasterPlanner、
  PoisonedStab、WellLaidPlans（Untouchable、UpMySleeve 已完成）。
  连带 Power：InfiniteBladesPlusPower、MasterPlannerPlusPower、WellLaidPlansPlusPower。
  依赖 1.2（匕首带的两个新附魔）。
- [ ] **4. Defect（8 张）**：ConsumingShadow、Glasswork、Leap、Refract、Shatter、Spinner、
  Synchronize、RocketPunch（`AfterCardGeneratedForCombat`）。
  连带 Power：ConsumingShadowPlusPower、LeapPower、SpinnerPlusPower、SynchronizePlusPower。
  **依赖 0.2**（其中 5 张求解器已登记）和 0.3（Synchronize）。
- [ ] **5. Regent（2 张剩余）**：ForegoneConclusion、HeirloomHammer。
  连带 Power：ForegoneConclusionPlusPower。（Glow、NeutronAegis 已完成）
- [ ] **6. Ironclad（3 张）**：ExpectAFight、ForgottenRitual、Tank。
  连带 Power：TankPlusPower。**依赖 0.3**（ExpectAFight）。
- [ ] **7. 无色（3 张）**：EternalArmor、Salvo、Bolas（`BeforeHandDraw`）。
  连带 Power：EternalArmorPower。**依赖 1.1**（镀甲）。
- [ ] **8. Token / Status**：Wither（见 1.4）。（Fuel 已完成）

## 阶段 9：怪物与遭遇

- [ ] **9.1 出招表抽查**。44 张 `GenerateMoveStateMachine` 理论上自动跟随（求解器读的是怪物身上
  活的那张表），但要实测抽查几个确认，特别是改了 `AfterAddedToRoom`（改血量/初始 Power）的那 22 个。
- [ ] **9.2 被重写的单招**：`TheForgotten.MiasmaMove`、`TheInsatiable.LiquifyMove`、
  `SlitheringStrangler.ConstrictMove`、`GasBomb.ExplodeMove`、`IllusionPower.ReviveMove`、
  `SkittishPower.AfterAttack`、`KnowledgeDemon.ChooseCurse`。
- [ ] **9.3 新 Boss Doormaker**。连带 `AttackCommand.TargetingRandomOpponents` 的改写
  （只在场上有 Doormaker 时生效）和 `OmnidynamicsPower`。新怪物求解器完全不认识。
- [ ] **9.4 新增卡牌**：CorpseExplosion、LimitBreak（连带 CorpseExplosionPower）。

## 阶段 10：地图外（默认不做，只记录）

事件（10 个 `GenerateInitialOptions`）、三位 Ancient 的选项、遗物池、地图生成、
`RunHistorySaveManager`。这些不参与战斗模拟。求解器的跨战斗路线估值里可能间接相关，
等前面做完再判断。

---

## 进度

- 2026-09-12 立项。阶段 0.1、0.2、0.3、0.4 完成，只剩 0.5 验收框架。
  已镜像 7 张牌：**Fuel、Untouchable、Glow、UpMySleeve、NeutronAegis、Spinner、ExpectAFight**
  （后两张属于 Defect / Ironclad 批，提前做是为了验证 0.2 和 0.3 的机制）；
  2 个新 Power：**SpinnerPlusPower、AfterlifePower**。
  无头实跑确认三个补丁都挂上了、自检通过、零报错。
