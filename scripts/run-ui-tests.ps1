<#
.SYNOPSIS
Installs a test MSIX, runs the UI smoke tests (tests/DistrictAI.UiTests) against
it, and uninstalls it again.

.DESCRIPTION
Used by CI (ci.yml, windows-app) on windows-2025, and by hand on Windows, from
PowerShell 7 run as administrator (trusting the test certificate needs it).

-Packages is a directory holding one DistrictAI_*.msix and the .cer of the
throwaway certificate that signed it, as CI's AppPackages directory (and its
district-ai-test-msix artifact) does; the framework packages it depends on are
installed from its Dependencies\x64 when there is one, and stay installed (they
are shared with other apps). The certificate is trusted for the run only, and
the package is always removed afterwards, pass or fail. A copy of the package
that was already installed is refused rather than replaced.

.EXAMPLE
pwsh scripts/run-ui-tests.ps1 -Packages $env:USERPROFILE\Downloads\district-ai-test-msix
#>
param(
    [Parameter(Mandatory)] [string] $Packages
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Packages = (Resolve-Path $Packages).Path
$project = Join-Path $PSScriptRoot '..' 'tests' 'DistrictAI.UiTests'

$msix = @(Get-ChildItem -Recurse $Packages -Filter 'DistrictAI_*.msix' | Where-Object { $_.FullName -notmatch '\\Dependencies\\' })
if ($msix.Count -ne 1) { throw "expected one DistrictAI MSIX under $Packages, found $($msix.Count)" }
$msix = $msix[0]
$cer = @(Get-ChildItem -Recurse $Packages -Filter '*.cer')
if ($cer.Count -ne 1) { throw "expected one .cer under $Packages, found $($cer.Count)" }

# The identity the package installs under, from its own manifest.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($msix.FullName)
try {
    $reader = [IO.StreamReader]::new($zip.GetEntry('AppxManifest.xml').Open())
    $name = ([xml]$reader.ReadToEnd()).Package.Identity.Name
    $reader.Dispose()
} finally { $zip.Dispose() }
if (Get-AppxPackage -Name $name) { throw "$name is already installed for this user: uninstall it first, so the tests run against $($msix.Name)" }

$dependencies = Join-Path $msix.DirectoryName 'Dependencies\x64'
if (Test-Path $dependencies) {
    Get-ChildItem -Path $dependencies -Filter '*.msix' -File | ForEach-Object {
        Write-Host "installing the framework package $($_.Name)"
        try { Add-AppxPackage -Path $_.FullName } catch { Write-Host "  not installed (a newer version may already be present): $($_.Exception.Message)" }
    }
}

$trusted = Import-Certificate -FilePath $cer[0].FullName -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople'
$failed = $true
try {
    Add-AppxPackage -Path $msix.FullName
    $package = Get-AppxPackage -Name $name
    if (-not $package) { throw "$name did not install" }
    Write-Host "installed $($package.PackageFullName)"

    $env:DISTRICTAI_PACKAGE_FAMILY = $package.PackageFamilyName
    $env:DISTRICTAI_UI_TESTS_REQUIRED = '1'
    $clock = [Diagnostics.Stopwatch]::StartNew()
    dotnet test $project --nologo --logger 'console;verbosity=detailed'
    $code = $LASTEXITCODE
    Write-Host ("UI tests ran in {0:N1} s (build included)" -f $clock.Elapsed.TotalSeconds)
    if ($code -ne 0) { throw "the UI tests failed ($code)" }
    $failed = $false
} finally {
    Get-Process -Name 'DistrictAI' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    if ($failed) {
        # Why the app stopped, if it crashed: Windows Error Reporting's record.
        Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = 'Application Error'; StartTime = (Get-Date).AddHours(-1) } -ErrorAction SilentlyContinue |
            Where-Object { $_.Message -match 'DistrictAI' } |
            ForEach-Object { Write-Host "--- crash $($_.TimeCreated)`n$($_.Message)" }
    }
    Get-AppxPackage -Name $name | ForEach-Object {
        Remove-AppxPackage -Package $_.PackageFullName
        Write-Host "removed $($_.PackageFullName)"
    }
    Remove-Item -Path "Cert:\LocalMachine\TrustedPeople\$($trusted.Thumbprint)"
}
