# 怪物招式待核对清单

RebalancedSpire 替换掉方法体的怪物招式，**只列带攻击以外效果的那些**。伤害数值不用列：
求解器按意图读伤害，数值改了自动跟随；会算错的是加甲、上 Power、生成牌、召唤这类。

怎么用这张表：逐行拿「改版的方法体」和两样东西比 ——
原版 `sts2.dll` 里同名方法，以及求解器 `MonsterMoveEffects.Apply` 里
`("怪物类型名", "招式 ID")` 那一条。**三者一致就划掉**，不一致才需要在
`src/MonsterMirrors.cs` 里加一条前缀。

**已经核过并修好的（2026-09-12）**：缠绕、瘴气、液化地面、自爆、乒乓、胆小、幻象复活、
感染棱柱的辐射与脉动、恶咒（幽灵骑士）、汲取之拥与喷吐脓水（史莱姆狂战士）、思考（知识恶魔）、
苏醒（来生雕像）、快拳（拳击构造体）、狂怒（污泥旋转工艺者）、尖叫（咀嚼者）、分神（利齿之眼）、
喷火器（机甲骑士）、浓毒（异螨）、感染（异蛙寄生虫）、噪音（噪音机器人）、无法逃脱（永世沙漏）。

外加第二轮：猛击（活体盾）、充能（火箭）、践踏与增压（瀑布巨兽）、加大力度（永世沙漏）。

**求解器有镜像的那 30 条已经逐条比完了。** 比下来一致、不用动的有：
亲随的能量之舞、尼比特的切割、潜行蚁群的惯性、瀑布巨兽的加压之外那几条 ——
它们读的是怪物自己的静态值，改版没动那些值。

**还没核的是另外 33 条**：那些招式求解器本来就没有镜像（`MonsterMoveEffects.Supports`
返回假），非攻击效果在原版里也没被模拟，所以改版改了它们**不会让求解器更错** ——
意图会被标成 unsupported 显示出来。要不要补是另一个问题，不属于「跟上改版」。

**这张表的方向是单向的，要配一次反向扫描（2026-09-17 补）。**表里列的是「改版**加了**攻击以外
效果」的招式，所以改版**删掉**效果的招式根本不会出现在这里 —— 而求解器照原版口径照样算，
那才是会静默算错的方向。第二轮补掉的三条被清空的老招式是这么找出来的，但只覆盖了「整个方法体
被清空」的情况；**一招里保留了攻击、只删掉附带效果的**当时漏了。

漏掉的那一条是墨影幻灵的肢解：改版只剩那一刀，原版打完还往弃牌堆塞 3 张伤口，求解器
`MonsterMoveEffects` 里就是这么写的。2026-09-17 的实机问题包里表现成「状态对不上、整场反复重算」，
差异写得很明白：`C[5] expected=WOUND actual=<missing>`，弃牌堆比实机多 3 张。

反向扫描怎么做：把求解器 `MonsterMoveEffects` 的 `Supports` 门和那张 switch 里所有
`("怪物类型名", "招式 ID")` 抽出来（另外还有 `ApplyBeforeAttack` 里写死的活雾膨胀和偷牌兔偷牌），
和改版补过的怪物求交集，再逐条看改版那一招的方法体还做不做那件事。2026-09-17 全量跑过一遍，
除了肢解之外**没有别的漏判**：亲随的力量之舞、尼比特的切割、潜行蚁群的惯性、实验体的头骨猛击与
多重爪击（连 `ExtraMultiClawCount` 的自增都还在）、偷牌兔的偷窃（优先级表和 RNG 流都没换）
都和原版一致；活雾的膨胀和实验体的复活、三阶段割裂各有各的归口。

这张表是脚本生成的（`sweep`/`triage` 见 git 历史），不是手写的，所以可能有漏判：
方法体里通过别的静态方法间接调用命令的看不出来。真要定稿还得逐个读。

