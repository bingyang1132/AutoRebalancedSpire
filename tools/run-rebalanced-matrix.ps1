#requires -Version 7.0
<#
  平衡尖塔适配层的验收矩阵。

  设计原则和 AutoWatcher 那份一样：每条用例都要「只有镜像正确才能通过」。

  这里最常用的断言是 -ExpectedInitialUnmirroredCount 0（求解器自己不得报告任何未镜像效果）。
  对本适配层来说这一条比在观者那边更硬：RebalancedSpire 改过的牌只要没被我们镜像，求解器在
  建根时就会因为「牌的 OnPlay 上有第三方补丁」整场拒绝；镜像了但漏了效果，则会记未镜像风险。
  两种都会让这条断言挂掉。

  能用算术自己验的地方就用算术（格挡数值、正好击杀的回合），不要只验「跑起来不报错」。

  用法：
    pwsh -NoProfile -File tools\run-rebalanced-matrix.ps1
    pwsh -NoProfile -File tools\run-rebalanced-matrix.ps1 -Only RS-UNTOUCHABLE-REPEAT-BLOCK

  前提：
    1. mods/ 里要有 CombatSolver、RebalancedSpire、AutoRebalancedSpire，
       并且**不能有 Sts2RebalanceBeta** —— 那个 mod 和 RebalancedSpire 都替换了燃料和辉光的
       OnPlay，两个前缀互相覆盖，测出来的东西不算数。
    2. 改过 mod 之后先 Stop-Process -Name SlayTheSpire2，否则 harness 会复用旧进程。
       本脚本开头会自动杀，每条用例也都用独立进程（-ExitOnComplete）。

  牌用**类型名**指称（Untouchable、Glow、…）。harness 的模型解析除了 Id 也认类型名，
  省掉一层「这张牌的 Id 到底是什么」的查证。
#>
param(
    [string]$SolverRepo = "E:\Modding\SlayTheSpire2\CombatSolver",
    [string]$GameRoot = "D:\Sponsored\Steam\steamapps\common\Slay the Spire 2",
    [string]$RitsuWorkshopRoot = "D:\Sponsored\Steam\steamapps\workshop\content\2868840\3747602295",
    [string]$Only = "",
    # 标签按「哪一层改动可能弄坏它」划分：
    #   cards  单张牌的镜像    powers 新 Power 的钩子    relics 遗物
    #   newcards 新加的牌      global 全局规则（镀甲衰减、附魔、病症）
    [string]$Tag = "",
    [string]$ProgressPath = "",
    [int]$MaxTotalMinutes = 30,
    [switch]$NoRestart
)

$ErrorActionPreference = "Stop"
$runner = Join-Path $SolverRepo "tools\run-unattended-test.ps1"
if (-not (Test-Path -LiteralPath $runner)) { throw "找不到 harness：$runner" }

# harness 要求这三个环境变量，缺了会在每条用例里立刻失败。不先拦的话，跑出来的是
# 「8 条全挂」，看上去像镜像写错了，实际只是沙箱路径没设 —— 第一次遇上白查了一轮。
foreach ($requiredEnv in @("COMBATSOLVER_HEADLESS_ROOT", "COMBATSOLVER_HEADLESS_HOST_ROOT", "NUGET_PACKAGES")) {
    if (-not (Get-Item -LiteralPath "env:$requiredEnv" -ErrorAction SilentlyContinue)) {
        throw "环境变量 $requiredEnv 没设。三个都要设：COMBATSOLVER_HEADLESS_ROOT、COMBATSOLVER_HEADLESS_HOST_ROOT、NUGET_PACKAGES。"
    }
}

# 和 AutoWatcher 那份同一个理由：harness 跑的是求解器仓库的构建产物，而适配层是照着游戏
# mods/ 里那份编译的。两份不一样时适配层在 harness 里加载不上，每条用例都会以「不兼容」挂掉，
# 跑完一小时只告诉你「全都没过」。开跑前先对一次内容哈希。
$solverBuildDll = Join-Path $SolverRepo ".godot\mono\temp\bin\Release\CombatSolver.dll"
$solverDeployedDll = Join-Path $GameRoot "mods\CombatSolver\CombatSolver.dll"
foreach ($required in @($solverBuildDll, $solverDeployedDll)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "找不到求解器：$required" }
}
$buildHash = (Get-FileHash -LiteralPath $solverBuildDll -Algorithm SHA256).Hash
$deployedHash = (Get-FileHash -LiteralPath $solverDeployedDll -Algorithm SHA256).Hash
if ($buildHash -ne $deployedHash) {
    throw "求解器的构建产物和部署不一致，先在求解器仓库跑一次 dotnet build -c Release 再来。"
}
$buildVersion = [Reflection.AssemblyName]::GetAssemblyName($solverBuildDll).Version
Write-Host "求解器 $buildVersion 构建产物与部署一致（$($buildHash.Substring(0, 12))）" -ForegroundColor DarkGray

