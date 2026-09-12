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
- [x] **0.5 验收框架**。`tools/run-rebalanced-matrix.ps1`，照 AutoWatcher 那份改的。
  开跑前拦两件事：求解器构建产物和部署不一致（否则每条都会以「不兼容」挂掉，跑完一小时只
  告诉你全没过）、`mods/` 里还有 Sts2RebalanceBeta（它和 RebalancedSpire 都换了燃料和辉光的
  `OnPlay`，同时在场测出来的东西不算数）。
  牌用**类型名**指称，harness 的模型解析除了 Id 也认类型名，省掉一层查证。

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
- [x] **1.3 病症**（新病症本身做完；两个源头 Power 归怪物批）。`Tainted` 改成不可叠加（`IsStackable` 是取值方法，多半自动跟随，待确认）；
  新病症 **Devoured、Weighted**（`AfterCardEnteredCombat` 里按持有者有没有某个 Power 决定
  清不清掉自己 —— 求解器那个时点是写死的 switch，**不分发给病症**，又是一个要打补丁的缺口）、
  **Withering**（`OnPlay` 第一次打出把枯萎假升级两级、本场费用 +1、给一层时之沙，之后每次退一级；
  `AfterCardExhausted` 把枯萎塞回弃牌堆 —— 也就是这张牌消耗不掉，不镜像会把一条还会持续吃伤害的
  路线算成安全的）、**ToItsOriginOwner**（拜尔多尼斯专属，归怪物批）。
  连带做了 **SandsOfTimePower** 的额外回合：求解器只认遗物给的额外回合，Power 给的看不见，
  照 AutoWatcher 给腾跃写的那份补丁做。
  `HungerPower` / `ScrutinyPower` 两个源头 Power：**牌进场时的感染已经做了**（饥饿不碰能力牌，
  审视来者不拒），和上面的自我清除是同一个补丁的两面。还没做的是它们刚施加时把**已有的**牌
  一次性感染（`AfterApplied`），以及审视的手牌上限修正（`IMaxHandSizeModifier`）。
- [x] **1.4 状态牌 Wither**。求解器登记的是通用的「吃 Damage 点伤害」，用 0.2 那套换掉。
  改版：没带「凋零」病症时吃固定 6 点（新变量 `Fixed`，属性 Unpowered|Move）；带了病症则只有
  假升级层数不为 0 才吃 `Damage`（改版基数是 0，每层加 `PerLevel`=3）。按原版算会把一张会
  持续掉血的状态牌当成无害的。诅咒牌 Enthralled / Folly 是纯数据层，不用做。
- [x] **1.5 遗物（战斗内的那批）**。做了四个：
  - **十字弩**：生成的攻击牌从「本回合免费」变成「本场免费 + 消耗」。整段接管而不是改参数，
    抽牌那一步要用同一个 RNG 通道按同样参数取，换写法会让分支之后的随机序列和实机对不上。
  - **选择悖论**：备选牌改成升级过的。挂在解析这次选择的入口上，备选牌是它的入参。
  - **小提琴**：`ShouldDraw` 改成对持有者永远为真（原版会挡掉一类抽牌）。这条求解器写在
    注册表里，走 0.2 那套摘掉再登记。
  - **钻石冠冕 / 轰鸣海螺**：整个机制换了。先把它们从求解器「参与回合开始结算的遗物」名单里
    摘掉（那两行做的还是原版的事），再把改版机制补回来 —— 冠冕是「本回合打牌不超过阈值就在
    回合结束给一层减伤」，海螺是「精英房前 3 张牌免费（能量和星都免）」。
  
  **不用做的**：领主之伞（`ModifyMaxEnergy`）走的是游戏自己的监听链，自动跟随；战锤、炼金匣
  这些求解器压根没镜像，改动也就无从谈起。
  
  **踩到一条结构性的事**：RebalancedSpire 是把钩子补在 `AbstractModel` 上的（前缀里判类型），
  被改的类型自己并没有重写那个虚方法。于是 (1) 求解器的注册表拒绝登记这种类型
  （`ValidateOverride` 要求真的重写了），只能改挂补丁；(2) `MirroredHookListenerFilter.Capture`
  一看见基类钩子被打了补丁就**整个关掉监听者过滤**，所以取值类钩子的「没登记就回落到监听者
  自己的实现」反而全都生效 —— 墨刃、领主之伞这些是这么白捡的。
