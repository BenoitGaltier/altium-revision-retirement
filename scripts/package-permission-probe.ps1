[CmdletBinding()]
param([switch]$SkipBuild,[string]$ProductionPackage)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
if(-not $SkipBuild) {& (Join-Path $PSScriptRoot 'build-permission-probe.ps1')}
if(-not $ProductionPackage) {$ProductionPackage=Join-Path $project 'dist\altium-revision-retirement-0.1.9-windows-x64.zip'}
if((Get-FileHash -LiteralPath $ProductionPackage).Hash -ne '225CFE2DC26A3A7D6F494500CB03985421B806B50F8B4DEB1CD6417E48C4B112') {throw 'Select the published official 0.1.9 Windows ZIP; its hash must match the original release.'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead($ProductionPackage)
try {
    $entry=@($zip.Entries | Where-Object {$_.FullName.Replace('\','/') -eq 'payload/AltiumRevisionRetirement.dll'})
    if($entry.Count -ne 1) {throw 'The production payload DLL is missing or ambiguous.'}
    $stream=$entry[0].Open();$hash=[Security.Cryptography.SHA256]::Create()
    try {$productionHash=[BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-','')} finally {$stream.Dispose();$hash.Dispose()}
} finally {$zip.Dispose()}
$dll=Join-Path $project 'bin\PermissionDiagnostics\AltiumRevisionRetirement.dll'
if([Diagnostics.FileVersionInfo]::GetVersionInfo($dll).FileVersion -ne '0.1.9.9000' -or [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($dll)).Contains('AddLifeCycleStateChanges')) {throw 'The read-only diagnostic DLL guard failed.'}
$stage=Join-Path $project ('dist\probe-staging\'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage)|Out-Null
foreach($file in @('Use-PermissionProbe.ps1','Collect-AltiumDiagnostics.ps1','registry-xml.ps1')) {Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $stage}
Copy-Item -LiteralPath $dll -Destination $stage
Copy-Item -LiteralPath (Join-Path $project 'docs\PERMISSION_INVESTIGATION.md') -Destination (Join-Path $stage 'README.md')
$source=(git -C $project rev-parse HEAD).Trim()
[ordered]@{schema=1;probeVersion='0.1.9.9000';probeSHA256=(Get-FileHash -LiteralPath $dll).Hash;productionSHA256=$productionHash;sourceCommit=$source;nativeLifecycleWriteReferenceExcluded=$true}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $stage 'probe-manifest.json') -Encoding UTF8
$archive=Join-Path $project 'dist\AltiumRevisionRetirement-ReadOnly-Permission-Probe-0.1.9.9000.zip'
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -Force
$digest=(Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant()
"$digest  $(Split-Path -Leaf $archive)" | Set-Content -LiteralPath ($archive+'.sha256') -Encoding ascii
Write-Output $archive
Write-Output 'Diagnostic package prepared. No workstation, installed file or workspace was changed.'
