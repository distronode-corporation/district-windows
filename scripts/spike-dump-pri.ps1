<#
.SYNOPSIS
SPIKE (do not merge): prints the resources.pri (a makepri dump) and the
generated AppxManifest.xml of the one DistrictAI_*.msix under -Packages.
#>
param(
    [Parameter(Mandatory)] [string] $Packages,
    [Parameter(Mandatory)] [string] $MakePri,
    [Parameter(Mandatory)] [string] $Label
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$msix = @(Get-ChildItem -Recurse $Packages -Filter 'DistrictAI_*.msix' | Where-Object { $_.FullName -notmatch '\\Dependencies\\' })
if ($msix.Count -ne 1) { throw "expected one DistrictAI MSIX under $Packages, found $($msix.Count)" }
$work = Join-Path ([IO.Path]::GetTempPath()) ("spike-pri-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null

$zip = [IO.Compression.ZipFile]::OpenRead($msix[0].FullName)
try {
    Write-Host "=== $Label`: $($msix[0].Name), the package's .pri and .resw entries"
    $zip.Entries | Where-Object { $_.FullName -match '\.(pri|resw)$' } | ForEach-Object { Write-Host "  $($_.FullName) ($($_.Length) bytes)" }
    [IO.Compression.ZipFileExtensions]::ExtractToFile($zip.GetEntry('resources.pri'), (Join-Path $work 'resources.pri'), $true)
    [IO.Compression.ZipFileExtensions]::ExtractToFile($zip.GetEntry('AppxManifest.xml'), (Join-Path $work 'AppxManifest.xml'), $true)
} finally { $zip.Dispose() }

$manifest = [xml](Get-Content -Raw (Join-Path $work 'AppxManifest.xml'))
Write-Host "=== $Label`: AppxManifest.xml as packaged"
Write-Host "  Properties/DisplayName: $($manifest.Package.Properties.DisplayName)"
Write-Host "  Resources: $(($manifest.Package.Resources.Resource | ForEach-Object { $_.Language }) -join ', ')"
$visual = $manifest.Package.Applications.Application.VisualElements
Write-Host "  VisualElements DisplayName: $($visual.DisplayName)"
Write-Host "  VisualElements Description: $($visual.Description)"

Push-Location $work
try {
    & $MakePri dump /if resources.pri /of dump.xml /dt Detailed /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "makepri dump exited $LASTEXITCODE" }
} finally { Pop-Location }
$dump = [xml](Get-Content -Raw (Join-Path $work 'dump.xml'))
Write-Host "=== $Label`: makepri dump, qualifiers"
$dump.SelectNodes('//QualifierSet//Qualifier') | ForEach-Object { "$($_.name)=$($_.value)" } | Sort-Object -Unique | ForEach-Object { Write-Host "  $_" }
Write-Host "=== $Label`: makepri dump, the Resources subtree (every candidate)"
foreach ($scope in $dump.SelectNodes("//ResourceMapSubtree[@name='Resources']")) {
    foreach ($named in $scope.SelectNodes('NamedResource')) {
        foreach ($candidate in $named.SelectNodes('Candidate')) {
            $qualifiers = ($candidate.SelectNodes('QualifierSet/Qualifier') | ForEach-Object { "$($_.name)=$($_.value)" }) -join ';'
            $value = $candidate.SelectSingleNode('Value')
            Write-Host "  $($named.name) [$qualifiers] $(if ($value) { $value.InnerText })"
        }
    }
}
Write-Host "=== $Label`: makepri dump, raw (first 200 lines)"
Get-Content (Join-Path $work 'dump.xml') -TotalCount 200 | ForEach-Object { Write-Host $_ }
Remove-Item -Recurse -Force $work
