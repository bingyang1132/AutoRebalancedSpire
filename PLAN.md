# AutoRebalancedSpire 总清单

对着 RebalancedSpire `v0.3.10-beta`（DLL sha256 `cce7a199…dcd2b`）全量反编译列出来的。
顺序是**先做影响全局的，再逐个角色**；每完成一个角色出一个可测的构建。

判据统一：**数据层改动（`CanonicalVars` / 关键字 / 费用 / 出招表）求解器读活模型，自动跟随，
不进这张清单。** 进清单的只有「求解器按原版语义算、而实机已经不那样了」的东西。

图例：`[x]` 已完成 · `[ ]` 待做 · `[?]` 待评估（先读代码再决定要不要做）

> **玩家须知**：请在 RebalancedSpire 的设置里关掉「Doormaker」（门匠）。那是改版新加的第三章
> Boss，本适配层没有为它写模拟，详见下面「玩家须知」一节。除它之外，改版的全部战斗内改动
> 都已镜像。

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
  清单和用法见 `docs/monster-move-audit.md`。**求解器有镜像的 30 条已经逐条比完，真有差异的 27 条全部修好**；
  另外 33 条求解器本来就没镜像（原版也没模拟），改版改了不会让它更错，意图会被标成 unsupported。44 张 `GenerateMoveStateMachine` 理论上自动跟随（求解器读的是怪物身上
  活的那张表），但要实测抽查几个确认，特别是改了 `AfterAddedToRoom`（改血量/初始 Power）的那 22 个。
- [x] **9.2 被重写的单招**。求解器模拟敌人招式走
  `MonsterMoveEffects.Apply` 里一张按「怪物类型名 + 招式 id」的大表，不是注册表，所以挂前缀：
  - **缠绕**：3 层 → 2 层。
  - **瘴气**：格挡从固定 8 改成 8 + 自己当前敏捷（在偷敏捷之后、给回之前读，顺序照原样）。
  - **液化**：原版的流沙 4 和 6 张仓皇逃窜之外，多一层 5 的「长距离」。
  - **自爆**：炸之前先摘掉「乒乓」，所以自爆不反伤生成它的迷雾；伤害那半求解器本来就对。
  - **乒乓**（新 Power）：挂着它的怪被打死时反伤给生成者，伤害等于死者最大生命 ——
    不镜像的话求解器看不到「先清小怪」这条收益。走 `AfterDeathMirrors` 登记。
  - **惊惶**：改版除了起甲还给自己一层负力量。求解器登记过它，走 0.2 那套换掉。
  - **幻影复活**：治满那半求解器本来就对，补的是「按幻灭层数给自己等量负力量」。
  - **知识恶魔的崩解**：三次给的层数从 6/7/8 改成 4/6/8。崩解是回合结束按层数扣真实生命的，
    几层的误差直接落在「这条路线会不会把自己打死」上。
  - **26 条改版新加的招式 id**：第二轮补的，见下面「还差什么」。
- [-] **9.3 新 Boss Doormaker**。连带 `AttackCommand.TargetingRandomOpponents` 的改写
  （只在场上有 Doormaker 时生效）和 `OmnidynamicsPower`。新怪物求解器完全不认识。
  **判定为边界，不做**；请玩家在 RebalancedSpire 设置里关掉它，见「玩家须知」一节。
- [ ] **9.4 新增卡牌**：CorpseExplosion、LimitBreak（连带 CorpseExplosionPower）。

## 阶段 10：地图外（默认不做，只记录）

事件（10 个 `GenerateInitialOptions`）、三位 Ancient 的选项、遗物池、地图生成、
`RunHistorySaveManager`。这些不参与战斗模拟。求解器的跨战斗路线估值里可能间接相关，
等前面做完再判断。

---

## 还差什么（2026-09-12，三轮之后）

第二轮的目标是「在平衡尖塔下像原版一样正确运行」，所以按**会不会骗人**重新排了一遍：
静默算错的先做，红字提示的其次，原版下同样不支持的不算回退。

