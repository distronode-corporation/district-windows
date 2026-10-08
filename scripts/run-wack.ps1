<#
.SYNOPSIS
Runs the Windows App Certification Kit against an MSIX and fails when the package fails.

.DESCRIPTION
Used by CI (ci.yml, windows-app) and by the release workflow (release.yml), on
windows-2025, which runs as an administrator, as appcert.exe requires.

appcert.exe is part of the Windows SDK, and its directory differs between SDK
versions, so it is looked for under the Windows Kits directories rather than
at one path. The kit installs the package to test it, so the package's
framework dependencies (the Windows App Runtime) are installed first and the
test certificate's public half is trusted for the run, then removed again.
The report is judged by scripts/check-wack-report.py: OVERALL_RESULT FAIL
fails, and every failed test is printed.

.EXAMPLE
pwsh scripts/run-wack.ps1 -Package out\DistrictAI_1.0.0.0_x64.msix -Certificate out\test.cer -Report out\wack.xml
#>
param(
    [Parameter(Mandatory)] [string] $Package,
    [Parameter(Mandatory)] [string] $Certificate,
    [Parameter(Mandatory)] [string] $Report,
    # A directory of framework packages (the build's Dependencies\x64), installed first.
    [string] $Dependencies = ''
)

$ErrorActionPreference = 'Stop'
$Package = (Resolve-Path $Package).Path
$Certificate = (Resolve-Path $Certificate).Path
$Report = [IO.Path]::GetFullPath($Report)
if (Test-Path $Report) { Remove-Item $Report }

$kits = @(${env:ProgramFiles(x86)}, $env:ProgramFiles) |
    Where-Object { $_ } |
    ForEach-Object { Join-Path $_ 'Windows Kits' } |
    Where-Object { Test-Path $_ }
$found = @($kits | ForEach-Object { Get-ChildItem -Path $_ -Recurse -Filter appcert.exe -File -ErrorAction SilentlyContinue })
if ($found.Count -eq 0) { throw "appcert.exe is not under any Windows Kits directory ($($kits -join ', ')): the Windows App Certification Kit is not installed" }
$found | ForEach-Object { Write-Host "found $($_.FullName) $($_.VersionInfo.FileVersion)" }
# The newest kit, and the 64-bit one where a directory carries both.
$byVersion = @{ Expression = { try { [version]($_.VersionInfo.FileVersion -replace '[^0-9.].*$', '') } catch { [version]'0.0' } } }
$by64 = @{ Expression = { $_.FullName -match 'x64' } }
$appcert = ($found | Sort-Object -Property $byVersion, $by64 -Descending | Select-Object -First 1).FullName
Write-Host "using $appcert"

if ($Dependencies) {
    Get-ChildItem -Path $Dependencies -Filter '*.msix' -File | ForEach-Object {
        Write-Host "installing the framework package $($_.Name)"
        try { Add-AppxPackage -Path $_.FullName } catch { Write-Host "  not installed (a newer version may already be present): $($_.Exception.Message)" }
    }
}

$trusted = Import-Certificate -FilePath $Certificate -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople'
try {
    & $appcert reset
    if ($LASTEXITCODE -ne 0) { throw "appcert.exe reset failed with $LASTEXITCODE" }
    & $appcert test -appxpackagepath $Package -reportoutputpath $Report
    Write-Host "appcert.exe test exited with $LASTEXITCODE"
} finally {
    Remove-Item -Path "Cert:\LocalMachine\TrustedPeople\$($trusted.Thumbprint)"
}

if (-not (Test-Path $Report)) { throw "appcert.exe wrote no report at $Report" }
python (Join-Path $PSScriptRoot 'check-wack-report.py') $Report
if ($LASTEXITCODE -ne 0) { throw "the package failed the Windows App Certification Kit (report: $Report)" }
