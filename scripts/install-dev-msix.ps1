param(
    [string]$MsixPath = ".\artifacts\MemoryWidgetProvider.Dev.msix",
    [string]$CertificatePath = ".\artifacts\cert\MemoryWidgetProvider.Dev.cer"
)

$ErrorActionPreference = "Stop"

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdministrator)) {
    throw "请使用管理员 PowerShell 运行该脚本（需要导入证书到 LocalMachine 证书库）。"
}

if (-not (Test-Path $MsixPath)) {
    throw "未找到 MSIX：$MsixPath"
}

if (-not (Test-Path $CertificatePath)) {
    throw "未找到证书：$CertificatePath"
}

Write-Host "导入证书到 LocalMachine\\TrustedPeople..." -ForegroundColor Cyan
Import-Certificate -FilePath $CertificatePath -CertStoreLocation "Cert:\LocalMachine\TrustedPeople" | Out-Null

Write-Host "导入证书到 LocalMachine\\Root..." -ForegroundColor Cyan
Import-Certificate -FilePath $CertificatePath -CertStoreLocation "Cert:\LocalMachine\Root" | Out-Null

Write-Host "安装开发者 MSIX..." -ForegroundColor Cyan
Add-AppxPackage -Path $MsixPath -ForceUpdateFromAnyVersion

Write-Host "安装完成。可以按 Win + W 打开小组件面板并添加“内存占用率”。" -ForegroundColor Green
