[CmdletBinding()]
param([string]$AltiumSdkRoot)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
if(-not $AltiumSdkRoot) {
    $build=Get-ChildItem 'HKLM:\SOFTWARE\Altium\Builds' | ForEach-Object {Get-ItemProperty $_.PSPath} | Where-Object {$_.Installed -eq $true} | Sort-Object {[version]$_.FullBuild} -Descending | Select-Object -First 1
    if(-not $build) {throw 'Altium Designer installation not found.'}
    $application=[string]$build.Application
    if(-not $application.StartsWith('Altium\',[StringComparison]::OrdinalIgnoreCase)) {$application="Altium\$application"}
    $AltiumSdkRoot=Join-Path $env:ProgramData "$application\Extensions\Altium Developer\SDK4.0"
}
foreach($file in @('CSharp\Altium.SDK.dll','..\Altium.SDK.Interfaces.dll','SDK.ticket')) {
    if(-not (Test-Path -LiteralPath (Join-Path $AltiumSdkRoot $file))) {throw "Missing licensed Altium SDK file: $file"}
}
$msbuild=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe'
& $msbuild (Join-Path $project 'AltiumRevisionRetirement.csproj') /t:Rebuild /p:Configuration=Release "/p:AltiumSdkRoot=$AltiumSdkRoot" /nologo /verbosity:minimal
if($LASTEXITCODE -ne 0) {throw 'Extension build failed.'}
# A local EPD has the complete Developer schema and paths resolved for this installation.
[xml]$epd=Get-Content -LiteralPath (Join-Path $project 'AltiumRevisionRetirement.EPD') -Raw
$epd.Main.ExtensionOptions.ExtensionSourceLocation=$project
$epd.Main.ExtensionVersionOptions.ExtensionTopLevelFolder=(Join-Path $project 'bin\Release')
$epd.Main.ExtensionVersionOptions.ExtensionSDKTicket=(Join-Path $AltiumSdkRoot 'SDK.ticket')
$epd.Save((Join-Path $project 'AltiumRevisionRetirement.local.EPD'))
Write-Output "Built native extension and local EPD in $project"
