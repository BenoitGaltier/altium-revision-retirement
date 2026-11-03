param([string]$PreviewPath)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$output=Join-Path $project '.artifacts\tests'
New-Item -ItemType Directory -Path $output -Force | Out-Null
if(-not $PreviewPath) {$PreviewPath=Join-Path $output 'ui-preview.png'}
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$logo=Join-Path $project 'assets\fluidra-logo.png'
& $csc /nologo /warnaserror+ /langversion:5 /target:exe "/out:$output\UiSmokeTests.exe" /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "/resource:$logo,AltiumRevisionRetirement.FluidraLogo.png" (Join-Path $project 'src\RetirementEngine.cs') (Join-Path $project 'src\RetirementForm.cs') (Join-Path $project 'src\AuditJournal.cs') (Join-Path $project 'src\ScanDiagnostics.cs') (Join-Path $project 'tests\UiSmokeTests.cs')
if($LASTEXITCODE -ne 0) {throw 'UI test compilation failed.'}
& (Join-Path $output 'UiSmokeTests.exe') $PreviewPath
if($LASTEXITCODE -ne 0) {throw 'UI tests failed.'}
