param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidateSet("win-x64", "win-x86", "win-arm64")]
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Path $PSScriptRoot -Parent
$projectPath = Join-Path $repoRoot "MemoryWidgetProvider\MemoryWidgetProvider.csproj"
$outputPath = Join-Path $repoRoot "artifacts\provider\$Runtime"

Write-Host "发布 Provider：$projectPath" -ForegroundColor Cyan
dotnet publish $projectPath -c $Configuration -r $Runtime --self-contained false -o $outputPath

Write-Host "发布完成: $outputPath" -ForegroundColor Green
