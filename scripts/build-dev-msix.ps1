param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidateSet("win-x64", "win-x86", "win-arm64")]
    [string]$Runtime = "win-x64",
    [string]$PackageVersion = "1.0.0.0",
    [string]$CertificatePassword = "DevWidget@2026"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Path $PSScriptRoot -Parent
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
$pfxPath = Join-Path $certOut "MemoryWidgetProvider.Dev.pfx"

Remove-Item $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $providerOut, $imagesOut, $assetsOut, $publicOut, $certOut | Out-Null

Write-Host "1/5 发布 Provider 二进制..." -ForegroundColor Cyan
dotnet publish $providerProject -c $Configuration -r $Runtime --self-contained false -o $providerOut

Write-Host "2/5 准备 AppxManifest..." -ForegroundColor Cyan
Copy-Item $manifestSource (Join-Path $stagingRoot "AppxManifest.xml") -Force
[xml]$manifestXml = Get-Content (Join-Path $stagingRoot "AppxManifest.xml")
$manifestXml.Package.Identity.Version = $PackageVersion
$manifestXml.Save((Join-Path $stagingRoot "AppxManifest.xml"))

if (Test-Path $publicFolderSource) {
    Copy-Item (Join-Path $publicFolderSource '*') $publicOut -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "3/5 生成占位图标资源..." -ForegroundColor Cyan
Add-Type -AssemblyName System.Drawing

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

New-PlaceholderPng -Path (Join-Path $imagesOut "StoreLogo.png") -Width 50 -Height 50 -Label "MEM"
New-PlaceholderPng -Path (Join-Path $imagesOut "Square44x44Logo.png") -Width 44 -Height 44 -Label "M"
New-PlaceholderPng -Path (Join-Path $imagesOut "Square150x150Logo.png") -Width 150 -Height 150 -Label "MEM"
New-PlaceholderPng -Path (Join-Path $imagesOut "Wide310x150Logo.png") -Width 310 -Height 150 -Label "MEMORY"
New-PlaceholderPng -Path (Join-Path $assetsOut "Memory_Icon.png") -Width 128 -Height 128 -Label "RAM"
New-PlaceholderPng -Path (Join-Path $assetsOut "Memory_Screenshot.png") -Width 748 -Height 748 -Label "Memory Widget"

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

Write-Host "5/5 生成并应用开发者签名..." -ForegroundColor Cyan
$cert = New-SelfSignedCertificate `
    -Type Custom `
    -Subject "CN=MemoryWidgetProvider" `
    -KeyUsage DigitalSignature `
    -FriendlyName "MemoryWidgetProvider Dev Cert" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3")

$securePassword = ConvertTo-SecureString -String $CertificatePassword -Force -AsPlainText
Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword | Out-Null
& $signTool sign /fd SHA256 /f $pfxPath /p $CertificatePassword $msixPath

Write-Host "开发者包构建完成。" -ForegroundColor Green
Write-Host "MSIX: $msixPath"
Write-Host "CER : $cerPath"
Write-Host "PFX : $pfxPath"
Write-Host ""
Write-Host "下一步（管理员 PowerShell）：" -ForegroundColor Yellow
Write-Host ".\\scripts\\install-dev-msix.ps1"
