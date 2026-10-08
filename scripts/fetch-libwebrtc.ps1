# Downloads the libwebrtc that a Windows build with calls links, checks it
# against a pinned SHA-256, and unpacks it for LK_CUSTOM_WEBRTC.
#
# The archive is this project's own audio-only build, published as a release of
# this repository by .github/workflows/libwebrtc-windows.yml: LiveKit's
# libwebrtc for its Rust SDK, built from the same pinned sources and patches by
# scripts/build-libwebrtc.ps1 without the H.264 and H.265 codecs or FFmpeg. It
# has the layout of LiveKit's prebuilt webrtc-win-x64-release.zip, so the SDK's
# build takes it the same way. The Windows port of District AI for Linux's and
# District AI core for Rust's scripts/fetch-libwebrtc.sh.
#
#   pwsh scripts/fetch-libwebrtc.ps1 <directory> [-GitHubEnv]
#
# prints the directory to point LK_CUSTOM_WEBRTC at, <directory>\win-x64-release,
# and with -GitHubEnv also writes LK_CUSTOM_WEBRTC to $env:GITHUB_ENV for the
# job's later steps. The archive is kept in <directory> and checked again every
# time, so CI can cache the directory and a local build can reuse it; a second
# run with a good archive downloads nothing.
#
# Why this exists: when LK_CUSTOM_WEBRTC is unset, the LiveKit SDK's build
# (webrtc-sys-build) downloads LiveKit's own prebuilt from GitHub itself, with no
# checksum and no signature, and that prebuilt carries the codecs this build
# leaves out. Setting LK_CUSTOM_WEBRTC to what this script unpacks means the
# build never downloads anything, and what it links is the archive whose digest
# is written below.
#
# The pin moves with the SDK. WebrtcSysBuildVersion is webrtc-sys-build's
# version in Cargo.lock; the script refuses to run when Cargo.lock holds
# another, because a different webrtc-sys against this libwebrtc fails to link
# at best. To move it, change the pins in scripts/build-libwebrtc.ps1, run
# libwebrtc-windows.yml from main, and change the version, Release and Sha256
# here to what it published, together.
#
# Windows x64 only, the one target a release ships.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $Directory,
    [switch] $GitHubEnv
)

$ErrorActionPreference = 'Stop'

$WebrtcSysBuildVersion = '0.3.19'
# The release of this repository that holds the archive. The number after
# "audio-win" counts builds of the same WebRTC commit.
$Release = 'libwebrtc-89d790b-audio-win-1'
$Archive = 'webrtc-win-x64-release.zip'
# Published by libwebrtc-windows.yml run 37839872970, with a build provenance
# attestation (gh attestation verify webrtc-win-x64-release.zip --repo
# distronode-corporation/district-windows).
$Sha256 = '2ebac04343a2b4e3705164831409432356f52ceda751829689c1366e3ba214c6'
$Url = "https://github.com/distronode-corporation/district-windows/releases/download/$Release/$Archive"
$Unpacked = 'win-x64-release'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$lock = Get-Content -Raw (Join-Path $root 'Cargo.lock')
$found = [regex]::Match($lock, '(?m)^name = "webrtc-sys-build"\r?\nversion = "([^"]+)"')
$locked = if ($found.Success) { $found.Groups[1].Value } else { 'none' }
if ($locked -ne $WebrtcSysBuildVersion) {
    throw ("Cargo.lock has webrtc-sys-build '$locked', but this script is pinned to " +
        "$WebrtcSysBuildVersion and its libwebrtc ($Release). Move the pin (see the comment at " +
        'the top of this script) in the same change as the SDK.')
}

New-Item -ItemType Directory -Force -Path $Directory | Out-Null
$dir = (Resolve-Path $Directory).Path
$archivePath = Join-Path $dir $Archive
$stamp = Join-Path $dir "$Unpacked.sha256"

function Test-Archive {
    (Test-Path $archivePath) -and
    ((Get-FileHash -Algorithm SHA256 $archivePath).Hash.ToLowerInvariant() -eq $Sha256)
}

if (-not (Test-Archive)) {
    Remove-Item -Force -ErrorAction SilentlyContinue $archivePath
    Write-Host "downloading $Url"
    $part = "$archivePath.part"
    Invoke-WebRequest -Uri $Url -OutFile $part -MaximumRetryCount 3 -RetryIntervalSec 5
    Move-Item -Force $part $archivePath
    if (-not (Test-Archive)) {
        Remove-Item -Force $archivePath
        throw "$Archive does not match its pinned SHA-256 ($Sha256); refusing to use it."
    }
}

$target = Join-Path $dir $Unpacked
# Named apart from $Unpacked: PowerShell's variable names ignore case.
$current = (Test-Path $target) -and (Test-Path $stamp) -and
    ((Get-Content -Raw $stamp).Trim() -eq $Sha256)
if (-not $current) {
    $staging = Join-Path $dir '.unpacking'
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $target, $staging, $stamp
    Expand-Archive -Path $archivePath -DestinationPath $staging
    Move-Item (Join-Path $staging $Unpacked) $target
    Remove-Item -Recurse -Force $staging
    Set-Content -NoNewline -Path $stamp -Value $Sha256
}

if ($GitHubEnv) {
    "LK_CUSTOM_WEBRTC=$target" | Out-File -Append -Encoding utf8 -FilePath $env:GITHUB_ENV
}
Write-Output $target
