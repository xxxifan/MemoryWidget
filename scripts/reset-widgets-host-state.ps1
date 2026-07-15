[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "Medium")]
param(
    [switch]$Aggressive,
    [switch]$ClearProviderState,
    [switch]$SkipProcessRestart,
    [switch]$ClearWidgetPreferences,
    [switch]$ClearDefinitionCache
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Stop-TargetProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $processes = Get-Process -Name $Name -ErrorAction SilentlyContinue
    if (-not $processes) {
        return
    }

    foreach ($proc in $processes) {
        Write-Host ("停止进程 {0} (PID={1})" -f $proc.ProcessName, $proc.Id) -ForegroundColor Yellow
        if ($PSCmdlet.ShouldProcess(("PID {0}" -f $proc.Id), "Stop-Process -Force")) {
            Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        }
    }
}

function Assert-PathUnderRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullRoot = [System.IO.Path]::GetFullPath($Root)
    if (-not $fullPath.StartsWith($fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw ("安全校验失败，路径不在目标根目录下: path='{0}', root='{1}'" -f $fullPath, $fullRoot)
    }
}

function Backup-And-Recreate {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Root,
        [Parameter(Mandatory = $true)]
        [string]$BackupRoot,
        [bool]$RecreateAsDirectory = $false
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        Write-Host ("跳过不存在的路径: {0}" -f $Path) -ForegroundColor DarkGray
        return
    }

    Assert-PathUnderRoot -Path $Path -Root $Root

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullRoot = [System.IO.Path]::GetFullPath($Root)
    $relative = $fullPath.Substring($fullRoot.Length).TrimStart('\')
    $backupPath = Join-Path $BackupRoot $relative
    $backupDir = Split-Path -Path $backupPath -Parent
    if (-not (Test-Path -LiteralPath $backupDir)) {
        New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
    }

    Write-Host ("备份并清理: {0}" -f $fullPath) -ForegroundColor Cyan
    if ($PSCmdlet.ShouldProcess($fullPath, ("Move-Item -> {0}" -f $backupPath))) {
        Move-Item -LiteralPath $fullPath -Destination $backupPath -Force
    }

    if ($RecreateAsDirectory) {
        Write-Host ("重建空目录: {0}" -f $fullPath) -ForegroundColor DarkCyan
        if ($PSCmdlet.ShouldProcess($fullPath, "New-Item -ItemType Directory")) {
            New-Item -ItemType Directory -Path $fullPath -Force | Out-Null
        }
    }
}

function Backup-MatchingChildren {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ParentPath,
        [Parameter(Mandatory = $true)]
        [string]$Root,
        [Parameter(Mandatory = $true)]
        [string]$BackupRoot,
        [Parameter(Mandatory = $true)]
        [string]$NamePattern
    )

    if (-not (Test-Path -LiteralPath $ParentPath)) {
        Write-Host ("跳过不存在的目录: {0}" -f $ParentPath) -ForegroundColor DarkGray
        return
    }

    Assert-PathUnderRoot -Path $ParentPath -Root $Root
    $items = Get-ChildItem -LiteralPath $ParentPath -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like $NamePattern }
    foreach ($item in $items) {
        Backup-And-Recreate -Path $item.FullName -Root $Root -BackupRoot $BackupRoot -RecreateAsDirectory $false
    }
}

$webExperienceFamily = "MicrosoftWindows.Client.WebExperience_cw5n1h2txyewy"
$webExperienceRoot = Join-Path $env:LOCALAPPDATA ("Packages\{0}" -f $webExperienceFamily)
$localStateRoot = Join-Path $webExperienceRoot "LocalState"

if (-not (Test-Path -LiteralPath $webExperienceRoot)) {
    throw ("未找到 Windows Web Experience 包目录: {0}" -f $webExperienceRoot)
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupRoot = Join-Path $env:LOCALAPPDATA ("MemoryWidget\backups\widgets-host-state-{0}" -f $timestamp)
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null

Write-Host ("备份目录: {0}" -f $backupRoot) -ForegroundColor Green

if (-not $SkipProcessRestart) {
    $targets = @("MemoryWidgetProvider", "WidgetService", "WidgetBoard", "Widgets", "WebExperienceHost")
    foreach ($name in $targets) {
        Stop-TargetProcess -Name $name
    }
    Start-Sleep -Milliseconds 800
}

$hostStateTargets = @(
    @{ Path = (Join-Path $localStateRoot "WidgetSessions"); Recreate = $true },
    @{ Path = (Join-Path $localStateRoot "FeedSessions"); Recreate = $true },
    @{ Path = (Join-Path $localStateRoot "EBWebView"); Recreate = $false }
)

if ($Aggressive) {
    $hostStateTargets += @(
        @{ Path = (Join-Path $localStateRoot "ShellFeeds"); Recreate = $true },
        @{ Path = (Join-Path $webExperienceRoot "AC\INetCache"); Recreate = $true }
    )
}

foreach ($item in $hostStateTargets) {
    Backup-And-Recreate -Path $item.Path -Root $webExperienceRoot -BackupRoot $backupRoot -RecreateAsDirectory $item.Recreate
}

if ($Aggressive -or $ClearWidgetPreferences) {
    $settingsDir = Join-Path $webExperienceRoot "Settings"
    $settingsTargets = @(
        (Join-Path $settingsDir "settings.dat"),
        (Join-Path $settingsDir "settings.dat.LOG1"),
        (Join-Path $settingsDir "settings.dat.LOG2"),
        (Join-Path $settingsDir "roaming.lock")
    )

    foreach ($path in $settingsTargets) {
        Backup-And-Recreate -Path $path -Root $webExperienceRoot -BackupRoot $backupRoot -RecreateAsDirectory $false
    }
}

if ($Aggressive -or $ClearDefinitionCache) {
    $definitionsDir = Join-Path $webExperienceRoot "LocalCache\Definitions"
    Backup-MatchingChildren `
        -ParentPath $definitionsDir `
        -Root $webExperienceRoot `
        -BackupRoot $backupRoot `
        -NamePattern "MemoryWidgetProvider.Package_*"
}

if ($ClearProviderState) {
    $providerPackages = Get-AppxPackage -Name "MemoryWidgetProvider.Package" -ErrorAction SilentlyContinue
    foreach ($pkg in $providerPackages) {
        $providerRoot = Join-Path $env:LOCALAPPDATA ("Packages\{0}" -f $pkg.PackageFamilyName)
        $providerLocalState = Join-Path $providerRoot "LocalState"
        if (Test-Path -LiteralPath $providerLocalState) {
            Backup-And-Recreate -Path $providerLocalState -Root $providerRoot -BackupRoot (Join-Path $backupRoot "provider-state") -RecreateAsDirectory $true
        }
    }
}

Write-Host "`nWidgets Host 状态重置完成。" -ForegroundColor Green
Write-Host "建议下一步：" -ForegroundColor Green
Write-Host "1) 按 Win + W 打开小组件面板" -ForegroundColor Green
Write-Host "2) 观察是否能正常隐藏/刷新" -ForegroundColor Green
Write-Host ("3) 如需更激进重置，可执行: .\scripts\reset-widgets-host-state.ps1 -Aggressive -ClearProviderState -ClearWidgetPreferences -ClearDefinitionCache") -ForegroundColor Green
Write-Host ("4) 如需仅清除小组件偏好，可执行: .\scripts\reset-widgets-host-state.ps1 -ClearWidgetPreferences -ClearDefinitionCache") -ForegroundColor Green
Write-Host ("5) 如需回滚，可从备份目录恢复: {0}" -f $backupRoot) -ForegroundColor Green
