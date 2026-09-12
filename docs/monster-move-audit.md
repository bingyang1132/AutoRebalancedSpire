# 怪物招式待核对清单

RebalancedSpire 替换掉方法体的怪物招式，**只列带攻击以外效果的那些**。伤害数值不用列：
求解器按意图读伤害，数值改了自动跟随；会算错的是加甲、上 Power、生成牌、召唤这类。

怎么用这张表：逐行拿「改版的方法体」和两样东西比 ——
原版 `sts2.dll` 里同名方法，以及求解器 `MonsterMoveEffects.Apply` 里
`("怪物类型名", "招式 ID")` 那一条。**三者一致就划掉**，不一致才需要在
`src/MonsterMirrors.cs` 里加一条前缀。

**已经核过并修好的（2026-09-12）**：缠绕、瘴气、液化、自爆、乒乓、惊惶、幻影复活、
寄生棱镜的辐射与脉动、咒缚（幽灵骑士）、吸取拥抱与呕吐黏液（黏液狂战士）、沉思（知识恶魔）、
苏醒（往世雕像）、快拳（拳击构造体）、狂怒（污泥纺纱者）、尖叫（咀嚼者）、分神（利齿之眼）、
喷火（机械骑士）、剧毒（螨）、感染（蛙类寄生虫）、噪音（噪音机器人）、凋零（永世沙漏）。

**还没核的**：下面列出来的其余条目。判据是三方对照 —— 一致就划掉。

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