# 撞车检查：这两个 mod 同时在场时测出来的东西不算数，直接拦住。
$conflict = Join-Path $GameRoot "mods\Sts2RebalanceBeta"
if (Test-Path -LiteralPath $conflict) {
    throw "mods/ 里还有 Sts2RebalanceBeta，它和 RebalancedSpire 都替换了燃料和辉光的 OnPlay。先把它挪走。"
}

if (-not $ProgressPath) {
    $ProgressPath = Join-Path $PSScriptRoot "..\.matrix-progress.txt"
}
$ProgressPath = [IO.Path]::GetFullPath($ProgressPath)
function Write-MatrixProgress([string]$line) {
    $stamped = "{0} {1}" -f (Get-Date -Format "HH:mm:ss"), $line
    Add-Content -LiteralPath $ProgressPath -Value $stamped -Encoding UTF8
    Write-Host $stamped
}
Set-Content -LiteralPath $ProgressPath -Value "" -Encoding UTF8

function Hand([string[]]$cardIds) {
    ($cardIds | Group-Object | ForEach-Object {
        [pscustomobject]@{ cardId = $_.Name; pile = "Hand"; count = $_.Count }
    }) | ConvertTo-Json -Compress -AsArray
}

$cases = @(
    @{
        # 改版的不可触碰是「4 点格挡重复 2 次」= 8。原版是一次性的一份格挡，
        # 所以只要镜像没接管，这条就达不到 8。
        Id = "RS-UNTOUCHABLE-REPEAT-BLOCK"
        Tags = @("cards")
        Why = "不可触碰：4 格挡 × 重复 2 次 = 8。"
        Args = @(
            "-EnemyCurrentHp", "60", "-ClearPlayerPiles", "-InitialPlayerEnergy", "3",
            "-CardsJson", (Hand @("Untouchable")),
            "-ExpectedInitialMaxBlockAtLeast", "8",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 永恒护甲给 11 点镀甲，外加一层「永恒护甲」标记（让镀甲不再每回合衰减）。
        # 这里不断言格挡数值：镀甲是在回合结束前才转成格挡的，而
        # -ExpectedInitialMaxBlockAtLeast 量的是出牌阶段的峰值，两者不是一回事 ——
        # 试过断言 11，挂的是口径不是镜像。
        Id = "RS-ETERNAL-ARMOR-PLATING"
        Tags = @("cards", "global")
        Why = "永恒护甲：11 点镀甲 + 一层不衰减标记。"
        Args = @(
            "-EnemyCurrentHp", "60", "-ClearPlayerPiles", "-InitialPlayerEnergy", "3",
            "-CardsJson", (Hand @("EternalArmor")),
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        Id = "RS-FUEL-ENERGY-DRAW"
        Tags = @("cards")
        Why = "燃料改成了给能量 + 抽牌，不再是转化手牌里的状态牌。"
        Args = @(
            "-EnemyCurrentHp", "60", "-ClearPlayerPiles", "-InitialPlayerEnergy", "3",
            "-CardsJson", (Hand @("Fuel")),
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        Id = "RS-GLOW-STARS-DRAW"
        Tags = @("cards")
        Why = "辉光去掉了原版那层下回合多抽，只留当场的星和抽牌。"
        Args = @(
            "-EnemyCurrentHp", "60", "-ClearPlayerPiles", "-InitialPlayerEnergy", "3",
            "-CardsJson", (Hand @("Glow")),
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 纺纱是求解器自己也登记了 bespoke 镜像的五张之一，这条盯的是「改写机制」有没有生效。
        Id = "RS-SPINNER-REPLACED-MIRROR"
        Tags = @("cards", "powers")
        Why = "纺纱换成了纺纱+，求解器原有的镜像必须被换掉。"
        Args = @(
            "-EnemyCurrentHp", "60", "-ClearPlayerPiles", "-InitialPlayerEnergy", "3",
            "-CardsJson", (Hand @("Spinner")),
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        Id = "RS-FORGOTTEN-RITUAL-ENERGY"
        Tags = @("cards")
        Why = "遗忘仪式：本回合消耗过牌才给能量，而且自己本场越打越贵。"
        Args = @(
            "-EnemyCurrentHp", "60", "-ClearPlayerPiles", "-InitialPlayerEnergy", "3",
            "-CardsJson", (Hand @("ForgottenRitual")),
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        Id = "RS-LIMIT-BREAK-STRENGTH"
        Tags = @("newcards")
        Why = "极限突破是新加的牌：先给一点力量，再把当前力量翻倍。"
        Args = @(
            "-EnemyCurrentHp", "60", "-ClearPlayerPiles", "-InitialPlayerEnergy", "3",
            "-CardsJson", (Hand @("LimitBreak")),
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        Id = "RS-CORPSE-EXPLOSION-POISON"
        Tags = @("newcards", "powers")
        Why = "尸爆是新加的牌：给目标上毒，再挂一层「死了炸全场」。"
        Args = @(
            "-EnemyCurrentHp", "60", "-ClearPlayerPiles", "-InitialPlayerEnergy", "3",
            "-CardsJson", (Hand @("CorpseExplosion")),
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 必然结局+ 会在**每个回合开始**开一次「从抽牌堆挑几张放牌堆顶」的选择。
        # 这条盯的不是某个数值，而是那条通道本身走不走得通：选择要能开出分支、
        # 能被计划记下来、跨回合重放时顺序要对得上。任何一处抛异常这条就挂。
        # 不清牌堆 —— 抽牌堆空了这一招就没有候选，通道根本不会被走到。
        Id = "RS-FOREGONE-CONCLUSION-DRAW-TOP"
        Character = "REGENT"
        Tags = @("cards", "powers", "choices")
        Why = "必然结局+：每回合开始从抽牌堆挑牌放堆顶，走求解器的选牌通道。"
        Args = @(
            "-EnemyCurrentHp", "60", "-InitialPlayerEnergy", "3",
            "-CardsJson", (Hand @("ForegoneConclusion")),
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 周密计划+ 的选择开在**回合结束清手牌之前**，那个时点求解器原本一条钩子都不跑。
        # 同样盯通道：挂在 RunPhaseOne 后面的那次挂起要能被上层当成搜索边界接住。
        Id = "RS-WELL-LAID-PLANS-RETAIN"
        Character = "SILENT"
        Tags = @("cards", "powers", "choices")
        Why = "周密计划+：回合结束前挑最多 N 张一次性保留。"
        Args = @(
            "-EnemyCurrentHp", "60", "-InitialPlayerEnergy", "3",
            "-CardsJson", (Hand @("WellLaidPlans")),
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 迷雾的「膨胀」在改版里有两处变化，都落在召唤那半截上：
        #   1. 每只生出来的气弹挂 1 层「乒乓」——不算的话求解器以为打气弹不要钱，实际要挨反伤。
        #   2. BloatAmount 每用一次 +1（上限 5）——求解器读的是建根时冻结的静态值，一场里不变。
        # 召唤那半截在 MonsterMoveEffects.ApplyBeforeAttack 里，MonsterMirrors 的前缀够不着，
        # 所以另开了 BloatSpawnPatch；递增靠 StateStore 上一个按分支复制的计数。
        #
        # 迷雾的出招是「首招 → 膨胀 → 蓄力 → 膨胀 → …」，所以第二回合就膨胀一次。
        # 断言盯的是「第二回合直接复用首轮计划、一次都没重算」：气弹数、乒乓层数任一算错，
        # 实机一到第二回合就和预测对不上，必然重算。
        #
        # **递增这一半这条用例没覆盖**：那要跑到第二次膨胀（第四回合），而计划排不到那么远，
        # 实测第三回合就已经不复用了。递增靠代码里那段注释和 StateStore 的分支复制语义锁着。
        Id = "RS-LIVING-FOG-BLOAT"
        Encounter = "LIVING_FOG_NORMAL"
        Character = "SILENT"
        Tags = @("monsters")
        Full = $true
        Why = "迷雾的膨胀：气弹要挂乒乓，张数要逐次递增。"
        Args = @(
            "-EnemyCurrentHp", "200", "-ClearPlayerPiles", "-InitialPlayerEnergy", "3",
            "-CardsJson", '[{"cardId":"StrikeSilent","pile":"Hand","count":5},{"cardId":"StrikeSilent","pile":"Draw","count":15}]',
            "-ExpectedReusedTurn", "2",
            "-ExpectedUnexpectedReplansAtMost", "0",
            "-StopAfterExpectedReuse"
        )
    },
    @{
        # 无尽之刃+ 让手牌上限随手里的匕首数变。求解器的上限是建根时冻结的，
        # 这条盯的是那份「按当前分支重算」的补丁在整条搜索里不会把上限算成负数或抛出来。
        # 藏匿匕首的效果被一个玩家选择劈成两段：先从手牌选几张弃掉，**选完之后**才造匕首。
        # 我们原来的镜像在出牌那一刻就把匕首造进手牌，顺序反了。后果不是差一点数值：模拟里
        # 手牌提前多出两张匕首，求解器于是计划「把匕首弃掉」，而实机弹弃牌页面时匕首还没造出来，
        # 部署直接报 NativeChoicePlanMismatchException：原生选牌页面找不到 SHIV，整场操作不了。
        # 2026-09-14 那份感染棱柱的问题包就是这个。
        #
        # **这条用例锁不住那个顺序，要说清楚。**试过三种断言都区分不开，原因是结构性的：
        # 顺序错的那一版是「更宽松」的——它的弃牌候选里多了两张匕首，但它完全可以选择不弃匕首，
        # 从而得到和正确顺序一模一样的动作数、伤害、格挡。要逼它必须弃匕首，就得让手牌数少于
        # 要弃的张数；可那样一来弃完手里只剩匕首，而匕首挂了「充能」之后伤害是 0、只给 1 点能量，
        # 没地方花，求解器根本不会去打这张牌，用例就跑不起来。两个条件互相排斥。
        #
        # 所以这条是**端到端覆盖**：真的把路线打出去（Full），确认这张牌能打、能应答那个弃牌
        # 页面、整场能走完。这张牌在此之前一条用例都没有，顺序错才会一直没人发现。
        # 顺序本身靠代码里的注释和那份问题包锁着，见 src/HiddenDaggersShivPatch.cs。
        Id = "RS-HIDDEN-DAGGERS-DISCARD-ORDER"
        Character = "SILENT"
        Tags = @("cards")
        Full = $true
        Why = "藏匿匕首：弃牌页面要应答得上，整场要走得完。"
        Args = @(
            # 让求解器非打这张牌不可：第一回合能量给 0，本牌 0 费，打完两张「充能」匕首
            # 各给 1 点能量，才凑得出手里那张打击的费用。不打的话第一回合一点伤害都没有。
            # （只把废牌塞满手牌是不够的——求解器优化的是掉血，弃废牌那点收益它不认，试过。）
            # 敌人血量还要压到「这一回合就能秒掉」：求解器优化的是掉血不是伤害，
            # 打不死的话第一回合那点伤害对它毫无价值，它照样不打这张牌（试过 30 血，不打）。
            # 12 血 = 手里两张打击各 6 点，正好当回合结束战斗、一滴血不掉。
            "-EnemyCurrentHp", "12", "-ClearPlayerPiles", "-InitialPlayerEnergy", "0",
            "-CardsJson", '[{"cardId":"HiddenDaggers","pile":"Hand","count":1},{"cardId":"Wound","pile":"Hand","count":3},{"cardId":"StrikeSilent","pile":"Hand","count":2}]',
            "-ExpectedPlayedCardId", "HIDDEN_DAGGERS"
        )
    },
    @{
        Id = "RS-INFINITE-BLADES-HAND-SIZE"
        Character = "SILENT"
        Tags = @("cards", "powers", "global")
        Why = "无尽之刃+：手牌上限随手里的匕首数变，求解器原本冻结。"
        Args = @(
            "-EnemyCurrentHp", "60", "-InitialPlayerEnergy", "3",
            "-CardsJson", (Hand @("InfiniteBlades")),
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 低语耳环是本适配层改动最大的一处：原版第一回合直接连打 13 张，改版整个关掉，
        # 换成「本场攒够 13 点能量之后，下一张牌打完触发那一轮连打」。
        #
        # 这条盯的是**原版那一段真的被拦住了**：耳环在身上、第一回合，如果拦漏了，
        # 求解器会替玩家连打 13 张，手牌被清空，起防峰值必然不是这里断言的 8。
        # 不可触碰本身是「4 格挡 × 重复 2 次」，和 RS-UNTOUCHABLE-REPEAT-BLOCK 同一张牌，
        # 所以这条挂掉只可能是耳环那一段的问题。
        Id = "RS-WHISPERING-EARRING-NO-TURN1-AUTOPLAY"
        Tags = @("relics", "global")
        Why = "低语耳环：原版的第一回合连打必须被拦住。"
        Args = @(
            "-EnemyCurrentHp", "60", "-ClearPlayerPiles", "-InitialPlayerEnergy", "3",
            "-RelicsJson", '[{"relicId":"WHISPERING_EARRING","addWithoutObtainedEffects":true}]',
            "-CardsJson", (Hand @("Untouchable")),
            "-ExpectedInitialMaxBlockAtLeast", "8",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 战史课程的重放范围从「攻击」放宽到「攻击或技能」。改动落在求解器两个写死的方法上，
        # 其中一个只在「建根时还没物化」的分支里走到。这条盯的是两处补完之后整条搜索不抛。
        Id = "RS-HISTORY-COURSE-SKILL-REPLAY"
        Tags = @("relics")
        Why = "战史课程：重放范围放宽到技能牌。"
        Args = @(
            "-EnemyCurrentHp", "60", "-ClearPlayerPiles", "-InitialPlayerEnergy", "3",
            "-RelicsJson", '[{"relicId":"HISTORY_COURSE","addWithoutObtainedEffects":true}]',
            "-CardsJson", (Hand @("Untouchable")),
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 改版新 Power 上的字符串变量。求解器给动态变量算指纹时遇到 StringVar 会去问
        # SemanticStateFieldPolicy 这个字段算不算「影响结算」，那是一张写死的白名单，
        # 认不出来就**抛异常** —— 整场战斗算不出来。七个新 Power 都带这种变量。
        #
        # 这条挂掉 = 那七场战斗全都用不了。实测把补丁摘掉这条会直接超时。
        # 逐个列出来跑，是因为它们分属不同遭遇，白名单漏一个就漏一整场。
        Id = "RS-STRING-VAR-POWERS"
        Tags = @("powers", "global")
        Why = "七个带字符串变量的新 Power：指纹分类不认就整场算不出来。"
        Args = @(
            "-EnemyCurrentHp", "60", "-ClearPlayerPiles", "-InitialPlayerEnergy", "3",
            "-CardsJson", (Hand @("Untouchable")),
            "-PowerId", "TaintedPlusPower", "-PowerAmount", "1", "-PowerTarget", "Player",
            "-ExpectedInitialMaxBlockAtLeast", "8",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 和上一条同一个机制，换一个 Power 和一个真实遭遇：感染棱柱精英战。
        # 玩家报的就是这一场「识别不了」。
        Id = "RS-INFESTED-PRISM-ELITE"
        Encounter = "INFESTED_PRISMS_ELITE"
        Tags = @("powers", "global")
        Why = "感染棱柱精英战：整场能不能算出来。"
        Args = @(
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 求解器推进怪物行动时，条件分支走的是它自己写死的那张表，返回招式 id 再查
        # machine.States[id]。改版把「组装打击」删了，分支不再通向随机节点 RAND，
        # 机器里根本没有这个 id —— 一查就抛 KeyNotFoundException，整场算不出来。
        #
        # 这条得跑到第二回合：分支是第一回合结束、推进到下一回合时才走到的。
        Id = "RS-FABRICATOR-BRANCH"
        Encounter = "FABRICATOR_NORMAL"
        Tags = @("monsters")
        Why = "组装师的组装分支：原版那个 RAND 节点在改版里不存在。"
        Args = @(
            # 这三个参数是这条用例的全部重点。harness 的敌人血量默认是 **1**，
            # 不改的话求解器第一回合就把组装师打死了，根本不会推进到下一回合——
            # 而条件分支只在推进回合时才走到。第一版就是这么写的，
            # 把补丁摘掉也照样「通过」，什么都没验到。
            "-EnemyCurrentHp", "240", "-ClearPlayerPiles", "-InitialPlayerEnergy", "3",
            "-CardsJson", (Hand @("DEFEND_IRONCLAD")),
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 同一个坑的另一处：活体护盾有同伴时原版走 SHIELD_SLAM_MOVE，
        # 改版换成了 SHIELD_UP_MOVE，那个 id 同样不存在。
        # 这一场里炮台操作员是活着的同伴，所以分支必定走「有同伴」那一支。
        Id = "RS-LIVING-SHIELD-BRANCH"
        Encounter = "TURRET_OPERATOR_WEAK"
        Tags = @("monsters")
        Why = "活体护盾的同伴分支：原版那个 SHIELD_SLAM_MOVE 在改版里不存在。"
        Args = @(
            # 同上：必须让战斗活到第二回合，分支才会被走到。
            "-EnemyCurrentHp", "90", "-ClearPlayerPiles", "-InitialPlayerEnergy", "3",
            "-CardsJson", (Hand @("DEFEND_IRONCLAD")),
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 前六轮都在查「算得对不对」，这条查的是「算出来的计划能不能真打完」。
        # 计划跨回合，所以不能用 -StopAfterInitialSolverResultAssertion（Full = $true），
        # 断言换成「第二回合直接复用上一次的计划、一次都没重算」。
        #
        # 组装师挂的那一条：FabricatorPower 让它在场上还有机器人时只挨一半伤害，
        # 而那份实现读的是**实机**的敌人列表。第一回合做计划时场上还没机器人，
        # 于是第二回合的刀全算成双倍。
        Id = "RS-FABRICATOR-NO-REPLAN"
        Encounter = "FABRICATOR_NORMAL"
        Tags = @("monsters")
        Full = $true
        Why = "组装师：第二回合的伤害要算上它的减伤，否则整局重算。"
        Args = @(
            # 又是那个默认值：不给血量的话敌人只有 1 点血，第一回合就打完了，
            # 根本没有第二回合可以复用计划。
            "-EnemyCurrentHp", "240",
            # 牌必须是**群伤**：减伤只对组装师本体生效，而第二回合求解器会优先清机器人，
            # 给单体牌的话它根本不打组装师，这条就又变成什么都没验。
            # 回合二要有牌可打，所以抽牌堆里也得放。
            "-ClearPlayerPiles", "-InitialPlayerEnergy", "3",
            "-CardsJson", '[{"cardId":"EchoingSlash","pile":"Hand","count":3},{"cardId":"EchoingSlash","pile":"Draw","count":8}]',
            "-ExpectedReusedTurn", "3",
            "-ExpectedUnexpectedReplansAtMost", "0",
            "-StopAfterExpectedReuse"
        )
    },
    @{
        # 魂枢挂的那一条：汲取生命在改版里只剩一刀，原版跟着的易伤 2、虚弱 2 都没了。
        # 求解器照原版口径白给玩家两层减益，一到实机就对不上。
        # 汲取生命是魂枢的第三招，所以这条得跑到第四回合。
        Id = "RS-SOUL-NEXUS-NO-REPLAN"
        Encounter = "SOUL_NEXUS_ELITE"
        Tags = @("monsters")
        Full = $true
        Why = "魂枢：汲取生命不再带减益，算多了就整局重算。"
        Args = @(
            "-EnemyCurrentHp", "254",
            # 必须把魂枢的攻击全挡下来。「枯魂」数的是**没挡住**的强化攻击次数，
            # 满 12 次它就改出「魂印」—— 用初始牌组跑的话两回合就满了（灵魂打击 5 次 +
            # 漩涡 12 次），根本轮不到汲取生命，这条就白跑了。挡满之后才是
            # 灵魂打击 → 漩涡 → 汲取生命，第四回合开头才能看出减益多没多。
            "-ClearPlayerPiles", "-InitialPlayerEnergy", "3",
            "-CardsJson", '[{"cardId":"Impervious","pile":"Hand","count":2},{"cardId":"Impervious","pile":"Draw","count":8}]',
            "-ExpectedReusedTurn", "4",
            "-ExpectedUnexpectedReplansAtMost", "0",
            "-StopAfterExpectedReuse"
        )
    },
    @{
        # 亲族随从：改版给它加了一条条件分支，但忘了把分支本身加进状态列表。
        # machine.States 里没有 "KinFollower"，求解器建根时抄不到这条分支的选择，
        # 推进到它时抛「没有根选择」—— 整场算不出来。
        Id = "RS-KIN-FOLLOWER-BRANCH"
        Encounter = "THE_KIN_BOSS"
        Tags = @("monsters")
        Why = "亲族随从的分支根本不在状态表里，整场算不出来。"
        Args = @(
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 亲族祭司第 1 回合召两只信徒，改版在 AfterAddedToRoom 里改了它们的血：
        # 不跳舞的乘 1.5，跳舞的那只挂「假随从」再减半。镜像不跟这一步，第 2 回合
        # 开头就有三处对不上（两只的血、外加那层假随从），整局重算。
        # 断言必须落在第 2 回合：第 1 回合信徒还没进场，开局那一下什么都看不出来。
        Id = "RS-KIN-SUMMON-HP"
        Encounter = "THE_KIN_BOSS"
        Tags = @("monsters")
        Full = $true
        Why = "亲族信徒召出来是 1.5 倍和 0.5 倍血，不是原版那份。"
        Args = @(
            "-ExpectedReusedTurn", "2",
            "-ExpectedUnexpectedReplansAtMost", "0",
            "-StopAfterExpectedReuse"
        )
    },
    @{
        # 千足虫：改版的鼓胀只给自己 1 力量，原版是 2。三节起手招式各不相同，
        # 中间那节第一回合就鼓胀，所以第二回合开头就能看出差一。
        # 顺带盯住一件事：这里 switch 的是 GetType().Name，在场的是
        # DecimillipedeSegmentFront/Middle/Back，写基类名的分支一次都不会命中。
        Id = "RS-DECIMILLIPEDE-BULK-STRENGTH"
        Encounter = "DECIMILLIPEDE_ELITE"
        Tags = @("monsters")
        Full = $true
        Why = "千足虫的鼓胀改成 1 力量，算成 2 就每回合重算。"
        Args = @(
            "-EnemyCurrentHp", "999",
            "-ExpectedReusedTurn", "2",
            "-ExpectedUnexpectedReplansAtMost", "0",
            "-StopAfterExpectedReuse"
        )
    },
    @{
        # 无餍之物：液化给玩家 5 层「长距离」，这层在自己侧回合开始时掉 1。
        # 求解器的 TriggerAfterSideTurnStart 只有倒计时和流沙坑两条写死的处理，
        # 认不出就静默不衰减，于是从第二回合起层数一直对不上。
        Id = "RS-INSATIABLE-LONG-DISTANCE-TICK"
        Encounter = "THE_INSATIABLE_BOSS"
        Tags = @("monsters")
        Full = $true
        Why = "长距离每回合掉一层，不衰减就每回合重算。"
        Args = @(
            "-EnemyCurrentHp", "999",
            "-ExpectedReusedTurn", "2",
            "-ExpectedUnexpectedReplansAtMost", "0",
            "-StopAfterExpectedReuse"
        )
    },
    @{
        # 胧光怪：改版的恐惧蛛进场除了原版那层幻影，还多 4 层「幻灭」——复活时按这个层数
        # 扣自己的力量。求解器召唤幻影走的是它自己写死的那份进场 Power，认不出改版加的这层，
        # 第 2 回合（幻影刚进场）开头就对不上。
        # 光这一条还盯不住死亡那半：幻灭是减益又声明「主人死了也不走」，求解器那条判据会把它
        # 清成 0。那半要恐惧蛛真死一次才看得见，这里只压住进场。
        Id = "RS-OBSCURA-ILLUSION-DISILLUSION"
        Encounter = "THE_OBSCURA_NORMAL"
        Tags = @("monsters")
        Full = $true
        Why = "恐惧蛛进场带 4 层幻灭，漏了就每回合重算。"
        Args = @(
            "-EnemyCurrentHp", "999",
            "-ExpectedReusedTurn", "2",
            "-ExpectedUnexpectedReplansAtMost", "0",
            "-StopAfterExpectedReuse"
        )
    },
    @{
        # 长距离：自己打出一张仓皇逃窜就涨一层。求解器的 AfterCardPlayed 是注册表，没登记
        # 就每打一张牌记一条未镜像风险（COVERAGE ... method=AfterCardPlayed
        # reason=MethodNotMirrored），数值照样按没涨算 —— 沙虫那一场长距离层数和奥斯提
        # 掉的血同时错，根因就是这一条。
        # 这里盯的是「有没有登记」而不是「涨得对不对」：涨层要真打出仓皇逃窜才看得见，
        # 而那是张状态牌，求解器打不打由它自己的估值决定，锁不住（见 docs/coverage-gaps.md）。
        Id = "RS-LONG-DISTANCE-CARD-PLAYED-MIRROR"
        Character = "NECROBINDER"
        Tags = @("powers")
        Why = "长距离的 AfterCardPlayed 必须登记，否则每打一张牌记一条未镜像。"
        Args = @(
            "-EnemyCurrentHp", "60", "-ClearPlayerPiles", "-InitialPlayerEnergy", "3",
            "-CardsJson", (Hand @("StrikeNecrobinder")),
            "-PowerId", "LongDistancePower", "-PowerAmount", "5", "-PowerTarget", "Player",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    },
    @{
        # 方块构造体：连发炮击一/二在改版里只剩那一炮，原版还各给自己 2 力量。
        # 求解器照原版口径给，一回合就多 2 点力量，下一回合开头对不上。
        # 招式顺序是蓄能 → 连发一 → 连发二 → 吐出，所以第三回合开头才能看出来。
        Id = "RS-CUBEX-NO-REPLAN"
        Encounter = "CUBEX_CONSTRUCT_NORMAL"
        Tags = @("monsters")
        Full = $true
        Why = "方块构造体：连发炮击不再给力量，算多了就整局重算。"
        Args = @(
            "-EnemyCurrentHp", "120",
            "-ExpectedReusedTurn", "3",
            "-ExpectedUnexpectedReplansAtMost", "0",
            "-StopAfterExpectedReuse"
        )
    },
    @{
        # 寄生蛙精英：寄生+ 的 AfterDeath 没登记，求解器一发现能打赢的路线上
        # 有没镜像的死亡钩子，就把路线标成 UnsupportedEffect 边界、不敢往下算，
        # 路线只剩半个回合，打完就「计划用尽」重算。
        Id = "RS-PHROG-DEATH-COVERAGE"
        Encounter = "PHROG_PARASITE_ELITE"
        Tags = @("monsters")
        Why = "寄生+ 的死亡钩子没登记，打赢的路线会被截短。"
        Args = @(
            "-EnemyCurrentHp", "66",
            "-ExpectedInitialUnmirroredCount", "0"
        )
    }
)

# 要用 @() 包住。只筛出一条时 PowerShell 会把数组拆成单个哈希表。
if ($Only) { $cases = @($cases | Where-Object { $_.Id -eq $Only }) }
if ($Tag)  { $cases = @($cases | Where-Object { $_.Tags -contains $Tag }) }
if (-not $cases) { throw "没有匹配的用例：$Only" }

if (-not $NoRestart) {
    Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
}

$results = @()
$deadline = (Get-Date).AddMinutes($MaxTotalMinutes)
Write-MatrixProgress ("开始 共 {0} 条 时限 {1} 分钟 进度文件 {2}" -f $cases.Count, $MaxTotalMinutes, $ProgressPath)
$index = 0
$aborted = $false
foreach ($case in $cases) {
    $index++
    if ($aborted) {
        $results += [pscustomobject]@{ Case = $case.Id; Passed = $false; Note = "未运行" }
        continue
    }
    $remaining = [int]($deadline - (Get-Date)).TotalSeconds
    if ($remaining -lt 40) {
        Write-MatrixProgress ("到时限 {0} 分钟，剩下 {1} 条未运行" -f $MaxTotalMinutes, ($cases.Count - $index + 1))
        $aborted = $true
        $results += [pscustomobject]@{ Case = $case.Id; Passed = $false; Note = "未运行" }
        continue
    }
    $caseTimeout = [Math]::Min(150, $remaining)
    Write-Host ""
    Write-Host "=== $($case.Id)" -ForegroundColor Cyan
    Write-Host "    $($case.Why)"
    # 并行度钉死：不钉的话线程调度会让展开顺序变化，最优性断言会随机不过。
    $argv = @(
        "-NoProfile", "-File", $runner,
        "-ScenarioId", $case.Id,
        "-CharacterId", ($case.Character ?? "IRONCLAD"),
        "-EncounterId", ($case.Encounter ?? "FUZZY_WURM_CRAWLER_WEAK"),
        "-Sts2GameRoot", $GameRoot,
        "-RitsuWorkshopRoot", $RitsuWorkshopRoot,
        "-ForceShortSearchOnly",
        "-SearchMaxDegreeOfParallelismForTest", "1",
        "-TimeoutSeconds", "$caseTimeout"
        "-ExitOnComplete"
    ) + ($case.Full ? @() : @("-StopAfterInitialSolverResultAssertion")) + $case.Args
    $output = & pwsh @argv 2>&1
    $ok = $LASTEXITCODE -eq 0
    if (-not $ok) {
        $output | Select-String -Pattern '"error"' | Select-Object -First 1 |
            ForEach-Object { Write-Host "    $($_.Line.Trim())" -ForegroundColor DarkYellow }
    }
    $results += [pscustomobject]@{ Case = $case.Id; Passed = $ok; Note = ($ok ? "" : "未通过") }
    Write-MatrixProgress ("[{0}/{1}] {2} {3}" -f $index, $cases.Count, $case.Id, ($ok ? "通过" : "未通过"))
}

Write-Host ""
Write-Host "=== 汇总" -ForegroundColor Cyan
$results | Format-Table -AutoSize
$failed = ($results | Where-Object { -not $_.Passed }).Count
$notRun = ($results | Where-Object { $_.Note -eq "未运行" }).Count
Write-MatrixProgress ("结束 通过 {0}/{1} 未通过 {2} 未运行 {3}" -f ($results.Count - $failed), $results.Count, ($failed - $notRun), $notRun)
exit ($failed -gt 0 ? 1 : 0)