```
Aeonglass              AfterAddedToRoom         Apply<ArtifactPower>
Aeonglass              IncreasingIntensityMove  Apply<StrengthPower>, GainBlock
Aeonglass              WitheringMove            Apply<>, CardCmd.Afflict, CardCmd.PreviewCardPileAdd, CardPileCmd.AddGeneratedCardToCombat
BygoneEffigy           SlashMove                Apply<StrengthPower>
BygoneEffigy           WakeMove                 Apply<StrengthPower>
Byrdonis               AfterAddedToRoom         Apply<WeakPower>
Byrdonis               AngryMove                Apply<FrailPower>, Apply<TerritorialPower>, CardCmd.AfflictAndPreview
CeremonialBeast        FirstStampMove           Apply<PlowPlusPower>
CeremonialBeast        SecondStampMove          Apply<PlowPlusPower>
Chomper                ScreechMove              CardPileCmd.AddToCombatAndPreview
Crusher                EnlargingStrikeMove      Apply<WeakPower>
DecimillipedeSegment   AfterAddedToRoom         Apply<ReattachPower>
DecimillipedeSegment   BulkMove                 Apply<StrengthPower>
Entomancer             SpitMove                 Apply<PersonalHivePower>, Apply<StrengthPower>
Exoskeleton            AfterAddedToRoom         Apply<HardToKillPower>
EyeWithTeeth           DistractMove             CardPileCmd.AddToCombatAndPreview
Fabricator             AfterAddedToRoom         Apply<FabricatorPower>
Fabricator             SpawnBot                 Apply<MinionPower>, Damage
GasBomb                ExplodeMove              Kill, Remove<PingPongPower>
GlobeHead              AfterAddedToRoom         Apply<GalvanicPower>
HunterKiller           WeakGoopMove             Apply<WeakPower>
InfestedPrism          AfterAddedToRoom         Apply<TaintedPlusPower>
InfestedPrism          PulsateMove              Apply<StrengthPower>
KinFollower            AfterAddedToRoom         Apply<MinionFakePower>
KinFollower            EscapeMove               RemoveAllPowers
KinFollower            GuardFakeMove            GainBlock
KinFollower            GuardMove                Apply<GuardPower>, GainBlock
KinFollower            PowerDanceFakeMove       Heal
KinFollower            PowerDanceMove           Apply<StrengthPower>
KinFollower            RevengeDanceMove         Apply<StrengthPower>
KinPriest              AfterDeath               Apply<StrengthPower>
KinPriest              BreakUpMove              Apply<FrailPower>, Apply<WeakPower>
KinPriest              HealUpMove               Heal
KinPriest              PowerUpMove              Apply<StrengthPower>
KinPriest              ShieldUpMove             GainBlock
KnowledgeDemon         PonderMove               Apply<StrengthPower>, Heal
LivingFog              BloatMove                Apply<PingPongPower>
LivingShield           AfterAddedToRoom         Apply<RampartPower>
LivingShield           ShieldUpMove             GainBlock
LivingShield           SmashMove                Apply<StrengthPower>
MechaKnight            FlamethrowerMove         CardPileCmd.AddToCombatAndPreview
MysteriousKnight       AfterAddedToRoom         Apply<PlatingPower>, Apply<StrengthPower>
Myte                   ToxicMove                CardPileCmd.AddToCombatAndPreview
Nibbit                 SliceMove                GainBlock
Noisebot               NoiseMove                CardCmd.PreviewCardPileAdd, CardPileCmd.AddGeneratedCardToCombat
Parafright             AfterAddedToRoom         Apply<DisillusionPower>, Apply<IllusionPower>
PhrogParasite          AfterAddedToRoom         Apply<InfestedPlusPower>
PhrogParasite          InfectMove               CardPileCmd.AddToCombatAndPreview
PhrogParasite          ProliferationMove        Apply<InfestedPlusPower>, Apply<WeakPower>
PunchConstruct         AfterAddedToRoom         Apply<ArtifactPower>
PunchConstruct         FastPunchMove            Apply<WeakPower>
PunchConstruct         FightWithMe              Damage
Rocket                 ChargeUpMove             Apply<StrengthPower>
Rocket                 LaserMove                Damage
Rocket                 TargetingReticleMove     Apply<FrailPower>
ScrollOfBiting         AfterAddedToRoom         Apply<PaperCutsPower>
SewerClam              AfterAddedToRoom         Apply<PlatingPower>
SkulkingColony         InertiaMove              Apply<StrengthPower>
SkulkingColony         ZoomMove                 GainBlock
SlimedBerserker        LeechingHugMove          Apply<WeakPower>
SlimedBerserker        VomitIchorMove           Apply<LeechingHugPower>, CardPileCmd.AddToCombatAndPreview
SlitheringStrangler    ConstrictMove            Apply<ConstrictPower>
SludgeSpinner          RageMove                 Apply<StrengthPower>, GainBlock
SoulNexus              AfterAddedToRoom         Apply<>
SoulNexus              SoulMarkMove             Apply<VulnerablePower>, Remove<>
SpectralKnight         AfterAddedToRoom         Apply<IntangiblePower>
SpectralKnight         HexMove                  Apply<HexPower>, Apply<IntangiblePower>
SpectralKnight         SoulFlameMove            Apply<IntangiblePower>
SpectralKnight         SoulSlashMove            Apply<IntangiblePower>
TestSubject            AfterAddedToRoom         Apply<AdaptablePower>
TestSubject            BurningGrowlMove         Apply<StrengthPower>, CardPileCmd.AddToCombatAndPreview
TestSubject            GrowlMove                Apply<EnragePower>
TestSubject            RespawnMove              Apply<NemesisPower>, Apply<PainfulStabsPower>, Remove<AdaptablePower>, Remove<PainfulStabsPower>
TestSubject            SkullBashMove            Apply<VulnerablePower>
TheForgotten           MiasmaMove               Apply<DexterityPower>, GainBlock
TheInsatiable          LiquifyMove              Apply<>, Apply<LongDistancePower>, CardCmd.PreviewCardPileAdd, CardPileCmd.AddGeneratedCardToCombat
ThievingHopper         AfterAddedToRoom         Apply<EscapeArtistPower>
ThievingHopper         AttackMove               Apply<WeakPower>
ThievingHopper         ThieveryMove             Apply<>, CardPileCmd.RemoveFromCombat
ToughEgg               AfterAddedToRoom         Apply<HatchPower>
Vantom                 AfterAddedToRoom         Apply<PainfulStabsPower>, Apply<SlipperyPower>
Vantom                 InkBlotMove              Apply<SlipperyPower>, Apply<StrengthPower>, Apply<WeakPower>
VineShambler           GraspingVinesMove        Apply<TangledPower>, GainBlock
WaterfallGiant         PressureUpMove           Apply<SteamEruptionPower>
WaterfallGiant         StompMove                Apply<WeakPower>

�� 85 ����ʽ�����������Ч��
```


