param(
    [string]$Version = "1.0.1",
    [switch]$KeepSymbols
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = Join-Path $repoRoot "artifacts"
$workRoot = Join-Path $artifactsRoot "work"
$viewerOutput = Join-Path $workRoot "viewer"
$providerOutput = Join-Path $workRoot "provider"
$payloadOutput = Join-Path $artifactsRoot "FastView-$Version-win-x64"

$viewerProject = Join-Path $repoRoot "src\FastViewDX12\FastViewDX12.csproj"
$providerProject = Join-Path $repoRoot "src\FastView.ThumbnailProvider\FastView.ThumbnailProvider.csproj"

Write-Host "FastView release build $Version" -ForegroundColor Cyan

if (Test-Path $workRoot) {
    Remove-Item $workRoot -Recurse -Force
}

if (Test-Path $payloadOutput) {
    Remove-Item $payloadOutput -Recurse -Force
}

New-Item $viewerOutput -ItemType Directory -Force | Out-Null
New-Item $providerOutput -ItemType Directory -Force | Out-Null
New-Item $payloadOutput -ItemType Directory -Force | Out-Null

Write-Host "[1/4] Publishing FastViewDX12 self-contained..."
dotnet publish $viewerProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -o $viewerOutput

if ($LASTEXITCODE -ne 0) {
    throw "FastViewDX12 publish failed with exit code $LASTEXITCODE."
}

Write-Host "[2/4] Publishing thumbnail provider..."
dotnet publish $providerProject `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -o $providerOutput

if ($LASTEXITCODE -ne 0) {
    throw "Thumbnail-provider publish failed with exit code $LASTEXITCODE."
}

Write-Host "[3/4] Assembling payload..."
Copy-Item (Join-Path $viewerOutput "*") $payloadOutput -Recurse -Force
Copy-Item (Join-Path $providerOutput "*") $payloadOutput -Recurse -Force

if (-not $KeepSymbols) {
    Get-ChildItem $payloadOutput -Filter *.pdb -Recurse -ErrorAction SilentlyContinue |
        Remove-Item -Force
}

Get-ChildItem $payloadOutput -Include *.error.txt,manual-test*.png -Recurse -ErrorAction SilentlyContinue |
    Remove-Item -Force

$requiredFiles = @(
    "FastViewDX12.exe",
    "FastViewDX12.dll",
    "FastView.ThumbnailProvider.comhost.dll",
    "FastView.ThumbnailProvider.dll",
    "coreclr.dll",
    "Shaders\SimpleVS.cso",
    "Shaders\SimplePS.cso",
    "Shaders\BackgroundVS.cso",
    "Shaders\BackgroundPS.cso",
    "Assets\Icons\GlbFile.ico"
)

foreach ($relativePath in $requiredFiles) {
    $fullPath = Join-Path $payloadOutput $relativePath

    if (-not (Test-Path $fullPath)) {
        throw "Required release file is missing: $relativePath"
    }
}

Write-Host "[4/4] Creating release ZIP..."
$zipPath = Join-Path $artifactsRoot "FastView-$Version-win-x64.zip"

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path (Join-Path $payloadOutput "*") -DestinationPath $zipPath -CompressionLevel Optimal

Remove-Item $workRoot -Recurse -Force

Write-Host ""
Write-Host "Release payload: $payloadOutput" -ForegroundColor Green
Write-Host "Release ZIP:     $zipPath" -ForegroundColor Green
