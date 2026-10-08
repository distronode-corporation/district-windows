# Builds the audio-only libwebrtc a Windows build with calls links: LiveKit's own
# build of libwebrtc for its Rust SDK (webrtc-sys/libwebrtc/build_windows.cmd),
# from the same pinned sources and patches, with the H.264 and H.265 codecs and
# FFmpeg left out. The Windows port of District AI for Linux's
# scripts/build-libwebrtc.sh, at the same pins.
#
#   pwsh scripts/build-libwebrtc.ps1 <directory>
#
# leaves <directory>\webrtc-win-x64-release.zip and a .sha256 file beside it,
# and the defined symbols that mention H.264, H.265 or FFmpeg in
# <directory>\libwebrtc-symbols.txt. The directory must be empty or missing:
# LiveKit's script applies its patches to the checkout, so a second run in the
# same directory would try to apply them twice. The archive has the layout of
# LiveKit's prebuilt webrtc-win-x64-release.zip (a win-x64-release/ directory
# holding include/, lib/webrtc.lib, args.gn, webrtc.ninja, desktop_capture.ninja
# and LICENSE.md), so scripts/fetch-libwebrtc and LK_CUSTOM_WEBRTC take it as
# they take LiveKit's.
#
# .github/workflows/libwebrtc-windows.yml runs this on a GitHub windows-2022
# runner, and that build is the one a release links. It needs Windows x64,
# Visual Studio 2022 with the Windows SDK and its debugging tools (the runner
# image has both), git, python with setuptools, ninja on PATH, about 40 GB of
# free disk, and DEPOT_TOOLS_WIN_TOOLCHAIN=0 in the environment so that
# depot_tools uses the installed Visual Studio. It downloads about 15 GB and
# takes hours.
#
# Why: LiveKit builds its prebuilt with ffmpeg_branding="Chrome" and
# rtc_use_h264=true, which link FFmpeg's H.264 decoder and the OpenH264 encoder
# into the library, and its Windows recipe leaves rtc_use_h265 to WebRTC's
# default. Those codecs carry patent licensing that an audio-only app has no use
# for. This build sets ffmpeg_branding="Chromium", rtc_use_h264=false and
# rtc_use_h265=false, and otherwise keeps LiveKit's: the WebRTC source it pins,
# every patch its Windows recipe applies (plus its licence generator's fix,
# from its Linux recipe), and every other argument, including
# use_custom_libcxx=false (MSVC's own standard library, which webrtc-sys
# compiles against) and Chromium's static CRT (/MT), which is why this
# repository builds Rust with +crt-static on Windows.
#
# The pins are District AI for Linux's, and move with the SDK. RUST_SDKS_COMMIT
# is the commit of github.com/livekit/rust-sdks that LiveKit's webrtc-* release
# tag for this webrtc-sys-build names, and whose webrtc-sys/libwebrtc/ holds the
# recipe; WEBRTC_COMMIT is the commit of github.com/webrtc-sdk/webrtc that the
# recipe's .gclient branch pointed at when LiveKit built that release. The
# .gclient names a branch, which moves, so this script pins the commit instead
# and refuses a checkout at any other.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $Directory
)

$ErrorActionPreference = 'Stop'
# A native command that exits non-zero stops the script, as `set -e` does.
$PSNativeCommandUseErrorActionPreference = $true

$WebrtcSysBuildVersion = '0.3.19'
$RustSdksCommit = '24f7126929efde0da13d22e02d8c7a5a05be682d'
$WebrtcCommit = '89d790b40447c3c5c54c3edd58aa53d285e35fa7'
$Archive = 'webrtc-win-x64-release.zip'
$Unpacked = 'win-x64-release'

# The patches build_windows.cmd applies to src, in its order, and the licence
# generator's fix this script adds. Checked after the build, because the recipe
# does not stop when one fails to apply.
$Patches = @(
    'add_licenses.patch',
    'fix_license_json_parsing.patch',
    'add_deps.patch',
    'ssl_verify_callback_with_native_handle.patch',
    'external_audio_source.patch'
)

