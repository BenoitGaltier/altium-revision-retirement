$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$root=Join-Path $project ('.artifacts\probe-swap-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
$package=Join-Path $root 'package';$extensions=Join-Path $root 'target\Extensions';$folder=Join-Path $extensions 'AltiumRevisionRetirement'
[IO.Directory]::CreateDirectory($package)|Out-Null;[IO.Directory]::CreateDirectory($folder)|Out-Null
Copy-Item -LiteralPath (Join-Path $project 'scripts\Use-PermissionProbe.ps1') -Destination $package
Copy-Item -LiteralPath (Join-Path $project 'scripts\registry-xml.ps1') -Destination $package
$target=Join-Path $folder 'AltiumRevisionRetirement.dll';$probe=Join-Path $package 'AltiumRevisionRetirement.dll'
[IO.File]::WriteAllText($target,'production fixture');[IO.File]::WriteAllText($probe,'read-only probe fixture')
$original=(Get-FileHash $target).Hash;$probeHash=(Get-FileHash $probe).Hash
[ordered]@{schema=1;probeVersion='0.1.9.9000';productionSHA256=$original;probeSHA256=$probeHash}|ConvertTo-Json|Set-Content (Join-Path $package 'probe-manifest.json')
$doc=[Xml.XmlDocument]::new();$doc.LoadXml('<Extensions><Item HRID="AltiumRevisionRetirement" Guid="f19cf220-6b04-4d43-a356-eebe90b392e6"><Path/></Item><Item HRID="VaultExplorer"><Description>Keep exactly</Description></Item></Extensions>');$doc.Extensions.Item[0].Path=[string]$folder
$registry=Join-Path $extensions 'ExtensionsRegistry.xml';$doc.Save($registry);$registryHash=(Get-FileHash $registry).Hash
$evidence=Join-Path $root 'diagnostics.json';@{ExtensionsPath=$extensions}|ConvertTo-Json|Set-Content $evidence
$backup=Join-Path $root 'backups';$script=Join-Path $package 'Use-PermissionProbe.ps1'
$state=[pscustomobject]@{Running=$false}
$mock={param([string]$Name,[string]$ErrorAction) if($state.Running){[pscustomobject]@{Id=999}}}.GetNewClosure();Set-Item Function:Get-Process $mock
function Reject($action,[string]$pattern){$failed=$false;try{&$action|Out-Null}catch{if($_.Exception.Message -notmatch $pattern){throw};$failed=$true};if(-not $failed){throw 'Expected a blocked probe swap'}}
$state.Running=$true;Reject {& $script -EvidenceReport $evidence -BackupRoot $backup} 'Close Altium';$state.Running=$false
if((Get-FileHash $target).Hash -ne $original){throw 'Open Designer did not protect the installed file'}
& $script -EvidenceReport $evidence -BackupRoot $backup | Out-Null
if((Get-FileHash $target).Hash -ne $probeHash -or (Get-FileHash $registry).Hash -ne $registryHash){throw 'Probe swap failed or changed the registry'}
$receipt=@(Get-ChildItem $backup -Recurse -Filter 'restore-receipt.json')[0].FullName
Reject {& $script -EvidenceReport $evidence -BackupRoot $backup} 'differs from the expected version'
[IO.File]::WriteAllText($target,'unexpected intervening update')
Reject {& $script -RestoreReceipt $receipt} 'differs from the expected version'
Copy-Item -LiteralPath $probe -Destination $target -Force
& $script -RestoreReceipt $receipt | Out-Null
if((Get-FileHash $target).Hash -ne $original -or (Get-FileHash $registry).Hash -ne $registryHash){throw 'Restore did not preserve exact original DLL/registry'}
[IO.File]::WriteAllText($probe,'corrupt download');Reject {& $script -EvidenceReport $evidence -BackupRoot $backup} 'package hash mismatch'
Write-Output 'PASS probe-only DLL swap and exact restore; open-process, corrupt-package and intervening-update guards; no XML writes or native workspace calls.'