### 已补完：原本会静默算错的

求解器在这些时点没有第三方入口，认不出的来源直接跳过、**不记风险**，所以不补就是
「给出一条看起来可信的错路线」。

1. **必然结局+**：回合开始从抽牌堆挑几张放牌堆顶。走求解器通用的选牌通道
   （`MoveToDrawTop`），分支进 beam、写进计划、部署时应答原生选牌页。
2. **周密计划+**：回合结束清手牌前挑最多 N 张一次性保留。求解器整个 `BeforeFlush`
   时点都没有（它的注释说原版唯一的监听者用不到），挂在 `RunPhaseOne` 后面。
3. **手牌上限**：求解器建根时冻结，原版没有会变的来源，改版有两个（无尽之刃+、审视）。
4. **饥饿 / 审视**：施加时整批感染已在场的牌、移除时整批清除、玩家回合结束衰减。
5. **污染+（寄生棱镜）**：整套机制换了，是那场精英战最主要的费用来源。
6. **死神形态+** 比原版多的那次提前收割。
7. **三条被改空的老招式**：魔法骑士备战一二、Vantom 蓄势 —— 求解器照原版口径多给格挡和力量。
8. **组装师每造一台机器人的自伤**（1/15 最大生命）。
9. **寄生蛙精英死后生几只蠕虫**按「寄生+」层数算，并登记进终局约束 ——
   不补这条会把没打完的战斗当成已胜。
10. **知识恶魔的崩解**：6/7/8 改成 4/6/8。

### 已补完：原本会显示成红字的

11. **26 条改版新加的招式 id**：求解器那张 `Supports` 表里没有，整条出招被标成「不支持」。
    效果逐条镜像 + 登记，涵盖信众整套（祭司召人 / 强化 / 护盾 / 治疗 / 击溃，
    信徒守护 / 复仇之舞 / 假守护 / 假舞 / 逃跑）、寄生蛙增殖一二三与感染二、
    拜尔多尼斯发怒、组装师组装与逃跑、祭祀之兽两次踏、试验体咆哮、魂枢魂印、
    拳击装置「打我」等。
12. **三个新 Power 的被打换招**：耕耘+、组装师低血逃跑、假随从同伴全灭后逃跑。

### 第三轮补的

13. **组装师「随从死了就补造一台」**。上一轮判成「做不了」是错的：`IntendsToAttack` 的
    定义就是「下一招的意图里有攻击或致命一击」，分支里那一招求解器自己有
    （`CurrentMonsterMove`），照同一个定义算即可，不需要去读实机模型。
    `CanFabricate`（同侧活着的不到 4 个）同理。
    不补的后果是**清小怪的收益被算反** —— 求解器以为打死机器人是纯赚，
    看不到它下回合直接补一台、原本那一刀也不挨了。
14. **流星锤每次飞回手里伤害永久 +3**。上一轮说「求解器从来不遍历牌的 `BeforeHandDraw`」
    是对的，但结论错了：回手那一半求解器**另有**一套 `_returnToHandNextTurn`，
    名单写死为 `card is Bolas or ThrummingHatchet`，已经模拟了。真正缺的只有涨伤害那一句。
    这条是静默的，而且越到后面差得越多。
15. **十三条求解器原版下也不支持的怪物招式**。原版同样不模拟，所以严格说不算回退，
    但补掉之后这几只怪在改版下反而比原版算得准：
    - 静默的一条：**火箭的激光**打完自己掉 10 点（不可格挡、不吃增益）。
      它的意图只有攻击，求解器不会标红，那 10 点自伤完全没算。
    - 红字的：往昔雕像斩击的 3 力量、粉碎者增大打击、火箭瞄准镜、千足虫节增厚与紧缚、
      潜行群落疾冲的加甲、幽魂骑士魂焰魂斩的虚无、Vantom 墨斑整套。
    - 只换了 id、实现还是原版的：祭祀之兽的第一/第二次犁击、千足虫节的重接
      （重接求解器**已经**完整模拟了，只是没进 `Supports` 那张表，于是「治疗」意图被标红）。

