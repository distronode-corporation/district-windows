<#
.SYNOPSIS
Installs a test MSIX, runs the UI smoke tests (tests/DistrictAI.UiTests) against
it, and uninstalls it again.

.DESCRIPTION
Used by CI (ci.yml, windows-app) on windows-2025, and by hand on Windows, from
PowerShell 7 run as administrator (trusting the test certificate needs it).

-Packages is a directory holding one DistrictAI_*.msix and the .cer of the
throwaway certificate that signed it (one or more copies), as CI's AppPackages
directory (and its district-ai-test-msix artifact) does; the framework packages
it depends on are installed from its Dependencies\x64 when there is one, and
stay installed (they are shared with other apps). The certificate is trusted
for the run only, and the package is always removed afterwards, pass or fail.
A copy of the package that was already installed is refused rather than
replaced.

Without -Scripted it runs the smoke tests (SmokeTests) against a package that
ships. With -Scripted, the package must be the scripted test package (built
with district-ffi's `scripted` feature, which the script checks), and it runs
the scene walk (SceneWalkTests). -Screenshots saves each page of the walk at
1920x1080 (its client area) in that folder, setting the display larger
first, and the whole window once (00-window.png).

.EXAMPLE
pwsh scripts/run-ui-tests.ps1 -Packages $env:USERPROFILE\Downloads\district-ai-test-msix

.EXAMPLE
pwsh scripts/run-ui-tests.ps1 -Packages .\district-ai-scripted-msix -Scripted -Screenshots .\shots
#>
param(
    [Parameter(Mandatory)] [string] $Packages,
    [switch] $Scripted,
    [string] $Screenshots = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Packages = (Resolve-Path $Packages).Path
$project = Join-Path $PSScriptRoot '..' 'tests' 'DistrictAI.UiTests'

$msix = @(Get-ChildItem -Recurse $Packages -Filter 'DistrictAI_*.msix' | Where-Object { $_.FullName -notmatch '\\Dependencies\\' })
if ($msix.Count -ne 1) { throw "expected one DistrictAI MSIX under $Packages, found $($msix.Count)" }
$msix = $msix[0]
# The packaging writes the certificate next to the package as well as CI's
# District-AI-CI-test.cer: any number of copies, so long as they are one certificate.
$cer = @(Get-ChildItem -Recurse $Packages -Filter '*.cer')
$thumbprints = @($cer | ForEach-Object { [Security.Cryptography.X509Certificates.X509Certificate2]::new($_.FullName).Thumbprint } | Sort-Object -Unique)
if ($thumbprints.Count -ne 1) { throw "expected one certificate under $Packages, found $($thumbprints.Count) in $($cer.Count) .cer file(s)" }

# The identity the package installs under, from its own manifest.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($msix.FullName)
try {
    $reader = [IO.StreamReader]::new($zip.GetEntry('AppxManifest.xml').Open())
    $name = ([xml]$reader.ReadToEnd()).Package.Identity.Name
    $reader.Dispose()
} finally { $zip.Dispose() }
# The scene walk needs a scripted package; the smoke tests one that ships.
python (Join-Path $PSScriptRoot 'check-scripted.py') $(if ($Scripted) { '--present' } else { '--absent' }) $msix.FullName
if ($LASTEXITCODE -ne 0) { throw "$($msix.Name) is $(if ($Scripted) { 'not ' })a scripted build" }
if ($Screenshots -and -not $Scripted) { throw '-Screenshots needs -Scripted' }

if (Get-AppxPackage -Name $name) { throw "$name is already installed for this user: uninstall it first, so the tests run against $($msix.Name)" }

$dependencies = Join-Path $msix.DirectoryName 'Dependencies\x64'
if (Test-Path $dependencies) {
    Get-ChildItem -Path $dependencies -Filter '*.msix' -File | ForEach-Object {
        Write-Host "installing the framework package $($_.Name)"
        try { Add-AppxPackage -Path $_.FullName } catch { Write-Host "  not installed (a newer version may already be present): $($_.Exception.Message)" }
    }
}

# A crash of DistrictAI.exe leaves a full dump, which is read below if the tests fail.
$dumps = Join-Path ([IO.Path]::GetTempPath()) 'district-ui-test-dumps'
$werKey = 'HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\DistrictAI.exe'
New-Item -ItemType Directory -Force -Path $dumps | Out-Null
Get-ChildItem $dumps -Filter '*.dmp' | Remove-Item
New-Item -Force -Path $werKey | Out-Null
Set-ItemProperty -Path $werKey -Name DumpFolder -Value $dumps -Type ExpandString
Set-ItemProperty -Path $werKey -Name DumpType -Value 2 -Type DWord

$trusted = Import-Certificate -FilePath $cer[0].FullName -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople'
$failed = $true
try {
    Add-AppxPackage -Path $msix.FullName
    $package = Get-AppxPackage -Name $name
    if (-not $package) { throw "$name did not install" }
    Write-Host "installed $($package.PackageFullName)"

    $env:DISTRICTAI_PACKAGE_FAMILY = $package.PackageFamilyName
    $env:DISTRICTAI_UI_TESTS_REQUIRED = '1'
    if ($Scripted) {
        $env:DISTRICTAI_UI_SCRIPTED = '1'
        $filter = 'FullyQualifiedName~DistrictAI.UiTests.SceneWalkTests'
    } else {
        $filter = 'FullyQualifiedName~DistrictAI.UiTests.SmokeTests'
    }
    if ($Screenshots) {
        $env:DISTRICTAI_SCREENSHOTS = [IO.Path]::GetFullPath($Screenshots)
        # The pages are saved at a 1920x1080 client area, so the window, with
        # its title bar and frame, needs a larger display. Windows Server's own
        # cmdlet; the walk checks the client area really is 1920x1080.
        foreach ($mode in @(@(2560, 1440), @(1920, 1200))) {
            try { Set-DisplayResolution -Width $mode[0] -Height $mode[1] -Force; break } catch { Write-Host "Set-DisplayResolution $($mode -join 'x'): $($_.Exception.Message)" }
        }
        Add-Type -AssemblyName System.Windows.Forms
        Write-Host "display: $([System.Windows.Forms.Screen]::PrimaryScreen.Bounds)"
    }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    dotnet test $project --nologo --logger 'console;verbosity=detailed' --filter $filter
    $code = $LASTEXITCODE
    Write-Host ("UI tests ran in {0:N1} s (build included)" -f $clock.Elapsed.TotalSeconds)
    if ($code -ne 0) { throw "the UI tests failed ($code)" }
    $failed = $false
} finally {
    foreach ($variable in 'DISTRICTAI_PACKAGE_FAMILY', 'DISTRICTAI_UI_TESTS_REQUIRED', 'DISTRICTAI_UI_SCRIPTED', 'DISTRICTAI_SCREENSHOTS') {
        Remove-Item -Path "Env:$variable" -ErrorAction SilentlyContinue
    }
    Get-Process -Name 'DistrictAI' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    if ($failed) {
        # Why the app stopped, if it crashed: Windows Error Reporting's record.
        Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = 'Application Error'; StartTime = (Get-Date).AddHours(-1) } -ErrorAction SilentlyContinue |
            Where-Object { $_.Message -match 'DistrictAI' } |
            ForEach-Object { Write-Host "--- crash $($_.TimeCreated)`n$($_.Message)" }
        # What the debugger makes of the newest dump: a XAML fail-fast's
        # stowed exception (its HRESULT and message) included.
        $cdb = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\Debuggers\x64\cdb.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
        $dump = Get-ChildItem $dumps -Filter '*.dmp' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($dump -and $cdb) {
            Write-Host "--- $($dump.Name) ($($dump.Length) bytes)"
            & $cdb.FullName -z $dump.FullName -c '.symfix; .reload; !analyze -v; !pde.dse; q' 2>&1 |
                Select-String -Pattern 'STOWED|Stowed|HRESULT|ERROR_CODE|EXCEPTION_|FAILURE_|SYMBOL_NAME|Message|Exception|STACK_TEXT|DistrictAI|Microsoft_UI_Xaml|!' |
                Select-Object -First 150 | ForEach-Object { Write-Host $_.Line }
        } elseif ($dump) {
            Write-Host "--- $($dump.Name): no cdb.exe to read it"
        }
    }
    Remove-Item -Path $werKey -ErrorAction SilentlyContinue
    Get-AppxPackage -Name $name | ForEach-Object {
        Remove-AppxPackage -Package $_.PackageFullName
        Write-Host "removed $($_.PackageFullName)"
    }
    Remove-Item -Path "Cert:\LocalMachine\TrustedPeople\$($trusted.Thumbprint)"
}
