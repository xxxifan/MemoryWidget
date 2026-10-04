# 一键安装：检查 .NET、构建开发者包、补装 Windows App Runtime、导入证书并安装小组件。
# 由仓库根目录的「一键安装.cmd」以管理员身份调用。
# Windows PowerShell 5.1 会把无 BOM 的 UTF-8 脚本按 ANSI 读取导致中文乱码，因此所有脚本都按 UTF-8 读入文本后再执行。
param(
    [Parameter(Mandatory = $true)]
    [string]$RepoRoot
)

$ErrorActionPreference = "Stop"
Set-Location $RepoRoot

function Invoke-Utf8Script {
    param(
        [string]$Path,
        [hashtable]$Parameters = @{}
    )

    $block = [scriptblock]::Create([IO.File]::ReadAllText((Join-Path $RepoRoot $Path)))
    & $block @Parameters
}

function Stop-WithMessage {
    param(
        [string]$Message,
        [string]$Url
    )

    Write-Host ""
    Write-Host $Message -ForegroundColor Red
    if ($Url) {
        Write-Host "下载地址：$Url"
        Start-Process $Url
    }
    exit 1
}

try {
    Write-Host "[1/4] 检查 .NET 8..." -ForegroundColor Cyan
    $dotnetUrl = "https://dotnet.microsoft.com/download/dotnet/8.0"
    $dotnetHint = "请先安装 .NET 8 SDK（Windows 安装程序 x64），装好后重新双击「一键安装.cmd」。"
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Stop-WithMessage "没有找到 .NET。$dotnetHint" $dotnetUrl
    }
    $hasSdk = dotnet --list-sdks | Where-Object { [int]($_.Split('.')[0]) -ge 8 }
    $hasRuntime = dotnet --list-runtimes | Where-Object { $_ -like "Microsoft.NETCore.App 8.*" }
    if (-not $hasSdk -or -not $hasRuntime) {
        Stop-WithMessage "没有找到 .NET 8。$dotnetHint" $dotnetUrl
    }

    $arch = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
    $runtime, $appxArch = switch ($arch) {
        "ARM64" { "win-arm64", "Arm64" }
        "x86" { "win-x86", "X86" }
        default { "win-x64", "X64" }
    }

    Write-Host "[2/4] 构建小组件（首次需要下载依赖，请耐心等待）..." -ForegroundColor Cyan
    Invoke-Utf8Script "scripts\build-dev-msix.ps1" @{ Runtime = $runtime; NoNextStepHint = $true }

    Write-Host "[3/4] 检查 Windows App Runtime..." -ForegroundColor Cyan
    [xml]$project = Get-Content -Raw (Join-Path $RepoRoot "MemoryWidgetProvider\MemoryWidgetProvider.csproj")
    $sdkVersion = $project.Project.ItemGroup.PackageReference |
        Where-Object { $_.Include -eq "Microsoft.WindowsAppSDK" } |
        Select-Object -ExpandProperty Version -First 1
    $frameworkName = "Microsoft.WindowsAppRuntime." + (($sdkVersion.Split('.')[0..1]) -join '.')
    $installed = Get-AppxPackage -Name $frameworkName | Where-Object { $_.Architecture.ToString() -eq $appxArch }
    if ($installed) {
        Write-Host "已安装 $frameworkName。"
    } else {
        # 构建时 NuGet 已下载 Windows App SDK，运行库安装包就在其中
        $nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE ".nuget\packages" }
        $frameworkMsix = Join-Path $nugetRoot ("microsoft.windowsappsdk.runtime\$sdkVersion\tools\MSIX\win10-" + $runtime.Substring(4) + "\$frameworkName.msix")
        if (-not (Test-Path $frameworkMsix)) {
            Stop-WithMessage "没有找到 $frameworkName 安装包，请手动安装 Windows App Runtime 后重试。" "https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads"
        }
        Write-Host "安装 $frameworkName..."
        Add-AppxPackage -Path $frameworkMsix
    }

    Write-Host "[4/4] 安装小组件..." -ForegroundColor Cyan
    Invoke-Utf8Script "scripts\install-dev-msix.ps1"

    Write-Host ""
    Write-Host "全部完成！按 Win + W 打开小组件面板，点右上角的 +，找到「内存清理」添加即可。" -ForegroundColor Green
} catch {
    Write-Host ""
    Write-Host "安装失败：$($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