### 第四轮补的：把全部补丁按「补的是哪个方法」重扫一遍

前三轮只扫了牌和怪物。第四轮把改版的**每一个** Harmony 补丁按补丁目标过了一遍，
发现整块没查过的地方（遗物 27 个、药水、Ancients、打在原版 Power 上的补丁）。补的：

16. **战史课程**（静默）：求解器重放「上回合最后一张**攻击**」，改版放宽成**攻击或技能**。
    两处都要改 —— 记录当回合候选的那一处，和「建根时还没物化」时回原始战斗历史重找的
    那一处，过滤条件是各写一份的。
17. **低语耳环**（静默，本适配层最严重的一处单点偏差）：原版是**第一回合**直接连打 13 张，
    求解器为此专门写了一整段；改版把原版那一段**整个关掉**，换成「本场累计花掉 13 点能量
    之后蓄势，你下一张打出的牌结算完触发那一轮连打，一场只触发一次」。
    所以求解器两头都错：第一回合替玩家打了 13 张实机不会打的牌，该触发的那次又看不见。
    连打本身不重写，复用求解器那段循环（上限两边都是 13 张）；
    「攒了多少能量、有没有蓄势」走求解器给第三方留的 `ModelPredictionStateMirrors`，
    它会替我们做分叉拷贝并把值写进搜索指纹和续接戳。
18. **三瓶新药水**（骨茶、余烬茶、失礼茶）：都进事件药水池，战斗里都能喝。
    不镜像只会记红字不会算错，但求解器**不会把它们排进路线**，等于玩家白带。

### 同一轮查完、确认不用做的

- **战锤**：`AfterDamageGiven` 是改版补在 `AbstractModel` 上的，战锤自己并没有重写那个
  虚方法，所以求解器的注册表判定是「未重写」，既不分发也不记风险；而它的效果
  （按杀敌数升级牌）发生在战斗**结束之后**。战斗内没有任何影响。
- **小提琴**：`ShouldDraw` 是取值钩子自动跟随；`AfterPreventingDraw` 改版改成空操作，
  求解器本来就不跑那个时点，两边一致；手牌上限那一份来自单例，建根时已经捕获。
- **领主之伞**：`ModifyMaxEnergy` 取值钩子自动跟随，`CanonicalVars` 是数据层。
- **炼金匣**：只记「上次喝的是哪瓶」，战斗结束后补发。战斗内无效果。
- **Vakuu**：改的是 Ancient 的遗物三选一，战斗外。（和求解器里同名的「Vakuu 选牌器」无关，
  那是连打时的固定选牌策略。）
- **墨影附魔、滚石、惊惶、坚韧**：分别是取值钩子 / 数据层 / 已镜像 / 纯本地化。
- **污染病症的可施加判据**：`CanAfflictCardType` / `CanAfflictUnplayableCards` 收窄了范围，
  而我们那份污染+ 的镜像本来就是照它自己的挑牌条件写的（攻击或技能、非「不可打出」、
  身上没有别的病症），两边一致。
- 地图、事件、商人、存档、新手引导、主菜单：战斗外。

### 仍然没做：只剩门匠 Boss

见下面「玩家须知」和「边界」。

## 第五个没有第三方入口的地方：字符串字段的语义分类

玩家报「感染棱柱识别不了」。根因不在那场战斗，也不在我们的镜像里：

求解器给动态变量算指纹时，遇到 `StringVar` 会去问
`SemanticStateFieldPolicy.ClassifyString(类型, 字段名)` 这个字段算不算「影响结算」。
那是一张写死的白名单，**认不出来就抛异常**。上游这么写是为了自己加新 Power 时不会漏分类，
但对第三方 Power 来说，后果是整场战斗算不出来。

改版有**七个**新 Power 带这种变量，每一个对应一场战斗：

