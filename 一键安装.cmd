@echo off
rem Double-click to build and install the widget. Logic lives in scripts\one-click-install.ps1.
rem Keep this file ASCII-only: cmd parses batch files with the ANSI code page.

fltmc >nul 2>&1 || (
    if "%~1"=="elevated" (
        echo Administrator rights are required.
        pause
        exit /b 1
    )
    powershell.exe -NoProfile -Command "Start-Process -FilePath '%~f0' -ArgumentList 'elevated' -Verb RunAs"
    exit /b
)

set "PS=powershell.exe"
where pwsh.exe >nul 2>&1 && set "PS=pwsh.exe"

"%PS%" -NoProfile -ExecutionPolicy Bypass -Command "$root = '%~dp0'; & ([scriptblock]::Create([IO.File]::ReadAllText($root + 'scripts\one-click-install.ps1'))) -RepoRoot $root"
pause
