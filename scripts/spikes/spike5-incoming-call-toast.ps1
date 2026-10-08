# W2 spike 5: the IncomingCall toast, and its Answer and Decline buttons both
# while the app runs and on a cold COM activation.
param([Parameter(Mandatory)] [string] $Packages, [Parameter(Mandatory)] [string] $Manifest)
Import-Module (Join-Path $PSScriptRoot 'SpikeHarness.psm1') -Force
Start-Summary 'Spike 5: IncomingCall toast'
$package = Install-TestPackage -Folder $Packages -Name 'Distronode.DistrictAI.Placeholder'
$family = $package.PackageFamilyName
$clsid = Get-ActivatorClsid $Manifest

Start-App $family
$started = Wait-SpikeLine -Family $family -Pattern 'started' -Seconds 60
Start-Sleep -Seconds 3
$first = if ($started -match 'pid=(\d+)') { $Matches[1] } else { '' }

$mark = (Get-SpikeLog $family).Count
Start-Process 'districtai://spike-toast'
$shown = Wait-SpikeLine -Family $family -Pattern 'toast-shown' -After $mark -Seconds 20
Add-Result 'the IncomingCall toast is shown' ($shown -match 'listed=True') "$shown"

# Pressing Answer on screen, through UI Automation, where the runner's session
# shows toasts at all; the COM call below proves the same path either way.
$mark = (Get-SpikeLog $family).Count
$clicked = & powershell.exe -NoProfile -File (Join-Path $PSScriptRoot 'click-toast-button.ps1') -Button 'Answer' 2>&1
Write-Host "UI Automation: $clicked"
$answered = Wait-SpikeLine -Family $family -Pattern "pid=$first toast-activated cold=false args=action=answer;call=spike" -After $mark -Seconds 5
$how = 'pressed on screen through UI Automation'
if (-not $answered) {
    $how = 'delivered through the COM activator (the on-screen button was not reachable)'
    Invoke-ToastActivator -Clsid $clsid -Family $family -Arguments 'action=answer;call=spike'
    $answered = Wait-SpikeLine -Family $family -Pattern "pid=$first toast-activated cold=false args=action=answer;call=spike" -After $mark -Seconds 20
}
Add-Result 'Answer reaches the running app' ($null -ne $answered) "$how; $answered"

Stop-App
$mark = (Get-SpikeLog $family).Count
$clock = [Diagnostics.Stopwatch]::StartNew()
Invoke-ToastActivator -Clsid $clsid -Family $family -Arguments 'action=decline;call=spike'
$cold = Wait-SpikeLine -Family $family -Pattern 'toast-activated cold=true args=action=decline;call=spike' -After $mark -Seconds 60
Add-Result 'Decline starts the app (cold COM activation)' ($null -ne $cold -and $cold -notmatch "pid=$first ") "$cold; $($clock.ElapsedMilliseconds) ms from the COM call"
Stop-App
Complete-Summary $family
