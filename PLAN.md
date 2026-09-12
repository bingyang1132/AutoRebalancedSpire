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
- [ ] **0.2 改写求解器已登记的牌**。`MethodMirrorRegistry.Register` 用 `Dictionary.Add`，
  同一类型登记第二次直接抛。这 33 张里有 5 张求解器自己已经登记了 bespoke 镜像：
  **ConsumingShadow、Glasswork、Refract、Shatter、Spinner**。不解决这条，整个 Defect 批做不了。
- [ ] **0.3 计算变量公式**。`CalculatedVarSpecRegistry` 把每张计算牌的乘数写死在 internal switch 里，
  没有第三方入口。**ExpectAFight**（原版乘数=力量 → 改成弃牌堆里攻击牌张数）、
  **Synchronize** 两张受影响。光镜像 `OnPlay` 不够，别处读这个变量的地方仍按原版公式算。
- [ ] **0.4 新 Power 的钩子镜像地基**。RebalancedSpire 新增 33 个 Power，共重写 22 种钩子。
  逐个登记进求解器对应的 `XxxMirrors.Registry`。其中 `AfterlifePower` 重写的
  `AfterEnergyResetLate` **求解器只对一个遗物分发、Power 一个不发也不记风险** ——
  和 PR #88 修的是同一类毛病，要么上游开注册表，要么本地打补丁补。
- [ ] **0.5 验收框架**。照 AutoWatcher 的做法搭 `tools/run-rebalanced-matrix.ps1`：
  一张牌一条算术判据 + 反向对照。用户负责小规模实测，矩阵只用于回归。

## 阶段 1：全局战斗规则（不分角色，任何一局都可能遇上）

- [ ] **1.1 镀甲（PlatingPower）的衰减规则变了**。原版每回合减 1；改版：敌人侧按
  `Decrement` 变量减、且战斗第一轮不减，玩家侧有 `EternalArmorPower` 时**不减**。
  镀甲是通用 Power，Regent / 无色 / 遗物都会给，算错就是整条防御线算错。
- [ ] **1.2 附魔**。`Inky`（墨刃）的 `EnchantDamageAdditive` 改成「只对强化攻击加伤」；
  新附魔 **Energetic**、**Poisonous**（匕首类牌会带）。
- [ ] **1.3 病症**。`Tainted` 改成不可叠加；新病症 **Devoured、Weighted、Withering、
  ToItsOriginOwner**。病症挂在牌上，任何角色都可能吃到。
- [ ] **1.4 状态牌 Wither**。`OnTurnEndInHand` 改成：没带 `Withering` 病症才吃固定伤害，
  带了则只有 FakeUpgrade 过的才吃 `Damage`。诅咒牌 Enthralled / Folly 是纯数据层，不用做。
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

- 2026-09-12 立项。阶段 0.1 完成；已镜像 5 张牌：
  **Fuel、Untouchable、Glow、UpMySleeve、NeutronAegis**。
