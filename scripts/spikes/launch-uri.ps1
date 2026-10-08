# Opens a URI in one named package, as Launcher.LaunchUriAsync with
# TargetApplicationPackageFamilyName does: with both flavours installed, the
# shell would otherwise ask which app to use. Windows PowerShell 5.1, which can
# call WinRT directly (PowerShell 7 cannot).
param([Parameter(Mandatory)] [string] $Uri, [Parameter(Mandatory)] [string] $Family)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.System.Launcher, Windows.System, ContentType = WindowsRuntime]
$null = [Windows.System.LauncherOptions, Windows.System, ContentType = WindowsRuntime]
$asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
} | Select-Object -First 1
$options = [Windows.System.LauncherOptions]::new()
$options.TargetApplicationPackageFamilyName = $Family
$operation = [Windows.System.Launcher]::LaunchUriAsync([Uri]$Uri, $options)
$task = $asTask.MakeGenericMethod([bool]).Invoke($null, @($operation))
$null = $task.Wait(30000)
if (-not $task.Result) { throw "LaunchUriAsync($Uri) in $Family returned false" }
Write-Host "launched $Uri in $Family"
