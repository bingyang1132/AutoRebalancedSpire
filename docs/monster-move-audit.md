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
