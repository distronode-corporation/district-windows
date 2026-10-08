# W2 spike 7: the website's /checkout and /login in WebView2 inside the app
# window: Cloudflare's challenge clears, Stripe.js loads, Elements renders,
# and the console stays clean. Nothing is typed or submitted, so no payment of
# any kind is attempted (production checkout runs on Stripe's live keys).
param([Parameter(Mandatory)] [string] $Packages)
Import-Module (Join-Path $PSScriptRoot 'SpikeHarness.psm1') -Force
Start-Summary 'Spike 7: checkout in WebView2'
$package = Install-TestPackage -Folder $Packages -Name 'Distronode.DistrictAI.Placeholder'
$family = $package.PackageFamilyName
Enable-CrashDumps
try {
    Start-App $family
    $null = Wait-SpikeLine -Family $family -Pattern 'started' -Seconds 60
    Start-Sleep -Seconds 3
    $mark = (Get-SpikeLog $family).Count
    Start-Process 'districtai://spike-checkout'
    $version = Wait-SpikeLine -Family $family -Pattern 'checkout-webview2' -After $mark -Seconds 60
    Add-Result 'WebView2 starts in the app window' ($null -ne $version) "$version"
    foreach ($page in @('/checkout', '/login')) {
        $result = Wait-SpikeLine -Family $family -Pattern "checkout-result page=https://www.distronode.com$page " -After $mark -Seconds 200
        $loaded = $result -match 'loaded=True'
        $clean = $result -match 'console_errors=0 '
        $what = if ($page -eq '/checkout') { 'challenge cleared, Stripe.js loaded, Elements rendered' } else { 'challenge cleared, page loaded' }
        Add-Result "$page`: $what" $loaded "$result"
        Add-Result "$page`: no console errors" ($loaded -and $clean) 'see checkout-console-error lines in the log'
    }
    Add-Result 'a Stripe test-mode payment completes' $false 'NOT ATTEMPTED: production /checkout uses the live publishable key and has no test mode; never pay live. Needs a test-mode deployment (or Sean on the dual-boot against one).'
    Stop-App
} catch {
    Add-Result 'the spike ran to its end' $false "$($_.Exception.Message)"
}
Show-CrashDumps
Complete-Summary $family
