[CmdletBinding(DefaultParameterSetName='Activate')]
param(
    [Parameter(Mandatory=$true,ParameterSetName='Activate')][string]$EvidenceReport,
    [Parameter(Mandatory=$true,ParameterSetName='Restore')][string]$RestoreReceipt,
    [Parameter(ParameterSetName='Activate')][string]$BackupRoot
)
$ErrorActionPreference='Stop'
if(@(Get-Process -Name X2 -ErrorAction SilentlyContinue).Count -gt 0) {throw 'Close Altium Designer normally before swapping or restoring the diagnostic DLL.'}
$hrid='AltiumRevisionRetirement'
$guid='f19cf220-6b04-4d43-a356-eebe90b392e6'
if($PSCmdlet.ParameterSetName -eq 'Activate') {
    $evidence=Get-Content -LiteralPath $EvidenceReport -Raw | ConvertFrom-Json
    $extensionRoot=[IO.Path]::GetFullPath([string]$evidence.ExtensionsPath).TrimEnd('\','/')
    $manifest=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'probe-manifest.json') -Raw | ConvertFrom-Json
    if($manifest.schema -ne 1 -or $manifest.probeVersion -ne '0.1.9.9000') {throw 'Invalid probe package manifest.'}
    $replacement=Join-Path $PSScriptRoot 'AltiumRevisionRetirement.dll'
    if((Get-FileHash -LiteralPath $replacement).Hash -ne $manifest.probeSHA256) {throw 'Probe package hash mismatch.'}
    $expectedCurrent=[string]$manifest.productionSHA256
} else {
    $receipt=Get-Content -LiteralPath $RestoreReceipt -Raw | ConvertFrom-Json
    if($receipt.schema -ne 1) {throw 'Invalid probe restore receipt.'}
    $extensionRoot=[IO.Path]::GetFullPath([string]$receipt.extensionsPath).TrimEnd('\','/')
    $replacement=[string]$receipt.backupFile
    if((Get-FileHash -LiteralPath $replacement).Hash -ne $receipt.originalSHA256) {throw 'Backup hash mismatch.'}
    $expectedCurrent=[string]$receipt.probeSHA256
}
if((Split-Path -Leaf $extensionRoot) -ne 'Extensions') {throw 'The report must select an existing Altium Extensions directory.'}
$registryPath=Join-Path $extensionRoot 'ExtensionsRegistry.xml'
$registryHash=(Get-FileHash -LiteralPath $registryPath).Hash
. (Join-Path $PSScriptRoot 'registry-xml.ps1')
$registry=Read-ExtensionRegistryXml $registryPath
$entries=@($registry.DocumentElement.ChildNodes | Where-Object {$_ -is [Xml.XmlElement] -and $_.Name -ceq 'Item' -and $_.NamespaceURI -eq '' -and $_.GetAttribute('HRID') -eq $hrid})
$folder=Join-Path $extensionRoot $hrid
if($entries.Count -ne 1 -or [Guid]$entries[0].GetAttribute('Guid') -ne [Guid]$guid -or [IO.Path]::GetFullPath([string]$entries[0].Path).TrimEnd('\','/') -ne $folder) {throw 'The original extension registration does not match the selected folder.'}
$target=Join-Path $folder 'AltiumRevisionRetirement.dll'
foreach($path in @($folder,$target)) {if((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) {throw 'Probe targets cannot be junctions or symbolic links.'}}
$currentHash=(Get-FileHash -LiteralPath $target).Hash
if($currentHash -ne $expectedCurrent) {throw 'The installed DLL differs from the expected version. No file was replaced.'}
if($PSCmdlet.ParameterSetName -eq 'Activate') {
    if(-not $BackupRoot) {$BackupRoot=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'AltiumRevisionRetirement\ProbeBackups'}
    $backupDirectory=Join-Path ([IO.Path]::GetFullPath($BackupRoot)) ([Guid]::NewGuid().ToString('N'))
    if($backupDirectory.StartsWith((Split-Path -Parent $extensionRoot)+'\',[StringComparison]::OrdinalIgnoreCase)) {throw 'The probe backup must stay outside the Altium installation directory.'}
    [IO.Directory]::CreateDirectory($backupDirectory)|Out-Null
    $backupFile=Join-Path $backupDirectory 'AltiumRevisionRetirement.dll.backup'
    Copy-Item -LiteralPath $target -Destination $backupFile
    if((Get-FileHash -LiteralPath $backupFile).Hash -ne $currentHash) {throw 'Backup verification failed.'}
    $receipt=[ordered]@{schema=1;extensionsPath=$extensionRoot;backupFile=$backupFile;originalSHA256=$currentHash;probeSHA256=$manifest.probeSHA256;registrySHA256=$registryHash;createdUTC=[DateTime]::UtcNow.ToString('o')}
    $receiptPath=Join-Path $backupDirectory 'restore-receipt.json'
    $receipt|ConvertTo-Json|Set-Content -LiteralPath $receiptPath -Encoding UTF8
}
$replacementHash=(Get-FileHash -LiteralPath $replacement).Hash
try {
    Copy-Item -LiteralPath $replacement -Destination $target -Force
    if((Get-FileHash -LiteralPath $target).Hash -ne $replacementHash) {throw 'Replacement DLL hash mismatch.'}
} catch {
    if($PSCmdlet.ParameterSetName -eq 'Activate') {Copy-Item -LiteralPath $backupFile -Destination $target -Force}
    throw
}
if((Get-FileHash -LiteralPath $registryPath).Hash -ne $registryHash) {throw 'The registry changed externally. This script never writes the registry; retain the evidence.'}
if($PSCmdlet.ParameterSetName -eq 'Activate') {
    Write-Output 'Read-only probe activated. The registry and other extension files were not changed.'
    Write-Output ('Restore receipt: '+$receiptPath)
    Write-Output 'Restart Designer and open the usual Revision Retirement command. The title must say READ-ONLY permission probe.'
} else {Write-Output 'Original DLL restored and verified. The registry was not changed. Restart Designer.'}
