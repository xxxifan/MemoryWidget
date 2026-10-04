# 用 Edge 无头模式把 AssetsSource\assets.html 导出为包内图标与小组件预览图（透明背景 PNG）
$ErrorActionPreference = 'Stop'

$edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
if (-not (Test-Path $edge)) { $edge = "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe" }

$packageRoot = Join-Path (Split-Path $PSScriptRoot -Parent) 'MemoryWidgetProvider.Package'
$url = ([System.Uri](Join-Path $packageRoot 'AssetsSource\assets.html')).AbsoluteUri
$imagesDir = Join-Path $packageRoot 'Images'
$assetsDir = Join-Path $packageRoot 'ProviderAssets'

# 文件, 资源名, 宽, 高, 缩放
$targets = @(
    @((Join-Path $imagesDir 'StoreLogo.png'), 'icon', 50, 50, 1),
    @((Join-Path $imagesDir 'Square44x44Logo.png'), 'icon', 44, 44, 1),
    @((Join-Path $imagesDir 'Square150x150Logo.png'), 'icon', 150, 150, 1),
    @((Join-Path $imagesDir 'Wide310x150Logo.png'), 'wide', 310, 150, 1),
    @((Join-Path $assetsDir 'Memory_Icon.png'), 'icon', 128, 128, 1),
    @((Join-Path $assetsDir 'Memory_Screenshot_Dark.png'), 'dark', 300, 304, 2),
    @((Join-Path $assetsDir 'Memory_Screenshot_Light.png'), 'light', 300, 304, 2)
)

foreach ($t in $targets) {
    $png, $asset, $width, $height, $scale = $t
    & $edge --headless=new --disable-gpu --hide-scrollbars --default-background-color=00000000 `
        --force-device-scale-factor=$scale --window-size="$width,$height" --screenshot="$png" "$url`?a=$asset&w=$width&h=$height" 2>$null | Out-Null
    Write-Host "已导出 $png"
}
