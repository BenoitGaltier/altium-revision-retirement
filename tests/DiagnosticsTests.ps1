$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$fixture=Join-Path $project ('.artifacts\diag-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
$root=Join-Path $fixture 'target\Extensions'
$backups=Join-Path $fixture 'backups'
$output=Join-Path $fixture 'report'
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
& (Join-Path $project 'scripts\Collect-AltiumDiagnostics.ps1') -AltiumExtensionsPath $root -BackupRoot $backups -OutputDirectory $output
if((Get-FileHash -LiteralPath $registry).Hash -ne $before) {throw 'Collector changed the Altium registry'}
if(-not (Test-Path -LiteralPath ($output+'.zip'))) {throw 'Diagnostic archive missing'}
$report=Get-Content -LiteralPath (Join-Path $output 'diagnostics.json') -Raw | ConvertFrom-Json
if(-not $report.VaultExplorer.Registered -or -not $report.VaultExplorer.InsExists -or -not $report.RegistryUnchangedDuringCollection -or -not $report.CopiedRegistryMatchesSource -or $report.Backups.Count -ne 1 -or -not $report.Backups[0].OtherRegistrationsMatchCurrent) {throw 'Incorrect diagnostics report'}
$unsafe=Join-Path $root 'unsafe-report'
$rejected=$false
try {& (Join-Path $project 'scripts\Collect-AltiumDiagnostics.ps1') -AltiumExtensionsPath $root -BackupRoot $backups -OutputDirectory $unsafe} catch {if($_.Exception.Message -notmatch 'outside the Altium installation') {throw};$rejected=$true}
if(-not $rejected -or (Test-Path -LiteralPath $unsafe)) {throw 'Collector accepted output inside the Altium installation'}
$doc.Extensions.Item[0].Path=''
$doc.Save($registry)
& (Join-Path $project 'scripts\Collect-AltiumDiagnostics.ps1') -AltiumExtensionsPath $root -BackupRoot $backups -OutputDirectory (Join-Path $fixture 'empty-path') | Out-Null
$missing=Get-Content -LiteralPath (Join-Path $fixture 'empty-path\diagnostics.json') -Raw | ConvertFrom-Json
if(-not $missing.VaultExplorer.Registered -or $missing.VaultExplorer.FolderExists -or $missing.VaultExplorer.InsExists) {throw 'Missing Explorer path was not reported correctly'}
[IO.File]::WriteAllText($registry,'<Extensions><Item>broken')
$brokenHash=(Get-FileHash -LiteralPath $registry).Hash
& (Join-Path $project 'scripts\Collect-AltiumDiagnostics.ps1') -AltiumExtensionsPath $root -BackupRoot $backups -OutputDirectory (Join-Path $fixture 'broken-xml') | Out-Null
$broken=Get-Content -LiteralPath (Join-Path $fixture 'broken-xml\diagnostics.json') -Raw | ConvertFrom-Json
if(-not $broken.RegistryReadError -or -not (Test-Path -LiteralPath (Join-Path $fixture 'broken-xml.zip')) -or (Get-FileHash -LiteralPath $registry).Hash -ne $brokenHash) {throw 'Malformed XML evidence was lost or modified'}
Write-Output 'PASS diagnostic collection preserves the source registry, copies exact evidence and rejects output inside Altium.'
