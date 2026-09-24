<#
    publish.ps1 — Publish script for ZRecover (Dual Mode: Full & Lite)
    Adheres to AgentOption .NET Publish Release standard & ZeroUniverse rules.
#>
[CmdletBinding()]
param(
    [ValidateSet('Full', 'Lite', 'All')]
    [string]$Mode = 'All',
    [ValidateSet('UI', 'CLI', 'All')]
    [string]$Target = 'All',
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$Root = $PSScriptRoot
$UiProj = Join-Path $Root "src\ZRecover.UI\ZRecover.UI.csproj"
$CliProj = Join-Path $Root "src\ZRecover.Cli\ZRecover.Cli.csproj"
$Dist = Join-Path $Root "publish"

# Ensure any running instances are closed before publishing
Get-Process -Name "*ZRecover*", "*zrec*" -ErrorAction SilentlyContinue | Stop-Process -Force

if ($Mode -eq 'Full' -or $Mode -eq 'All') {
    Write-Host ">>> Publishing ZRecover FULL (Self-Contained Single File)..." -ForegroundColor Cyan
    
    if ($Target -eq 'UI' -or $Target -eq 'All') {
        $outUiFull = Join-Path $Dist "ui-full"
        dotnet publish $UiProj -c $Configuration -r $Runtime --self-contained true `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:EnableCompressionInSingleFile=true `
            -o $outUiFull
        Write-Host "  ✔ UI Full generated at: $outUiFull\ZRecover.exe" -ForegroundColor Green
    }
    
    if ($Target -eq 'CLI' -or $Target -eq 'All') {
        $outCliFull = Join-Path $Dist "cli-full"
        dotnet publish $CliProj -c $Configuration -r $Runtime --self-contained true `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:EnableCompressionInSingleFile=true `
            -o $outCliFull
        Write-Host "  ✔ CLI Full generated at: $outCliFull\zerorecover.exe" -ForegroundColor Green
    }
}

if ($Mode -eq 'Lite' -or $Mode -eq 'All') {
    Write-Host ">>> Publishing ZRecover LITE (Framework-Dependent Single File)..." -ForegroundColor Cyan
    
    if ($Target -eq 'UI' -or $Target -eq 'All') {
        $outUiLite = Join-Path $Dist "ui-lite"
        dotnet publish $UiProj -c $Configuration -r $Runtime --self-contained false `
            -p:PublishSingleFile=true `
            -o $outUiLite
        Write-Host "  ✔ UI Lite generated at: $outUiLite\ZRecover.exe" -ForegroundColor Green
    }
    
    if ($Target -eq 'CLI' -or $Target -eq 'All') {
        $outCliLite = Join-Path $Dist "cli-lite"
        dotnet publish $CliProj -c $Configuration -r $Runtime --self-contained false `
            -p:PublishSingleFile=true `
            -o $outCliLite
        Write-Host "  ✔ CLI Lite generated at: $outCliLite\zerorecover.exe" -ForegroundColor Green
    }
}

Write-Host ">>> ZRecover publish completed successfully!" -ForegroundColor Green
