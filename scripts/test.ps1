$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$output=Join-Path $project '.artifacts\tests'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /warnaserror+ /langversion:5 /target:exe "/out:$output\SafetyTests.exe" /r:System.Core.dll (Join-Path $project 'src\RetirementEngine.cs') (Join-Path $project 'src\FolderInventory.cs') (Join-Path $project 'tests\SafetyTests.cs') (Join-Path $project 'tests\FolderInventoryTests.cs')
if($LASTEXITCODE -ne 0) {throw 'Test compilation failed.'}
& (Join-Path $output 'SafetyTests.exe')
if($LASTEXITCODE -ne 0) {throw 'Safety tests failed.'}
