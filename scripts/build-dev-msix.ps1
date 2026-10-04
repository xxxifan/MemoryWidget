param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidateSet("win-x64", "win-x86", "win-arm64")]
    [string]$Runtime = "win-x64",
    [string]$PackageVersion = "1.0.0.0",
    [switch]$NoNextStepHint,
    # 强制生成新签名证书（已安装的用户需要重新导入证书）
    [switch]$NewCertificate
)

$ErrorActionPreference = "Stop"

# 被 one-click-install.ps1 以脚本块方式执行时 $PSScriptRoot 为空，此时当前目录即仓库根目录
$repoRoot = if ($PSScriptRoot) { Split-Path -Path $PSScriptRoot -Parent } else { (Get-Location).Path }
$providerProject = Join-Path $repoRoot "MemoryWidgetProvider\MemoryWidgetProvider.csproj"
$manifestSource = Join-Path $repoRoot "MemoryWidgetProvider.Package\Package.appxmanifest"
$publicFolderSource = Join-Path $repoRoot "MemoryWidgetProvider.Package\Public"

$artifactsRoot = Join-Path $repoRoot "artifacts"
$stagingRoot = Join-Path $artifactsRoot "staging"
$providerOut = Join-Path $stagingRoot "MemoryWidgetProvider"
$imagesOut = Join-Path $stagingRoot "Images"
$assetsOut = Join-Path $stagingRoot "ProviderAssets"
$publicOut = Join-Path $stagingRoot "Public"
$certOut = Join-Path $artifactsRoot "cert"
$msixPath = Join-Path $artifactsRoot "MemoryWidgetProvider.Dev.msix"
$cerPath = Join-Path $certOut "MemoryWidgetProvider.Dev.cer"

Remove-Item $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $providerOut, $imagesOut, $assetsOut, $publicOut, $certOut | Out-Null

Write-Host "1/5 发布 Provider 二进制..." -ForegroundColor Cyan
dotnet publish $providerProject -c $Configuration -r $Runtime --self-contained false -o $providerOut
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（退出码 $LASTEXITCODE）。" }

Write-Host "2/5 准备 AppxManifest..." -ForegroundColor Cyan
Copy-Item $manifestSource (Join-Path $stagingRoot "AppxManifest.xml") -Force
[xml]$manifestXml = Get-Content -Raw -Encoding UTF8 (Join-Path $stagingRoot "AppxManifest.xml")
$manifestXml.Package.Identity.Version = $PackageVersion
$manifestXml.Save((Join-Path $stagingRoot "AppxManifest.xml"))

if (Test-Path $publicFolderSource) {
    Copy-Item (Join-Path $publicFolderSource '*') $publicOut -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "3/5 准备图标资源..." -ForegroundColor Cyan
Add-Type -AssemblyName System.Drawing
$imagesSource = Join-Path $repoRoot "MemoryWidgetProvider.Package\Images"
$assetsSource = Join-Path $repoRoot "MemoryWidgetProvider.Package\ProviderAssets"

function New-PlaceholderPng {
    param(
        [string]$Path,
        [int]$Width,
        [int]$Height,
        [string]$Label
    )

    $bitmap = New-Object System.Drawing.Bitmap($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::FromArgb(255, 24, 24, 28))

    $bandBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0, 120, 215))
    $graphics.FillRectangle($bandBrush, 0, 0, $Width, [Math]::Max(24, [int]($Height * 0.22)))

    $fontSize = [Math]::Max(12, [int]($Height * 0.16))
    $font = New-Object System.Drawing.Font("Segoe UI", $fontSize, [System.Drawing.FontStyle]::Bold)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $rect = New-Object System.Drawing.RectangleF(0, 0, $Width, $Height)
    $graphics.DrawString($Label, $font, [System.Drawing.Brushes]::White, $rect, $format)

    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)

    $format.Dispose()
    $font.Dispose()
    $bandBrush.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

# 优先使用仓库内由 scripts\export-assets.ps1 导出的资源，缺失时生成占位图
function Copy-AssetOrPlaceholder {
    param(
        [string]$SourceDir,
        [string]$OutDir,
        [string]$Name,
        [int]$Width,
        [int]$Height,
        [string]$Label
    )

    $source = Join-Path $SourceDir $Name
    if (Test-Path $source) {
        Copy-Item $source (Join-Path $OutDir $Name) -Force
    } else {
        Write-Warning "缺少 $source，使用占位图。"
        New-PlaceholderPng -Path (Join-Path $OutDir $Name) -Width $Width -Height $Height -Label $Label
    }
}