# What must not be in the library: FFmpeg's H.264 and H.265 decoders, and the
# OpenH264 encoder and decoder. Linux's list, case-insensitive.
$CodecPattern = 'ff_h264|ff_hevc|hevc|WelsCreateSVCEncoder|WelsCreateDecoder|WelsDecoder|avcodec_'
# What is printed for the record when it is defined: WebRTC's own RTP code for
# those formats (packetizers, SDP names) stays; it parses headers and carries no
# codec.
$MentionPattern = 'h264|h265|ffmpeg|openh264'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

# Cargo.lock names webrtc-sys-build once the build links the LiveKit engine.
# When it does, it must be the version these pins are for.
$lock = Get-Content -Raw (Join-Path $root 'Cargo.lock')
$locked = [regex]::Match($lock, '(?m)^name = "webrtc-sys-build"\r?\nversion = "([^"]+)"')
if ($locked.Success -and $locked.Groups[1].Value -ne $WebrtcSysBuildVersion) {
    throw ("Cargo.lock has webrtc-sys-build '$($locked.Groups[1].Value)', but this script is " +
        "pinned to $WebrtcSysBuildVersion's libwebrtc. Move the pins (see the comment at the top " +
        'of this script) in the same change as the SDK.')
}

New-Item -ItemType Directory -Force -Path $Directory | Out-Null
$work = (Resolve-Path $Directory).Path
if (Get-ChildItem -Force $work) {
    throw "$work is not empty; give this script an empty or missing directory."
}

# Fetch one commit of a repository into a directory, without its history.
# Extra `git config` pairs are set before the checkout.
function Get-Commit([string] $Url, [string] $Commit, [string] $Dir, [string[]] $Config = @()) {
    git init --quiet $Dir
    for ($i = 0; $i -lt $Config.Count; $i += 2) {
        git -C $Dir config $Config[$i] $Config[$i + 1]
    }
    git -C $Dir remote add origin $Url
    git -C $Dir fetch --quiet --depth 1 origin $Commit
    git -C $Dir -c advice.detachedHead=false checkout --quiet FETCH_HEAD
}

# Replace one exact piece of text in a file, refusing unless it occurs exactly
# once, so an upstream change to the recipe fails here instead of building
# something other than what this script says. Line endings are left as they are.
function Set-Once([string] $Path, [string] $Old, [string] $New) {
    $text = [IO.File]::ReadAllText($Path)
    $count = ([regex]::Matches($text, [regex]::Escape($Old))).Count
    if ($count -ne 1) {
        throw "${Path}: expected '$Old' exactly once, found it $count times"
    }
    [IO.File]::WriteAllText($Path, $text.Replace($Old, $New), [Text.UTF8Encoding]::new($false))
}

# LiveKit's recipe, at the pinned commit. Checked out with the runner's line
# endings: cmd.exe misreads labels in a batch file with bare LF endings.
$sdks = Join-Path $work 'rust-sdks'
Get-Commit 'https://github.com/livekit/rust-sdks.git' $RustSdksCommit $sdks
$recipe = Join-Path $sdks 'webrtc-sys\libwebrtc'
$cmd = Join-Path $recipe 'build_windows.cmd'