- [?] **1.6 战斗外的遗物与奖励**（`AfterObtained`、`AfterCombatVictory`、`TryModifyRewards`、
  `AfterRoomEntered`、`AfterRewardTaken`）。不影响战斗模拟，只可能影响跨战斗估值。先记着。

## 阶段 2 起：逐角色

每个角色一批，做完给一个可测构建。括号里是这一批要镜像的牌。

- [x] **2. Necrobinder（8 张 OnPlay 已做，RightHandHand 的钩子待做）**：Afterlife、GraveWarden、PullAggro、ReaperForm、Seance、
  SicEm、Spur、Wisp、RightHandHand（`AfterCardPlayedLate`）。
  连带 Power：AfterlifePower、ReaperFormPlusPower、SicEmPlusPower、SoulWitherPower、
  GuardPower、MinionFakePower、LeechingHugPower。涉及召唤奥斯提和魂牌生成，最重的一批。
- [x] **3. Silent（8 张）**：HandTrick、HiddenDaggers、InfiniteBlades、MasterPlanner、
  PoisonedStab、WellLaidPlans（Untouchable、UpMySleeve 已完成）。
  连带 Power：InfiniteBladesPlusPower、MasterPlannerPlusPower、WellLaidPlansPlusPower。
  依赖 1.2（匕首带的两个新附魔）。
- [x] **4. Defect（7 张 OnPlay 已做，RocketPunch 的钩子待做）**：ConsumingShadow、Glasswork、Leap、Refract、Shatter、Spinner、
  Synchronize、RocketPunch（`AfterCardGeneratedForCombat`）。
  连带 Power：ConsumingShadowPlusPower、LeapPower、SpinnerPlusPower、SynchronizePlusPower。
  **依赖 0.2**（其中 5 张求解器已登记）和 0.3（Synchronize）。
- [x] **5. Regent**：ForegoneConclusion、HeirloomHammer。
  连带 Power：ForegoneConclusionPlusPower。（Glow、NeutronAegis 已完成）
- [x] **6. Ironclad**：ExpectAFight、ForgottenRitual、Tank。
  连带 Power：TankPlusPower。**依赖 0.3**（ExpectAFight）。
- [x] **7. 无色（2 张 OnPlay 已做，Bolas 的钩子待做）**：EternalArmor、Salvo、Bolas（`BeforeHandDraw`）。
  连带 Power：EternalArmorPower。**依赖 1.1**（镀甲）。
- [x] **8. Token / Status**：Wither（见 1.4）。（Fuel 已完成）

## 阶段 9：怪物与遭遇

- [ ] **9.1 出招表与招式实现**（先前判成「不用做」是**错的**，已更正）。
  出招表（`GenerateMoveStateMachine`）确实自动跟随，但 RebalancedSpire 还**整个替换了 50 个
  怪物的招式方法体**，一共 85 个招式带攻击以外的效果（加甲、上 Power、生成牌、召唤）。
  先前漏判是因为 ilspy 解不出这些补丁的 `[HarmonyPatch]` 参数，我按能解出来的那份表统计，
  只看到 5 个 —— 后来按**方法体**重扫才看出真实规模。
  伤害数值不用管（求解器按意图读，自动跟随），要逐条核的是那 85 个招式的非攻击部分。
  清单和用法见 `docs/monster-move-audit.md`。已核过并修好 9 个，剩下的还没逐条比。44 张 `GenerateMoveStateMachine` 理论上自动跟随（求解器读的是怪物身上
  活的那张表），但要实测抽查几个确认，特别是改了 `AfterAddedToRoom`（改血量/初始 Power）的那 22 个。
