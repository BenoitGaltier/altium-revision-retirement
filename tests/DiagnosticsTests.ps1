$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$fixture=Join-Path $project ('.artifacts\diag-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
$root=Join-Path $fixture 'target\Extensions'
$backups=Join-Path $fixture 'backups'
$output=Join-Path $fixture 'report'
$dataRoot=Join-Path $fixture 'user-data'
[IO.Directory]::CreateDirectory((Join-Path $root 'VaultExplorer')) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $root 'AltiumRevisionRetirement')) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $backups '20261111-before')) | Out-Null
$doc=New-Object Xml.XmlDocument
$doc.LoadXml('<Extensions><Item HRID="VaultExplorer"><Path/><Version>1.2</Version><Status>0</Status></Item><Item HRID="AltiumRevisionRetirement"><Path/><Version>0.1.7.0</Version><Status>0</Status></Item></Extensions>')
$doc.Extensions.Item[0].Path=[string](Join-Path $root 'VaultExplorer')
$doc.Extensions.Item[1].Path=[string](Join-Path $root 'AltiumRevisionRetirement')
$registry=Join-Path $root 'ExtensionsRegistry.xml'
$doc.Save($registry)
Copy-Item -LiteralPath $registry -Destination (Join-Path $backups '20261111-before\ExtensionsRegistry.xml')
[IO.File]::WriteAllText((Join-Path $root 'VaultExplorer\VaultExplorer.ins'),'fixture')
$before=(Get-FileHash -LiteralPath $registry).Hash
& (Join-Path $project 'scripts\Collect-AltiumDiagnostics.ps1') -AltiumExtensionsPath $root -BackupRoot $backups -RevisionRetirementDataPath $dataRoot -OutputDirectory $output
if((Get-FileHash -LiteralPath $registry).Hash -ne $before) {throw 'Collector changed the Altium registry'}
if(-not (Test-Path -LiteralPath ($output+'.zip'))) {throw 'Diagnostic archive missing'}
$report=Get-Content -LiteralPath (Join-Path $output 'diagnostics.json') -Raw | ConvertFrom-Json
if(-not $report.VaultExplorer.Registered -or -not $report.VaultExplorer.InsExists -or -not $report.RegistryUnchangedDuringCollection -or -not $report.CopiedRegistryMatchesSource -or $report.Backups.Count -ne 1 -or -not $report.Backups[0].OtherRegistrationsMatchCurrent) {throw 'Incorrect diagnostics report'}
if($report.ScanEvidence.Exists -or $report.WorkspaceContactedByCollector -or $report.ScanEvidence.PermissionAPIsExecutedByCollector) {throw 'Missing scan evidence was invented or collector claimed a live permission test'}
$log=Join-Path $dataRoot 'Diagnostics\last-scan.txt'
[IO.Directory]::CreateDirectory((Split-Path -Parent $log)) | Out-Null
$text="status=succeeded`r`nstarted_utc=2026-11-18T09:00:00Z`r`nworkspace=Fixture workspace`r`nmessage=Blocked: GetACLEntries: read rejected PortalSessionHandle=secret-value Authorization: Bearer hidden-value https://example.invalid/service?ticket=private-value`r`nlast_progress=Reading item history"
[IO.File]::WriteAllText($log,$text,[Text.UTF8Encoding]::new($false))
$logBefore=(Get-FileHash -LiteralPath $log).Hash
$probeFile=Join-Path $dataRoot 'Diagnostics\permission-probe-fixture.json'
[ordered]@{Schema=1;Build='0.1.9.9000 READ-ONLY';NativeWritesEnabled=$false;Events=@(@{Stage='acl-entry';Data=@{Allowed=$false;AllowPermissions=15}})} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $probeFile -Encoding UTF8
$probeBefore=(Get-FileHash -LiteralPath $probeFile).Hash
& (Join-Path $project 'scripts\Collect-AltiumDiagnostics.ps1') -AltiumExtensionsPath $root -BackupRoot $backups -RevisionRetirementDataPath $dataRoot -OutputDirectory (Join-Path $fixture 'permission-report') | Out-Null
$permissions=Get-Content -LiteralPath (Join-Path $fixture 'permission-report\diagnostics.json') -Raw | ConvertFrom-Json
$captured=[IO.File]::ReadAllText((Join-Path $fixture 'permission-report\last-scan.redacted.txt'))
if(-not $permissions.ScanEvidence.Exists -or -not $permissions.ScanEvidence.CredentialsOrURLsRedacted -or -not $permissions.ScanEvidence.SourceUnchangedDuringCollection -or $permissions.ScanEvidence.Fields.status -ne 'succeeded') {throw 'Scan evidence was not collected correctly'}
if(-not $permissions.ProbeEvidence.Exists -or -not $permissions.ProbeEvidence.SourceUnchangedDuringCollection -or (Get-FileHash -LiteralPath $probeFile).Hash -ne $probeBefore -or -not (Test-Path -LiteralPath (Join-Path $fixture 'permission-report\permission-probe.redacted.txt'))) {throw 'Native probe evidence was not preserved'}
foreach($secret in @('secret-value','hidden-value','private-value','example.invalid')) {if($captured.Contains($secret) -or ([IO.File]::ReadAllText((Join-Path $fixture 'permission-report\diagnostics.json'))).Contains($secret)) {throw 'Credential-like value escaped evidence redaction'}}
if(-not $captured.Contains('GetACLEntries: read rejected') -or (Get-FileHash -LiteralPath $log).Hash -ne $logBefore -or (Get-FileHash -LiteralPath $registry).Hash -ne $before) {throw 'Evidence cause was lost or a source file changed'}
$unsafeData=Join-Path $dataRoot 'unsafe-output'
$rejected=$false
try {& (Join-Path $project 'scripts\Collect-AltiumDiagnostics.ps1') -AltiumExtensionsPath $root -BackupRoot $backups -RevisionRetirementDataPath $dataRoot -OutputDirectory $unsafeData} catch {if($_.Exception.Message -notmatch 'outside the extension user-data') {throw};$rejected=$true}
if(-not $rejected -or (Test-Path -LiteralPath $unsafeData)) {throw 'Collector accepted output inside source user-data'}
$unsafe=Join-Path $root 'unsafe-report'
$rejected=$false
try {& (Join-Path $project 'scripts\Collect-AltiumDiagnostics.ps1') -AltiumExtensionsPath $root -BackupRoot $backups -RevisionRetirementDataPath $dataRoot -OutputDirectory $unsafe} catch {if($_.Exception.Message -notmatch 'outside the Altium installation') {throw};$rejected=$true}
if(-not $rejected -or (Test-Path -LiteralPath $unsafe)) {throw 'Collector accepted output inside the Altium installation'}
$doc.Extensions.Item[0].Path=''
$doc.Save($registry)
& (Join-Path $project 'scripts\Collect-AltiumDiagnostics.ps1') -AltiumExtensionsPath $root -BackupRoot $backups -RevisionRetirementDataPath $dataRoot -OutputDirectory (Join-Path $fixture 'empty-path') | Out-Null
$missing=Get-Content -LiteralPath (Join-Path $fixture 'empty-path\diagnostics.json') -Raw | ConvertFrom-Json
if(-not $missing.VaultExplorer.Registered -or $missing.VaultExplorer.FolderExists -or $missing.VaultExplorer.InsExists) {throw 'Missing Explorer path was not reported correctly'}
[IO.File]::WriteAllText($registry,'<Extensions><Item>broken')
$brokenHash=(Get-FileHash -LiteralPath $registry).Hash
& (Join-Path $project 'scripts\Collect-AltiumDiagnostics.ps1') -AltiumExtensionsPath $root -BackupRoot $backups -RevisionRetirementDataPath $dataRoot -OutputDirectory (Join-Path $fixture 'broken-xml') | Out-Null
$broken=Get-Content -LiteralPath (Join-Path $fixture 'broken-xml\diagnostics.json') -Raw | ConvertFrom-Json
if(-not $broken.RegistryReadError -or -not (Test-Path -LiteralPath (Join-Path $fixture 'broken-xml.zip')) -or (Get-FileHash -LiteralPath $registry).Hash -ne $brokenHash) {throw 'Malformed XML evidence was lost or modified'}
Write-Output 'PASS evidence collection preserves registry and scan sources, handles missing/malformed data, redacts credentials and rejects output inside Altium/user-data. No workspace permission APIs were executed.'
