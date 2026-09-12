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
        # 永恒护甲给 11 点镀甲。镀甲在回合结束前会变成等量格挡，所以峰值格挡不低于 11。
        Id = "RS-ETERNAL-ARMOR-PLATING"
        Tags = @("cards", "global")
        Why = "永恒护甲：11 点镀甲，回合结束前转成等量格挡。"
        Args = @(
            "-EnemyCurrentHp", "60", "-ClearPlayerPiles", "-InitialPlayerEnergy", "3",
            "-CardsJson", (Hand @("EternalArmor")),
            "-ExpectedInitialMaxBlockAtLeast", "11",
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
        "-EncounterId", "FUZZY_WURM_CRAWLER_WEAK",
        "-Sts2GameRoot", $GameRoot,
        "-RitsuWorkshopRoot", $RitsuWorkshopRoot,
        "-StopAfterInitialSolverResultAssertion",
        "-ForceShortSearchOnly",
        "-SearchMaxDegreeOfParallelismForTest", "1",
        "-TimeoutSeconds", "$caseTimeout"
        "-ExitOnComplete"
    ) + $case.Args
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
