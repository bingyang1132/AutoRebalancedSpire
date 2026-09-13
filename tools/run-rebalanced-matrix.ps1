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
        # 无尽之刃+ 让手牌上限随手里的匕首数变。求解器的上限是建根时冻结的，
        # 这条盯的是那份「按当前分支重算」的补丁在整条搜索里不会把上限算成负数或抛出来。
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
