# Presses a button on a toast on screen through UI Automation. Windows
# PowerShell 5.1, which has the UI Automation client assemblies. Prints what it
# did; never fails the job by itself.
param([Parameter(Mandatory)] [string] $Button)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$root = [Windows.Automation.AutomationElement]::RootElement
$deadline = (Get-Date).AddSeconds(10)
while ((Get-Date) -lt $deadline) {
    $condition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, $Button)
    $found = $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
    if ($found) {
        $invoke = $found.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)
        $invoke.Invoke()
        Write-Output "pressed '$Button'"
        exit 0
    }
    Start-Sleep -Milliseconds 500
}
Write-Output "no '$Button' button on screen"
