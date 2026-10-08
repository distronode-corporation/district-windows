# W2 spike 3: with the packaged app running, districtai://auth and
# districtai://handoff each start a second process that hands the activation to
# the first and exits in under a second; the first receives the link.
param([Parameter(Mandatory)] [string] $Packages)
Import-Module (Join-Path $PSScriptRoot 'SpikeHarness.psm1') -Force
Start-Summary 'Spike 3: protocol activation redirect'
$package = Install-TestPackage -Folder $Packages -Name 'Distronode.DistrictAI.Placeholder'
$family = $package.PackageFamilyName
try {

    Start-App $family
    $started = Wait-SpikeLine -Family $family -Pattern 'started' -Seconds 60
    Add-Result 'first instance starts' ($null -ne $started) "$started"
    Start-Sleep -Seconds 3
    $first = if ($started -match 'pid=(\d+)') { $Matches[1] } else { '' }

    foreach ($link in @('districtai://auth?code=spike-code&state=spike-state', 'districtai://handoff?n=spike')) {
        $kind = if ($link -like '*://auth*') { 'Auth' } else { 'Handoff' }
        $mark = (Get-SpikeLog $family).Count
        $clock = [Diagnostics.Stopwatch]::StartNew()
        Start-Process $link
        $redirected = Wait-SpikeLine -Family $family -Pattern 'redirected kind=Protocol' -After $mark -Seconds 20
        $received = Wait-SpikeLine -Family $family -Pattern "pid=$first received link=$kind" -After $mark -Seconds 20
        $roundTrip = $clock.ElapsedMilliseconds
        $uptime = if ($redirected -match 'uptime_ms=(\d+)') { [int]$Matches[1] } else { -1 }
        Start-Sleep -Seconds 2
        $alive = (Get-AppProcesses).Count
        Add-Result "$kind link: second process exits in under 1 s" ($uptime -ge 0 -and $uptime -lt 1000) "second process ran $uptime ms (start to redirect done); $redirected"
        Add-Result "$kind link: the first instance receives it" ($null -ne $received) "$received; link to log line $roundTrip ms"
        Add-Result "$kind link: one process left" ($alive -eq 1) "$alive DistrictAI process(es) 2 s later"
        if ($kind -eq 'Auth') {
            $handled = Wait-SpikeLine -Family $family -Pattern 'auth-handled' -After $mark -Seconds 10
            Add-Result 'Auth link: the core checks the answer' ($null -ne $handled) "$handled (no sign-in was started, so the core refuses the answer; the exchange itself needs the service: checklist 1)"
        }
    }
    Stop-App
} catch {
    Add-Result 'the spike ran to its end' $false "$($_.Exception.Message)"
}
Complete-Summary $family