| Power | 字段 | 战斗 |
|---|---|---|
| `TaintedPlusPower` | `AfflictionTitle` | 感染棱柱精英 |
| `GuardPower` | `MasterName` | 信众 |
| `InfestedPlusPower` | `PhrogParasite` | 寄生蛙精英 |
| `LeechingHugPower` | `Slimed`、`SlimedBerserker` | 黏液狂战士 |
| `LongDistancePower` | `TheInsatiable` | 贪食者 |
| `PingPongPower` | `LivingFog` | 活体迷雾 |
| `SoulWitherPower` | `SoulNexus` | 魂枢 |

七个字段全是「某张牌／某只怪的名字」，拿去填提示文字用的，一个都不参与结算 ——
和上游自己已经列进白名单的 `VitalSparkPower.AfflictionTitle` 是同一类东西。

补法见 `src/StringFieldPolicyPatch.cs`。名单外的字段如果来自 RebalancedSpire，
**按只用于显示处理并记一条警告**，不跟着抛：字符串在这套模型里只进本地化插值，
没有任何结算读它，而抛出去的代价是整场战斗用不了。

这条同时也是问题包导不出来的原因 —— `ContinuationStamp` 走同一个分类器。

## 性能：开关**必须**自己缓存

`RebalancedSpireSettingsStore.Settings` 这个属性每读一次都是

```csharp
ModDataStore.For("RebalancedSpire").CreateCache<RebalancedSpireSettings>("settings").Value
```

而 `CreateCache` 字面意思就是 `new ModDataStoreCache<T>(...)`：每次调用新建一个缓存对象、
一把锁，并且**往数据仓库的 `EntryReloaded` 事件上再挂一个处理器**，还从不释放。
于是每读一次开关就多几笔分配、事件订阅列表长一截 —— 越跑越慢，而且是复利。

RebalancedSpire 自己没事，它只在每个补丁类的 `static readonly bool Disabled` 里读一次；
踩坑的是适配层：出牌、怪物出招、意图预测这些每秒成千上万次的路径上都在读。

同一个四回合场景（无头 harness，并行度钉死 1）：

| | 搜索耗时 | 分配 |
|---|---|---|
| 原版（两个 mod 都移出） | 816 ms | 66 MB |
| 只装平衡尖塔 | 955 ms | 67 MB |
| + 适配层，开关不缓存 | 1139 ms | **644 MB** |
| + 适配层，开关缓存一次 | 973 ms | 70 MB |

所以：**热路径上一律用 `AdapterSettings.Current`，不要用 `RebalancedSpireSettingsStore.Settings`。**
只有 `AdapterSelfCheck` 里那一处「读得到吗」保留原样。

顺带量出来的另一件事，**适配层修不了**：平衡尖塔自己就要 +17%。求解器的
`MirroredHookListenerFilter.Capture()` 一旦发现**任何**一个 `AbstractModel` 基类钩子上挂了
Harmony 补丁，就整个关掉监听者过滤；而平衡尖塔补了好几个。之后每次钩子分发都要走完整份
监听者名单。这是上游的事。

## 玩家须知：把「门匠」关掉

**在 RebalancedSpire 的设置里关掉「Doormaker」。** 本适配层没有为这只新 Boss 写模拟，
关掉之后它不进第三章的 Boss 池，连带的随机目标改写（`AttackCommand.TargetingRandomOpponents`）
和「全能」也一起不生效 —— 整块没适配的区域就没了，其余内容一切照常。

不关也不会出错的路线：求解器遇到它会把出招标成「不支持」，显示红字，不会给出看似可信的
错路线。只是那一场用不了求解器。适配层加载时也会在日志里提醒一次。

这里只提醒，**不替玩家改设置** —— 本 mod 声明了 `affects_gameplay: false`，
自己去动别人的开关会让这句话变成假的。

## 边界：哪些东西故意不做

本适配要解决的问题是「求解器按原版语义算，而实机已经不那样了」。**新内容不属于这个问题**：
求解器对它不认识的东西本来就会显式标出来，不会给出一条看似可信的错路线。

