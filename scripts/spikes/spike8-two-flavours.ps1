# W2 spike 8: the Store and GitHub flavours installed side by side. Each finds
# the other through FindUriSchemeHandlersAsync("districtai"), keeps its
# credentials under its own package family, and a districtai://auth answer
# that reaches the copy which started no sign-in is dropped with the notice.
param([Parameter(Mandatory)] [string] $StorePackages, [Parameter(Mandatory)] [string] $GitHubPackages)
Import-Module (Join-Path $PSScriptRoot 'SpikeHarness.psm1') -Force
Start-Summary 'Spike 8: two flavours side by side'
$store = Install-TestPackage -Folder $StorePackages -Name 'Distronode.DistrictAI.Placeholder'
$github = Install-TestPackage -Folder $GitHubPackages -Name 'Distronode.DistrictAI.GitHub'
Add-Result 'both flavours install together' ($store.PackageFamilyName -ne $github.PackageFamilyName) "$($store.PackageFamilyName) and $($github.PackageFamilyName)"

foreach ($pair in @(@($store, $github), @($github, $store))) {
    $mine = $pair[0].PackageFamilyName
    $other = $pair[1].PackageFamilyName
    Start-App $mine
    $line = Wait-SpikeLine -Family $mine -Pattern 'other-copy found=' -Seconds 60
    Add-Result "$mine detects the other copy" ($line -match [regex]::Escape($other)) "$line"
    Add-Result "$mine keeps credentials under its own family" ($line -match "credentials=$([regex]::Escape($mine))$") "$line"
}
Start-Sleep -Seconds 2

$mark = (Get-SpikeLog $github.PackageFamilyName).Count
& powershell.exe -NoProfile -File (Join-Path $PSScriptRoot 'launch-uri.ps1') -Uri 'districtai://auth?code=spike&state=spike' -Family $github.PackageFamilyName
$dropped = Wait-SpikeLine -Family $github.PackageFamilyName -Pattern 'received link=Auth dropped=wrong-copy' -After $mark -Seconds 30
Add-Result 'an auth answer in the copy that started no sign-in is dropped with the notice' ($null -ne $dropped) "$dropped"

$mark = (Get-SpikeLog $store.PackageFamilyName).Count
& powershell.exe -NoProfile -File (Join-Path $PSScriptRoot 'launch-uri.ps1') -Uri 'districtai://handoff?n=spike' -Family $store.PackageFamilyName
$handoff = Wait-SpikeLine -Family $store.PackageFamilyName -Pattern 'received link=Handoff' -After $mark -Seconds 30
Add-Result 'a hand-off works in either copy' ($null -ne $handoff) "$handoff"

$targets = @(cmdkey /list | Select-String 'DistrictAI/' | ForEach-Object { $_.Line.Trim() })
Add-Result 'no credential outside a package family namespace' (-not ($targets | Where-Object { $_ -notmatch 'DistrictAI/(' + [regex]::Escape($store.PackageFamilyName) + '|' + [regex]::Escape($github.PackageFamilyName) + ')/' })) "Credential Manager targets: $($targets -join ', ')"
Stop-App
Write-Host '--- GitHub flavour log'
Get-SpikeLog $github.PackageFamilyName | ForEach-Object { Write-Host $_ }
Complete-Summary $store.PackageFamilyName
