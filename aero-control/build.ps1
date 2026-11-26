param([switch]$Test, [string]$OutputDirectory = 'dist\AeroControl-0.3.0')
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Split-Path $compiler
$output = Join-Path $PSScriptRoot $OutputDirectory
New-Item -ItemType Directory -Path $output -Force | Out-Null
if ($Test) {
    & $compiler /nologo /target:exe /out:"$output\ContractTests.exe" "$PSScriptRoot\Models.cs" "$PSScriptRoot\tests\ContractTests.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
    & "$output\ContractTests.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Contract tests failed' }
    $powerSources = @("$PSScriptRoot\Models.cs", "$PSScriptRoot\WindowsPower.cs", "$PSScriptRoot\tests\PowerTests.cs")
    & $compiler /nologo /target:exe /out:"$output\PowerTests.exe" @powerSources
    if ($LASTEXITCODE -ne 0) { throw 'Power test compilation failed' }
    & "$output\PowerTests.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Power tests failed' }
}
$references = @('System.dll','System.Core.dll','System.Xml.Linq.dll','System.Drawing.dll','System.Xaml.dll','System.Management.dll','System.ServiceProcess.dll','System.Windows.Forms.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object { '/r:' + (Join-Path $framework $_) }
$sources = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName
$resource = '/resource:' + (Join-Path $PSScriptRoot 'MainWindow.xaml') + ',AeroControl.MainWindow.xaml'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /out:"$output\AeroControl.exe" $resource @references @sources
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed' }
$repoRoot = Split-Path $PSScriptRoot -Parent
if (Test-Path "$repoRoot\README.md") { Copy-Item "$repoRoot\README.md" $output -Force }
elseif (Test-Path "$PSScriptRoot\README.md") { Copy-Item "$PSScriptRoot\README.md" $output -Force }
if (Test-Path "$repoRoot\LICENSE") { Copy-Item "$repoRoot\LICENSE" $output -Force }
elseif (Test-Path "$PSScriptRoot\LICENSE") { Copy-Item "$PSScriptRoot\LICENSE" $output -Force }
Write-Output "Built $output\AeroControl.exe"
if ($Test) { & "$PSScriptRoot\tests\Test-Ui.ps1" -AppDirectory $OutputDirectory }