## 第三个方向：入场钩子只在「战斗中新生成的怪」上算错（2026-09-18 补）

上面两轮扫的都是**招式**。改版另有 22 处补丁打在 `AfterAddedToRoom`（怪物进场那一刻），
这一类的特殊之处是：**开局就在场的怪物，求解器是照实机现场捕获的**，实机挂了什么层数就是什么层数，
改版怎么改都不会错。只有**战斗中新生成**的那些，才会走求解器自己那份入场层数
（`MonsterSpawnSupport.ApplyNativeEntrancePowers` 里写死的 switch），这时候改版和原版的差量才会露出来。

所以核对的范围是这个交集：**改版补过 `AfterAddedToRoom` 的怪物 ∩ 求解器会在战斗中生成的怪物**。
后者只有这些（`Spawn<T>` / `Create<T>` 的调用点）：气态炸弹、利齿之眼、胧光怪的幻象、结实的卵、
双尾鼠的援军、电击机器人、巨斧机器人、几种小鬼、噪音机器人、扭动虫。

2026-09-18 全量比过一遍，交集只有两只：

| 怪物 | 改版的入场 | 求解器的入场 | 结论 |
| --- | --- | --- | --- |
| `ToughEgg`（结实的卵） | `HatchPower` = `敌方回合 ? 3 : 2` | `敌方回合 ? 2 : 1` | **差 1，已修**，见 `src/ToughEggHatchPatch.cs` |
| `Parafright`（寄生惧魔） | `IllusionPower` 1 + `DisillusionPower` 4 | `IllusionPower` 1 + `MinionPower` 1 | 一致：那 4 层幻灭是生成它的那一招（`TheObscura/ILLUSION_MOVE`）的镜像现补的 |

结实的卵那一条 2026-09-18 的问题包里表现成一场战斗重算 3 次，每次都是
`P[n] HATCH_POWER expected=1 actual=2` —— 每下一个蛋差一层。改版同时给它的出招表前面插了一个
空的 `STUN_MOVE`（原版是 `HATCH_MOVE → NIBBLE_MOVE`），那一半不用补：意图是照实机的状态机现读的，
问题包里的 `FORECAST` 把 `STUN_MOVE → HATCH_MOVE → NIBBLE_MOVE` 排得和实机一样。

## 第四个方向：随机通道和插入位置必须逐处对齐（2026-09-18 补）

和「算错数值」并列的另一类静默错误：**镜像动了一条会进指纹的随机通道，而实机没动，或者反过来。**
求解器在战斗内镜像九条运行期随机通道（洗牌、造牌、选牌、费用、目标、充能球、药水、怪物 AI、杂项），
它们**全部进回合边界的比对**，所以偏一个数就是整场重算，哪怕牌面结果恰好一样。

判据两条，每一处「把牌放进牌堆」或「随机取一个」的镜像都要过：

1. **位置参数要照抄。**`CardPilePosition.Random` 会从洗牌通道取一个数，`Bottom` / `Top` 一个都不取。
   改版里用随机位置的一共 6 处（凋萎的无法逃脱、守墓人的魂、永世沙漏的凋萎、贪得无厌的惊慌逃窜、
   失礼之茶、凋萎被变形后的补发），2026-09-18 逐处比过：**只有无法逃脱那一处写成了牌堆底，已修**。
2. **通道要同一条，取数个数也要一样。**鬼火那一处原来用「选牌通道 + UnstableShuffle」顶替，
   而实机用的是 `PlayerRng.Transformations` —— 求解器根本不镜像这条通道。于是抽牌堆里有 n 个魂时，
   预测白烧 n−1 个选牌数、实机一个都没烧。2026-09-18 帝皇蟹的问题包差的正是 7 个
   （`R.card_selection expected=15 actual=8`）。改法不是换通道，而是**一个数都不取**，
   并在魂多于一个时记一条未镜像风险：变的是哪一个魂，求解器无从得知，该显示成红字。