Copy-AssetOrPlaceholder $imagesSource $imagesOut "StoreLogo.png" 50 50 "MEM"
Copy-AssetOrPlaceholder $imagesSource $imagesOut "Square44x44Logo.png" 44 44 "M"
Copy-AssetOrPlaceholder $imagesSource $imagesOut "Square150x150Logo.png" 150 150 "MEM"
Copy-AssetOrPlaceholder $imagesSource $imagesOut "Wide310x150Logo.png" 310 150 "MEMORY"
Copy-AssetOrPlaceholder $assetsSource $assetsOut "Memory_Icon.png" 128 128 "RAM"
Copy-AssetOrPlaceholder $assetsSource $assetsOut "Memory_Screenshot_Dark.png" 600 608 "Memory Widget"
Copy-AssetOrPlaceholder $assetsSource $assetsOut "Memory_Screenshot_Light.png" 600 608 "Memory Widget"

$sdkToolsBase = Join-Path $env:USERPROFILE ".nuget\packages\microsoft.windows.sdk.buildtools"
$sdkVersion = Get-ChildItem $sdkToolsBase -Directory -ErrorAction Stop |
    Sort-Object Name -Descending |
    Select-Object -First 1
if (-not $sdkVersion) { throw "未在 NuGet 缓存中找到 microsoft.windows.sdk.buildtools，请先执行 dotnet restore。" }
$sdkBinRoot = Get-ChildItem (Join-Path $sdkVersion.FullName "bin") -Directory |
    Sort-Object Name -Descending |
    Select-Object -First 1
$sdkRoot = Join-Path $sdkBinRoot.FullName "x64"
$makeAppx = Join-Path $sdkRoot "makeappx.exe"
$signTool = Join-Path $sdkRoot "signtool.exe"

if (-not (Test-Path $makeAppx)) {
    throw "未找到 makeappx.exe：$makeAppx"
}
if (-not (Test-Path $signTool)) {
    throw "未找到 signtool.exe：$signTool"
}

Write-Host "4/5 打包 MSIX..." -ForegroundColor Cyan
Remove-Item $msixPath -Force -ErrorAction SilentlyContinue
& $makeAppx pack /d $stagingRoot /p $msixPath /o

Write-Host "5/5 应用开发者签名..." -ForegroundColor Cyan
# 复用本机已有的签名证书，已安装过的用户更新时无需重新导入；本机没有时（如用户自行构建）才生成新证书
$cert = $null
if (-not $NewCertificate) {
    $cert = Get-ChildItem "Cert:\CurrentUser\My" -CodeSigningCert |
        Where-Object { $_.Subject -eq "CN=MemoryWidgetProvider" -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(30) } |
        Sort-Object NotBefore -Descending |
        Select-Object -First 1
}
if ($cert) {
    Write-Host "复用已有证书 $($cert.Thumbprint)（有效期至 $($cert.NotAfter.ToString('yyyy-MM-dd'))）"
} else {
    Write-Host "生成新的开发者证书..."
    $cert = New-SelfSignedCertificate `
        -Type Custom `
        -Subject "CN=MemoryWidgetProvider" `
        -KeyUsage DigitalSignature `
        -FriendlyName "MemoryWidgetProvider Dev Cert" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -NotAfter (Get-Date).AddYears(10) `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3")
}

# 私钥只留在当前用户证书库，直接按指纹签名，不导出 PFX；只导出公钥 .cer 供安装时信任
Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
& $signTool sign /fd SHA256 /s My /sha1 $cert.Thumbprint $msixPath
if ($LASTEXITCODE -ne 0) { throw "signtool 签名失败（退出码 $LASTEXITCODE）。" }

Write-Host "开发者包构建完成。" -ForegroundColor Green
Write-Host "MSIX: $msixPath"
Write-Host "CER : $cerPath"
if (-not $NoNextStepHint) {
    Write-Host ""
    Write-Host "下一步（管理员 PowerShell）：" -ForegroundColor Yellow
    Write-Host ".\\scripts\\install-dev-msix.ps1"
}
