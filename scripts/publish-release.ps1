# == Publish self-contained vigil.exe and assemble a handoff zip == #
param(
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "$PSScriptRoot\..\dist"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path "$PSScriptRoot\.."
$PublishDir = Join-Path $RepoRoot "publish"
$BundleName = "Vigil-$Runtime"
$BundleDir = Join-Path $OutputRoot $BundleName
$SamplesDir = Join-Path $BundleDir "samples"
$TestFilesRoot = Join-Path $RepoRoot "Docs\TestFiles\SimpleLogIncident"

Write-Host "Publishing Vigil.Cli ($Runtime, self-contained single-file)..." -ForegroundColor Cyan
Push-Location $RepoRoot
try {
    dotnet publish Vigil.Cli\Vigil.Cli.csproj `
        -c Release `
        -r $Runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $PublishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
}
finally {
    Pop-Location
}

$ExePath = Join-Path $PublishDir "vigil.exe"
if (-not (Test-Path $ExePath)) {
    throw "Expected publish output at $ExePath"
}

if (Test-Path $BundleDir) { Remove-Item $BundleDir -Recurse -Force }
New-Item -ItemType Directory -Path $SamplesDir -Force | Out-Null

Copy-Item $ExePath $BundleDir
Copy-Item (Join-Path $TestFilesRoot "app.log") $SamplesDir
Copy-Item (Join-Path $TestFilesRoot "changes.txt") $SamplesDir

@'
Vigil — Quick Start (Windows)
=============================

This folder is self-contained. No .NET install required.

1. Open PowerShell or Windows Terminal in this folder.
2. Interactive TUI (primary experience):
     .\vigil
3. One-shot offline demo (no API key needed):
     Get-Content .\samples\app.log, .\samples\changes.txt | .\vigil diagnose --offline --symptom "errors after deploy"
4. Optional — enable Grok for natural-language turns:
     $env:XAI_API_KEY = "xai-your-key-here"
     .\vigil

Notes:
- Double-clicking vigil.exe will not work; use a terminal.
- Windows SmartScreen may warn on unsigned executables: More info -> Run anyway.
- Type /help inside the TUI for slash commands.
'@ | Set-Content -Path (Join-Path $BundleDir "QUICKSTART.txt") -Encoding UTF8

@'
# == Offline demo launcher == #
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $here
Write-Host "Running offline diagnosis against bundled samples..." -ForegroundColor Cyan
Get-Content .\samples\app.log, .\samples\changes.txt |
    .\vigil diagnose --offline --symptom "intermittent errors after deploy"
'@ | Set-Content -Path (Join-Path $BundleDir "run-demo.ps1") -Encoding UTF8

$ZipPath = Join-Path $OutputRoot "$BundleName.zip"
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path $BundleDir -DestinationPath $ZipPath -Force

$exeSizeMb = [math]::Round((Get-Item $ExePath).Length / 1MB, 1)
$zipSizeMb = [math]::Round((Get-Item $ZipPath).Length / 1MB, 1)

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  Exe:  $ExePath ($exeSizeMb MB)"
Write-Host "  Zip:  $ZipPath ($zipSizeMb MB)"
Write-Host "  Hand $ZipPath to users; they unzip and follow QUICKSTART.txt."