# The three arguments, and the source pinned to a commit. LiveKit's CI appends
# target_os the same way.
Set-Once $cmd 'ffmpeg_branding=\"Chrome\"' 'ffmpeg_branding=\"Chromium\"'
Set-Once $cmd 'rtc_use_h264=true' 'rtc_use_h264=false rtc_use_h265=false'
# LiveKit's own fix for its licence generator, which its Linux recipe applies
# and its Windows recipe does not: GN warns that ffmpeg_branding has no effect
# on Windows (no FFmpeg is built there), the warning lands in front of the JSON
# the generator parses, and without the fix there is no LICENSE.md.
# The patch has a context line that is empty (no leading space), which git
# apply reads as corrupt once the checkout has given it CRLF endings, as the
# runner's git does: it is applied with LF endings.
$licenceFix = Join-Path $recipe 'patches\fix_license_json_parsing.patch'
[IO.File]::WriteAllText($licenceFix, [IO.File]::ReadAllText($licenceFix).Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
$applyLicenses = 'call git apply "%COMMAND_DIR%/patches/add_licenses.patch" -v --ignore-space-change --ignore-whitespace --whitespace=nowarn'
$eol = if ([IO.File]::ReadAllText($cmd).Contains("`r`n")) { "`r`n" } else { "`n" }
Set-Once $cmd $applyLicenses ($applyLicenses + $eol + $applyLicenses.Replace('add_licenses.patch', 'fix_license_json_parsing.patch'))
Set-Once (Join-Path $recipe '.gclient') "webrtc.git@m150_release'" "webrtc.git@$WebrtcCommit'"
Add-Content -NoNewline -Path (Join-Path $recipe '.gclient') -Value "`ntarget_os = [`"win`"]`n"
Write-Host 'The recipe, as this build runs it:'
git -C $sdks --no-pager diff -- webrtc-sys/libwebrtc

Set-Location $recipe
git clone --quiet --depth 1 https://chromium.googlesource.com/chromium/tools/depot_tools.git
$env:PATH = "$recipe\depot_tools;$env:PATH"

# LiveKit's script syncs only when src\ is missing, so the sync happens here,
# where the commit can be checked. The commit is fetched first, so gclient finds
# it in place instead of resolving a branch. Chromium's Windows checkout wants
# LF endings and long paths.
Get-Commit 'https://github.com/webrtc-sdk/webrtc.git' $WebrtcCommit 'src' `
    @('core.autocrlf', 'false', 'core.filemode', 'false', 'core.longpaths', 'true')
& gclient.bat sync -D --no-history
$head = (git -C src rev-parse HEAD).Trim()
if ($head -ne $WebrtcCommit) {
    throw "src is at $head, not the pinned $WebrtcCommit."
}

# The time every binary is stamped with. Chromium's build takes it from
# build/util/LASTCHANGE.committime, which the sync's lastchange hook writes from
# the last commit of src/build that carries a Change-Id; a checkout without
# history has none, the hook falls back to 0, and lld-link then refuses the
# negative /TIMESTAMP it is given ("invalid timestamp"). Linux links ELF, which
# has no such stamp. The commit time of the pinned src/build is what a full
# checkout would give, and it depends only on the pins.
$committime = (git -C src/build log -1 --format=%ct HEAD).Trim()
if ($committime -notmatch '^[1-9][0-9]{9}$') {
    throw "src/build's commit time is '$committime', not a time."
}
Set-Content -NoNewline -Path 'src\build\util\LASTCHANGE.committime' -Value $committime
Write-Host "build timestamp: $committime"

# LiveKit's recipe, which does not stop on a failed step (its exit status is
# its last copy's), so what it leaves is checked below instead.
$PSNativeCommandUseErrorActionPreference = $false
& cmd.exe /d /c build_windows.cmd --arch x64 --profile release
Write-Host "build_windows.cmd exited with $LASTEXITCODE"
$PSNativeCommandUseErrorActionPreference = $true

$out = Join-Path $recipe $Unpacked
foreach ($f in 'lib\webrtc.lib', 'args.gn', 'webrtc.ninja', 'desktop_capture.ninja', 'LICENSE.md', 'include') {
    if (-not (Test-Path (Join-Path $out $f))) {
        throw "The build left no $Unpacked\$f."
    }
}

# Every patch the recipe names is in the source the library was built from.
foreach ($p in $Patches) {
    git -C src apply --check --reverse --ignore-space-change --ignore-whitespace (Join-Path $recipe "patches\$p")
    Write-Host "applied: $p"
}

# The arguments the library was built with, as GN recorded them.
$gnArgs = Get-Content -Raw (Join-Path $out 'args.gn')
$want = [ordered]@{
    'ffmpeg_branding'   = '"Chromium"'
    'rtc_use_h264'      = 'false'
    'rtc_use_h265'      = 'false'
    'use_custom_libcxx' = 'false'
    'is_debug'          = 'false'
    'target_cpu'        = '"x64"'
}
foreach ($name in $want.Keys) {
    $found = @([regex]::Matches($gnArgs, "\b$name\s*=\s*(\S+)") | ForEach-Object { $_.Groups[1].Value })
    if ($found.Count -ne 1 -or $found[0] -ne $want[$name]) {
        throw "args.gn sets $name to [$($found -join ', ')], not [$($want[$name])]"
    }
    Write-Host "args.gn: $name = $($want[$name])"
}

# No H.264 or H.265 codec in the library, read from its COFF symbol tables with
# dumpbin: a symbol is defined when its section is not UNDEF.
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$dumpbin = & $vswhere -latest -products * -find 'VC\Tools\MSVC\**\bin\Hostx64\x64\dumpbin.exe' | Select-Object -First 1
if (-not $dumpbin) { throw 'no dumpbin.exe in the installed Visual Studio' }
$all = Join-Path $work 'dumpbin-symbols.txt'
& $dumpbin /nologo /symbols "/out:$all" (Join-Path $out 'lib\webrtc.lib')
Write-Host ("dumpbin listed {0:N0} bytes of symbols" -f (Get-Item $all).Length)
if ((Get-Item $all).Length -lt 1MB) { throw 'dumpbin listed almost nothing; the check would prove nothing.' }
$defined = { param($m) $m.Line -notmatch '\sUNDEF\s' }
$codecs = @(Select-String -Path $all -Pattern $CodecPattern | Where-Object { & $defined $_ })
Write-Host "Defined symbols naming an H.264 or H.265 codec: $($codecs.Count)"
$symbols = Join-Path $work 'libwebrtc-symbols.txt'
$mentions = @(Select-String -Path $all -Pattern $MentionPattern | Where-Object { & $defined $_ } |
    ForEach-Object { ($_.Line -split '\|', 2)[-1].Trim() } | Sort-Object -Unique)
Set-Content -Path $symbols -Value $mentions
if ($codecs.Count -ne 0) {
    $codecs | Select-Object -First 50 | ForEach-Object { Write-Host $_.Line }
    throw 'The library still carries an H.264 or H.265 codec.'
}
Write-Host 'Other defined symbols that mention H.264, H.265 or FFmpeg (no codec):'
$mentions | Select-Object -First 100 | ForEach-Object { Write-Host $_ }
Remove-Item $all

# The archive, with fixed timestamps, forward slashes and a sorted file list, so
# that its digest depends only on what it holds.
$zipper = Join-Path $work 'zip.py'
@'
import os, sys, zipfile
src, top, dest = sys.argv[1:]
entries = []
for dirpath, dirnames, filenames in os.walk(os.path.join(src, top)):
    rel = os.path.relpath(dirpath, src).replace(os.sep, "/")
    entries.append(rel + "/")
    entries.extend(rel + "/" + f for f in filenames)
with zipfile.ZipFile(dest, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for name in sorted(entries):
        info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
        info.create_system = 3
        if name.endswith("/"):
            info.external_attr = (0o40755 << 16) | 0x10
            z.writestr(info, b"")
        else:
            info.external_attr = 0o100644 << 16
            info.compress_type = zipfile.ZIP_DEFLATED
            with open(os.path.join(src, name), "rb") as f:
                z.writestr(info, f.read())
'@ | Set-Content -NoNewline -Path $zipper
python -I $zipper $recipe $Unpacked (Join-Path $work $Archive)
Remove-Item $zipper
$hash = (Get-FileHash -Algorithm SHA256 (Join-Path $work $Archive)).Hash.ToLowerInvariant()
# sha256sum's format, with LF, so `sha256sum --check` reads it anywhere.
[IO.File]::WriteAllText((Join-Path $work "$Archive.sha256"), "$hash  $Archive`n")
Write-Host "$hash  $Archive"
Write-Output (Join-Path $work $Archive)
