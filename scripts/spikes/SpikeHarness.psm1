# Shared steps for the W2 spike jobs in .github/workflows/spikes.yml: install
# a CI-built test package, start the app, read the spike build's log
# (LocalState\spikes.log, written by src/DistrictAI/Spikes/SpikeLog.cs), call
# the notification activator as Windows does, and record results.
#
# Runs in PowerShell 7 on windows-2025. Nothing here is used by the app.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# INotificationActivationCallback: what Windows calls on a toast's COM class
# when a button is pressed. Calling it here is the same activation, from the
# same out-of-process COM registration the package declares.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

[ComImport, Guid("53E31837-6600-4A81-9395-75CFFE746F94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface INotificationActivationCallback
{
    void Activate([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
                  [MarshalAs(UnmanagedType.LPWStr)] string invokedArgs,
                  IntPtr data, uint count);
}

public static class ToastActivator
{
    public static void Activate(Guid clsid, string aumid, string arguments)
    {
        var type = Type.GetTypeFromCLSID(clsid, true);
        var callback = (INotificationActivationCallback)Activator.CreateInstance(type);
        try { callback.Activate(aumid, arguments, IntPtr.Zero, 0); }
        finally { Marshal.ReleaseComObject(callback); }
    }
}
'@

function Install-TestPackage {
    <# Trusts the package's test certificate, installs its framework
       dependencies and the package, and returns the installed package. #>
    param([Parameter(Mandatory)] [string] $Folder, [Parameter(Mandatory)] [string] $Name)
    $cer = Get-ChildItem -Recurse $Folder -Filter '*.cer' | Select-Object -First 1
    Import-Certificate -FilePath $cer.FullName -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
    foreach ($dependency in Get-ChildItem -Recurse $Folder -Include '*.msix', '*.appx' | Where-Object { $_.FullName -match '\\Dependencies\\x64\\' }) {
        try { Add-AppxPackage -Path $dependency.FullName } catch { Write-Host "dependency $($dependency.Name): $($_.Exception.Message)" }
    }
    $msix = Get-ChildItem -Recurse $Folder -Filter 'DistrictAI_*.msix' | Where-Object { $_.FullName -notmatch '\\Dependencies\\' } | Select-Object -First 1
    Add-AppxPackage -Path $msix.FullName
    $package = Get-AppxPackage -Name $Name
    if (-not $package) { throw "package $Name did not install" }
    Write-Host "installed $($package.PackageFullName)"
    $package
}

function Get-LogPath([string] $Family) {
    Join-Path $env:LOCALAPPDATA "Packages\$Family\LocalState\spikes.log"
}

function Get-SpikeLog([string] $Family) {
    <# Always an array, even of none or one line (so .Count works under StrictMode). #>
    $path = Get-LogPath $Family
    $lines = if (Test-Path $path) { @(Get-Content -LiteralPath $path) } else { @() }
    Write-Output -NoEnumerate $lines
}

function Wait-SpikeLine {
    <# The first log line after line $After that matches $Pattern, or $null at the timeout. #>
    param([string] $Family, [string] $Pattern, [int] $After = 0, [int] $Seconds = 30)
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        $lines = Get-SpikeLog $Family
        for ($i = $After; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match $Pattern) { return $lines[$i] }
        }
        Start-Sleep -Milliseconds 100
    }
    $null
}

function Enable-CrashDumps {
    <# Windows Error Reporting writes a full dump of DistrictAI.exe if it crashes. #>
    $key = 'HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\DistrictAI.exe'
    $folder = Join-Path $env:RUNNER_TEMP 'dumps'
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    New-Item -Force -Path $key | Out-Null
    Set-ItemProperty -Path $key -Name DumpFolder -Value $folder -Type ExpandString
    Set-ItemProperty -Path $key -Name DumpType -Value 2 -Type DWord
}

function Show-CrashDumps {
    <# What the debugger makes of any dump: the stowed exception behind a XAML fail-fast included. #>
    $cdb = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    foreach ($dump in Get-ChildItem (Join-Path $env:RUNNER_TEMP 'dumps') -Filter '*.dmp' -ErrorAction SilentlyContinue) {
        Write-Host "--- dump $($dump.Name) ($($dump.Length) bytes)"
        if ($cdb) {
            & $cdb.FullName -z $dump.FullName -c '.symfix; .reload; !analyze -v; .exr -1; !error @$ea; q' 2>&1 |
                Select-String -Pattern 'STOWED|Stowed|HRESULT|EXCEPTION_|ERROR_CODE|FAILURE_|SYMBOL_NAME|Error code|Exception|WebView|Message' |
                Select-Object -First 80 | ForEach-Object { Write-Host $_.Line }
        } else {
            Write-Host 'no cdb.exe on this runner'
        }
    }
}

function Start-App([string] $Family) {
    Start-Process -FilePath 'explorer.exe' -ArgumentList "shell:AppsFolder\$Family!App"
}

function Get-AppProcesses {
    Write-Output -NoEnumerate @(Get-Process -Name 'DistrictAI' -ErrorAction SilentlyContinue)
}

function Stop-App {
    $running = Get-AppProcesses
    foreach ($process in $running) { Stop-Process -InputObject $process -Force -ErrorAction SilentlyContinue }
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-AppProcesses).Count -gt 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
}

function Get-ActivatorClsid([string] $Manifest) {
    ([xml](Get-Content -Raw $Manifest)).SelectSingleNode("//*[local-name()='ToastNotificationActivation']").ToastActivatorCLSID
}

function Invoke-ToastActivator([string] $Clsid, [string] $Family, [string] $Arguments) {
    <# Calls the activator; returns $null, or the error it raised. #>
    try {
        [ToastActivator]::Activate([Guid]$Clsid, "$Family!App", $Arguments)
        $null
    } catch {
        $_.Exception.InnerException?.Message ?? $_.Exception.Message
    }
}

function Add-Result {
    <# One row of the job summary, and the job's verdict. #>
    param([string] $Check, [bool] $Passed, [string] $Detail)
    $verdict = if ($Passed) { 'pass' } else { 'FAIL' }
    $row = "| $Check | $verdict | $($Detail -replace '\|', '/') |"
    Write-Host $row
    if ($env:GITHUB_STEP_SUMMARY) { Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $row }
    if (-not $Passed) { $script:Failed = $true }
}

function Start-Summary([string] $Title) {
    $script:Failed = $false
    if ($env:GITHUB_STEP_SUMMARY) {
        Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value "### $Title`n`n| Check | Result | Detail |`n|---|---|---|"
    }
}

function Complete-Summary([string] $Family) {
    if ($Family) {
        Write-Host "--- spikes.log"
        $lines = Get-SpikeLog $Family
        foreach ($line in $lines) { Write-Host $line }
    }
    # Why the app stopped, if it crashed: Windows Error Reporting's record.
    $crashes = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = 'Application Error'; StartTime = (Get-Date).AddHours(-1) } -ErrorAction SilentlyContinue |
        Where-Object { $_.Message -match 'DistrictAI' }
    foreach ($crash in $crashes) { Write-Host "--- crash $($crash.TimeCreated)`n$($crash.Message)" }
    if ($script:Failed) { throw 'one or more checks failed' }
}

Export-ModuleMember -Function *
