[CmdletBinding()]
param([string]$AltiumSdkRoot)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
if(-not $AltiumSdkRoot) {
    $build=Get-ChildItem 'HKLM:\SOFTWARE\Altium\Builds' | ForEach-Object {Get-ItemProperty $_.PSPath} | Where-Object {$_.Installed -eq $true} | Sort-Object {[version]$_.FullBuild} -Descending | Select-Object -First 1
    if(-not $build) {throw 'A licensed developer SDK is required to build the probe.'}
    $application=[string]$build.Application
    if(-not $application.StartsWith('Altium\',[StringComparison]::OrdinalIgnoreCase)) {$application='Altium\'+$application}
    $AltiumSdkRoot=Join-Path $env:ProgramData ($application+'\Extensions\Altium Developer\SDK4.0')
}
$AltiumSdkRoot=[IO.Path]::GetFullPath($AltiumSdkRoot.TrimEnd('\','/'))
$output=(Join-Path $project 'bin\PermissionDiagnostics').Replace('\','/')+'/'
$intermediate=(Join-Path $project 'obj\PermissionDiagnostics').Replace('\','/')+'/'
$msbuild=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe'
& $msbuild (Join-Path $project 'AltiumRevisionRetirement.csproj') /t:Rebuild /p:Configuration=Release /p:PermissionDiagnostics=true "/p:AltiumSdkRoot=$AltiumSdkRoot" "/p:OutputPath=$output" "/p:IntermediateOutputPath=$intermediate" /nologo /verbosity:minimal
if($LASTEXITCODE -ne 0) {throw 'Read-only probe build failed.'}
$dll=Join-Path $output 'AltiumRevisionRetirement.dll'
if([Diagnostics.FileVersionInfo]::GetVersionInfo($dll).FileVersion -ne '0.1.9.9000') {throw 'The diagnostic DLL is not clearly versioned.'}
if([Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($dll)).Contains('AddLifeCycleStateChanges')) {throw 'Native mutation reference present in the diagnostic DLL.'}
$probeExe=Join-Path $output 'ProbeWriteGuardTest.exe'
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /warnaserror+ /target:exe "/out:$probeExe" "/reference:$dll" (Join-Path $project 'tests\ProbeWriteGuardTest.cs')
if($LASTEXITCODE -ne 0) {throw 'Probe write-guard test compilation failed.'}
& $probeExe
if($LASTEXITCODE -ne 0) {throw 'The read-only guard failed.'}
Write-Output ('Read-only diagnostic build: '+$dll)
