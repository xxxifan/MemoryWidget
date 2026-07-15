param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidateSet("x64", "x86", "ARM64")]
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Path $PSScriptRoot -Parent
$wapprojPath = Join-Path $repoRoot "MemoryWidgetProvider.Package\MemoryWidgetProvider.Package.wapproj"

$vswhere = Join-Path "${env:ProgramFiles(x86)}" "Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) {
    throw "未找到 vswhere.exe。请先安装 Visual Studio（含 Windows Application Packaging Project）。"
}

$installationPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
if (-not $installationPath) {
    throw "未检测到可用的 Visual Studio MSBuild。"
}

$msbuildPath = Join-Path $installationPath "MSBuild\Current\Bin\MSBuild.exe"
if (-not (Test-Path $msbuildPath)) {
    throw "未找到 MSBuild.exe：$msbuildPath"
}

Write-Host "使用 MSBuild 打包小组件..." -ForegroundColor Cyan
& $msbuildPath $wapprojPath `
    /t:Restore,Build `
    /p:Configuration=$Configuration `
    /p:Platform=$Platform `
    /p:GenerateAppxPackageOnBuild=true `
    /p:AppxBundle=Never

Write-Host "打包完成。请在 MemoryWidgetProvider.Package\\AppPackages 目录查看输出。" -ForegroundColor Green
