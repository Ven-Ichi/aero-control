param([string]$AppDirectory = 'dist\AeroControl-0.3.0')
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework=Split-Path $compiler
$app=Join-Path $project "$AppDirectory\AeroControl.exe"
$references=@('System.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object {'/r:'+(Join-Path $framework $_)}
$output=Join-Path $project $AppDirectory
& $compiler /nologo /target:exe /out:"$output\UiSmokeTests.exe" /r:"$app" @references "$PSScriptRoot\UiSmokeTests.cs"
if($LASTEXITCODE -ne 0){throw 'UI test compilation failed'}
& "$output\UiSmokeTests.exe" "$project\test-results"
if($LASTEXITCODE -ne 0){throw 'UI tests failed'}