- **新 Boss 门匠（Doormaker）**：两半身、互相搬运 Power、自带召唤意图的全新怪物。
  它「关着」的时候会把自己的最大和当前生命都设成 999999999、把 `HpDisplay` 换成门、
  用 `ShouldAllowHitting` 挡住选中，开门时再把原来的生命和暂存的 Power 逐个搬回来。
  求解器没有「血条是假的」这个概念，要镜像就得先在求解器里造一套生命遮罩机制 ——
  那是上游的事，不是适配层能钉在外面的补丁。连带 `AttackCommand.TargetingRandomOpponents`
  的改写（只在场上有门匠时生效）和 `OmnidynamicsPower`（纯视觉，翻转贴图朝向）同理。
  处理办法见上面「玩家须知」。
- **`GuardPower.AfterSideTurnEnd`**：它的条件是「side 不是敌方 且 参与者里有自己」，
  而自己是怪物 —— 玩家侧结束时参与者只有玩家，敌方侧结束时第一个条件不成立。
  这条钩子在实机里一次都不会触发，镜像成「什么都不做」才是对的。
- **`ToItsOriginOwnerPower`**：战斗结束后加一次卡牌奖励、清掉手里的蛋。战斗外的事。
- **饥饿 / 审视同时在场、其中一个被移除**时，被清掉的那批牌会在下一次归一化里被另一个
  补上病症，实机不会（实机只在牌进场时感染）。求解器自己那条诅咒 / 缠绕 / 耳鸣的链就是
  这个形状，跟着它走，不另起一套。
- **求解器把回合边界选牌的效果名直接按英文枚举名显示**（`MoveToDrawTop`、`ApplyRetain`）。
  原版的必然结局走同一条路、显示的也是英文，不是改动带来的回退，属于上游的文案问题。

## 验收

`tools/run-rebalanced-matrix.ps1` **十五条，2026-09-12 22:51 全部通过**（求解器 `06D43102`，
适配层当日构建）。

前八条盯单张牌和全局规则；第二轮加的三条盯**通道本身走不走得通**（必然结局+ 的回合开始选牌、
周密计划+ 的回合结束选牌、无尽之刃+ 让手牌上限随分支变）；第四轮加的两条盯遗物：

- `RS-WHISPERING-EARRING-NO-TURN1-AUTOPLAY` 断言**原版那一段真的被拦住了** ——
  耳环在身上、第一回合，拦漏的话求解器会替玩家连打 13 张、手牌被清空，
  起防峰值必然不是断言的 8。用的牌和 `RS-UNTOUCHABLE-REPEAT-BLOCK` 是同一张，
  所以这条挂掉只可能是耳环那一段的问题。
- `RS-HISTORY-COURSE-SKILL-REPLAY` 盯两处补完之后整条搜索不抛。

最后两条盯的是「字符串字段分类」那个坑：一条直接注入 `TaintedPlusPower`，
一条跑真实的感染棱柱精英战。**把 `StringFieldPolicyPatch` 摘掉之后这两条会超时失败** ——
这个 A/B 做过了，否则「通过」什么都不证明：第一次跑的那个场景里根本没有出问题的那个 Power。

跑之前三个环境变量都要设（`COMBATSOLVER_HEADLESS_ROOT`、`COMBATSOLVER_HEADLESS_HOST_ROOT`、
`NUGET_PACKAGES`）。脚本开头会先拦一道：缺了的话之前跑出来是「全挂」，
看上去像镜像写错了，实际只是沙箱路径没设。

## 进度

- 2026-09-12 立项。阶段 0.1、0.2、0.3、0.4 完成，只剩 0.5 验收框架。
  已镜像 7 张牌：**Fuel、Untouchable、Glow、UpMySleeve、NeutronAegis、Spinner、ExpectAFight**
  （后两张属于 Defect / Ironclad 批，提前做是为了验证 0.2 和 0.3 的机制）；
  2 个新 Power：**SpinnerPlusPower、AfterlifePower**。
  无头实跑确认三个补丁都挂上了、自检通过、零报错。