- [x] **9.2 被重写的单招**（差 `KnowledgeDemon.ChooseCurse` 一条）。求解器模拟敌人招式走
  `MonsterMoveEffects.Apply` 里一张按「怪物类型名 + 招式 id」的大表，不是注册表，所以挂前缀：
  - **缠绕**：3 层 → 2 层。
  - **瘴气**：格挡从固定 8 改成 8 + 自己当前敏捷（在偷敏捷之后、给回之前读，顺序照原样）。
  - **液化**：原版的流沙 4 和 6 张仓皇逃窜之外，多一层 5 的「长距离」。
  - **自爆**：炸之前先摘掉「乒乓」，所以自爆不反伤生成它的迷雾；伤害那半求解器本来就对。
  - **乒乓**（新 Power）：挂着它的怪被打死时反伤给生成者，伤害等于死者最大生命 ——
    不镜像的话求解器看不到「先清小怪」这条收益。走 `AfterDeathMirrors` 登记。
  - **惊惶**：改版除了起甲还给自己一层负力量。求解器登记过它，走 0.2 那套换掉。
  - **幻影复活**：治满那半求解器本来就对，补的是「按幻灭层数给自己等量负力量」。
  - 还差 `KnowledgeDemon.ChooseCurse`（选哪张诅咒进牌组）。
- [ ] **9.3 新 Boss Doormaker**。连带 `AttackCommand.TargetingRandomOpponents` 的改写
  （只在场上有 Doormaker 时生效）和 `OmnidynamicsPower`。新怪物求解器完全不认识。
- [ ] **9.4 新增卡牌**：CorpseExplosion、LimitBreak（连带 CorpseExplosionPower）。

## 阶段 10：地图外（默认不做，只记录）

事件（10 个 `GenerateInitialOptions`）、三位 Ancient 的选项、遗物池、地图生成、
`RunHistorySaveManager`。这些不参与战斗模拟。求解器的跨战斗路线估值里可能间接相关，
等前面做完再判断。

---

## 还差什么（2026-09-12 晚）

**33 张改动牌的 `OnPlay` 全部镜像完毕。** 剩下的是：

1. **新 Power 自己的钩子**。已补 14 个：SpinnerPlus、Afterlife、PingPong、SandsOfTime、
   DiamondDiadem（自动）、EternalArmor（纯标记）、ReaperFormPlus、SicEmPlus、MasterPlannerPlus、
   InfiniteBladesPlus、SynchronizePlus、ConsumingShadowPlus、CorpseExplosion、LeechingHug、
   SoulWither、WitheringPresencePlus。
   **没补的**：门匠的全能（Omnidynamics）、织机的制造者（Fabricator）、寄生棱镜的感染+
   （InfestedPlus）、亲随的守护（Guard）、假随从（MinionFake）、耕耘+ / 被耕耘（Plow*）、
   拜尔多尼斯的归还（ToItsOriginOwner）、幻灭（Disillusion）。
   这些全是**动作类钩子**，没登记会记一条未镜像风险、显示成红字，不会静默算错；
   它们的取值类钩子（伤害倍率、能不能被选中、费用修正）本来就会回落到 Power 自己的实现。
2. ~~三张只改了钩子的牌~~ —— RightHandHand 和 RocketPunch 已做（两张求解器都登记过，
   走 0.2 那套换掉）。**Bolas 不做**：它改的是 `BeforeHandDraw`，而求解器的
   `TriggerBeforeHandDraw` 只遍历 Power，**牌的这个钩子从来不分发** —— 原版 Bolas 在求解器里
   本来就没镜像，不是改动带来的新问题。要补得先在那一段里加上对牌的遍历，属于另一件事。
3. ~~两张新牌~~ —— CorpseExplosion、LimitBreak 已做（连带 CorpseExplosionPower）。
4. **新 Boss Doormaker** 及其 `AttackCommand.TargetingRandomOpponents`、OmnidynamicsPower。
5. `KnowledgeDemon.ChooseCurse`；饥饿 / 审视刚施加时的一次性感染和手牌上限修正。

## 验收

`tools/run-rebalanced-matrix.ps1` 八条，**2026-09-12 17:50 全部通过**（求解器
`06D43102`，适配层当日构建）。第一次跑是 4/8，挂掉的四条各自都是真问题，见 git 历史里
「矩阵抓到两处真问题」那一条。

## 进度

- 2026-09-12 立项。阶段 0.1、0.2、0.3、0.4 完成，只剩 0.5 验收框架。
  已镜像 7 张牌：**Fuel、Untouchable、Glow、UpMySleeve、NeutronAegis、Spinner、ExpectAFight**
  （后两张属于 Defect / Ironclad 批，提前做是为了验证 0.2 和 0.3 的机制）；
  2 个新 Power：**SpinnerPlusPower、AfterlifePower**。
  无头实跑确认三个补丁都挂上了、自检通过、零报错。
