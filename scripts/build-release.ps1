# Builds zapret-gui publish output and Inno Setup installer for GitHub Releases.
# Requires: .NET SDK, Inno Setup 6 (ISCC.exe on PATH or default install path)

param(
    [string]$Version = "",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "zapret-gui.csproj"
$artifacts = Join-Path $root "artifacts"
$publishDir = Join-Path $artifacts "publish\$Runtime"
$installerOut = Join-Path $artifacts "installer"

if (-not $Version) {
    [xml]$csproj = Get-Content $project
    $Version = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1).Trim()
}

if (-not $Version) {
    throw "Version not set. Pass -Version or set <Version> in zapret-gui.csproj."
}

Write-Host "Building zapret-gui v$Version ($Runtime)..."

if (Test-Path $artifacts) {
    Remove-Item $artifacts -Recurse -Force
}

$publishArgs = @(
    "publish", $project,
    "-c", $Configuration,
    "-r", $Runtime,
    "-o", $publishDir,
    "/p:Version=$Version",
    "/p:AssemblyVersion=$Version.0",
    "/p:FileVersion=$Version.0",
    "/p:InformationalVersion=$Version"
)

if ($SelfContained) {
    $publishArgs += "--self-contained", "true", "-p:PublishSingleFile=false"
} else {
    $publishArgs += "--self-contained", "false"
}

dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$iscc = $env:ISCC_PATH
if (-not $iscc) {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    foreach ($path in $candidates) {
        if (Test-Path $path) {
            $iscc = $path
            break
        }
    }
}

if (-not $iscc -or -not (Test-Path $iscc)) {
    Write-Warning "Inno Setup (ISCC.exe) not found. Publish output is at: $publishDir"
    Write-Warning "Install Inno Setup 6 and re-run, or set ISCC_PATH."
    exit 0
}

$iss = Join-Path $root "installer\zapret-gui.iss"
New-Item -ItemType Directory -Force -Path $installerOut | Out-Null

& $iscc $iss `
    "/DMyAppVersion=$Version" `
    "/DPublishDir=$publishDir"

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$setup = Get-ChildItem $installerOut -Filter "zapret-gui_win-x64_v$Version.exe" | Select-Object -First 1
Write-Host ""
Write-Host "Release asset ready:"
Write-Host "  $($setup.FullName)"
Write-Host ""
Write-Host "Upload to GitHub release as: zapret-gui_win-x64_v$Version.exe"
