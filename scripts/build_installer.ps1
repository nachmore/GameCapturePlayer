# Build & package Game Capture Player using Inno Setup
# - Converts img/logo.png to img/logo.ico (PNG-compressed ICO)
# - Publishes a self-contained x64 build
# - Builds installer via Inno Setup (ISCC)

$ErrorActionPreference = 'Stop'

function New-IcoFromPng {
    param(
        [Parameter(Mandatory=$true)][string]$PngPath,
        [Parameter(Mandatory=$true)][string]$IcoPath
    )
    if (-not (Test-Path $PngPath)) { throw "PNG not found: $PngPath" }
    $pngBytes = [IO.File]::ReadAllBytes($PngPath)
    # ICO header (ICONDIR)
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    # Reserved (0), Type (1 = icon), Count (1)
    $bw.Write([UInt16]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]1)
    # ICONDIRENTRY
    # Width/Height as bytes: 0 means 256
    $bw.Write([Byte]0)  # width 256
    $bw.Write([Byte]0)  # height 256
    $bw.Write([Byte]0)  # color count
    $bw.Write([Byte]0)  # reserved
    $bw.Write([UInt16]0) # planes (0 for PNG-compressed)
    $bw.Write([UInt16]0) # bitcount (0 for PNG-compressed)
    $bw.Write([UInt32]$pngBytes.Length) # bytes in resource
    $bw.Write([UInt32]22) # offset: 6 (ICONDIR) + 16 (ICONDIRENTRY)
    # Image data (PNG)
    $bw.Write($pngBytes)
    $bw.Flush()
    [IO.File]::WriteAllBytes($IcoPath, $ms.ToArray())
}

# Paths
$RepoRoot = Split-Path -Parent $PSScriptRoot
$SrcDir   = Join-Path $RepoRoot 'src'
$InstallerDir = Join-Path $RepoRoot 'installer'
$PublishDir = Join-Path $InstallerDir 'publish'
$LogoPng = Join-Path $SrcDir 'img/logo.png'
$LogoIco = Join-Path $SrcDir 'img/logo.ico'

Write-Host '=== Step 1/3: Ensure logo.ico exists ==='
New-Item -ItemType Directory -Force -Path (Split-Path $LogoIco) | Out-Null
New-IcoFromPng -PngPath $LogoPng -IcoPath $LogoIco
Write-Host "Generated ICO: $LogoIco"

Write-Host '=== Step 2/3: dotnet publish (self-contained, single-file) ==='
New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null
$csproj = Join-Path $SrcDir 'GameCapturePlayer.csproj'
& dotnet publish $csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true -p:PublishTrimmed=false -o $PublishDir

Write-Host '=== Step 3/3: Build installer with Inno Setup ==='
$defaultISCC = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'
if (Test-Path $defaultISCC) {
    $iscc = $defaultISCC
} else {
    # Try PATH
    $iscc = (Get-Command ISCC -ErrorAction SilentlyContinue)?.Source
}
if (-not $iscc) { throw 'ISCC.exe not found. Install Inno Setup or add ISCC to PATH.' }
$iss = Join-Path $InstallerDir 'Setup.iss'
& $iscc $iss
Write-Host "Installer built. See: $(Join-Path $InstallerDir 'dist')"
