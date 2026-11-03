[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$localEpd=Join-Path $project 'AltiumRevisionRetirement.local.EPD'
if(-not (Test-Path -LiteralPath $localEpd)) {throw 'Run scripts/build.ps1 first.'}
[xml]$epd=Get-Content -LiteralPath $localEpd -Raw
$destination=[IO.Path]::GetFullPath([string]$epd.Main.ExtensionVersionOptions.ExtensionTopLevelFolder)
if((Split-Path -Leaf $destination) -ne 'AltiumRevisionRetirement') {throw 'Unexpected extension destination. Rebuild the local EPD.'}
$extensionRoot=Split-Path -Parent $destination
if((Split-Path -Leaf $extensionRoot) -ne 'Extensions') {throw 'Destination must be inside the Altium Extensions directory.'}
$registryPath=Join-Path $extensionRoot 'ExtensionsRegistry.xml'
[xml]$registry=Get-Content -LiteralPath $registryPath -Raw
$entries=@($registry.Extensions.Item | Where-Object {$_.HRID -eq 'AltiumRevisionRetirement'})
if($entries.Count -ne 1 -or [IO.Path]::GetFullPath([string]$entries[0].Path) -ne $destination) {
    throw 'Open the generated local EPD in Altium Developer and use Install Extension first. This script only updates an existing registration.'
}
if(@(Get-Process X2 -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'Close Altium Designer before updating runtime files and display metadata. Unsaved documents are never closed by this script.'
}
$files=@('AltiumRevisionRetirement.dll','AltiumRevisionRetirement.ins','AltiumRevisionRetirement.rcs','Altium.SDK.dll','Altium.SDK.Interfaces.dll')
$source=Join-Path $project 'bin\Release'
foreach($file in $files) {if(-not (Test-Path -LiteralPath (Join-Path $source $file))) {throw "Missing build output: $file"}}
$backup=Join-Path $project ('.artifacts\installation\'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
Copy-Item -LiteralPath $registryPath -Destination (Join-Path $backup 'ExtensionsRegistry.xml')
$registryHash=(Get-FileHash -LiteralPath $registryPath -Algorithm SHA256).Hash
$otherEntries=@($registry.Extensions.Item | Where-Object {$_.HRID -ne 'AltiumRevisionRetirement'} | ForEach-Object {$_.OuterXml})
$receipt=@()
foreach($file in $files) {
    $target=Join-Path $destination $file
    if(Test-Path -LiteralPath $target) {Copy-Item -LiteralPath $target -Destination (Join-Path $backup $file)}
    $origin=Join-Path $source $file
    Copy-Item -LiteralPath $origin -Destination $target -Force
    $expected=(Get-FileHash -LiteralPath $origin -Algorithm SHA256).Hash
    $actual=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    if($expected -ne $actual) {throw "Installed hash mismatch: $file"}
    $receipt+=[pscustomobject]@{File=$file;SHA256=$actual}
}
$guide=Join-Path $project 'docs\USER_GUIDE.md'
if(Test-Path -LiteralPath $guide) {Copy-Item -LiteralPath $guide -Destination (Join-Path $destination 'USER_GUIDE.en.md') -Force}
foreach($icon in @('fluidra-extension-small.png','fluidra-extension-large.png')) {
    Copy-Item -LiteralPath (Join-Path $project "assets\$icon") -Destination (Join-Path $destination $icon) -Force
}
# Altium Developer creates the registration. Fill only this extension's display metadata.
$entry=$entries[0]
$entry.Title=[string]$epd.Main.ExtensionVersionOptions.ExtensionTitle
$entry.CreatedBy=[string]$epd.Main.ExtensionVersionOptions.ExtensionPublisherName
$entry.ShortDescription=[string]$epd.Main.ExtensionVersionOptions.ExtensionShortDescription
$entry.LongDescription=[string]$epd.Main.ExtensionVersionOptions.ExtensionLongDescription
$entry.Help=[string]$epd.Main.ExtensionVersionOptions.ExtensionHelp
$entry.ReadMe=[string]$epd.Main.ExtensionVersionOptions.ExtensionHelp
$entry.SmallImage=Join-Path $destination 'fluidra-extension-small.png'
$entry.LargeImage=Join-Path $destination 'fluidra-extension-large.png'
$entry.Version=[string]$epd.Main.ExtensionVersionOptions.ExtensionVersion
$entry.DateInstalled=[DateTime]::Now.ToOADate().ToString([Globalization.CultureInfo]::InvariantCulture)
if((Get-FileHash -LiteralPath $registryPath -Algorithm SHA256).Hash -ne $registryHash) {throw 'Altium extension registry changed during installation. Metadata update stopped.'}
$temporary=Join-Path $extensionRoot ('ExtensionsRegistry.AltiumRevisionRetirement.'+[Guid]::NewGuid().ToString('N')+'.tmp')
$registry.Save($temporary)
[IO.File]::Replace($temporary,$registryPath,(Join-Path $backup 'ExtensionsRegistry.replaced.xml'))
[xml]$verified=Get-Content -LiteralPath $registryPath -Raw
$afterOthers=@($verified.Extensions.Item | Where-Object {$_.HRID -ne 'AltiumRevisionRetirement'} | ForEach-Object {$_.OuterXml})
if(($otherEntries -join "`n") -ne ($afterOthers -join "`n")) {throw 'Unexpected change to another extension registration.'}
$own=@($verified.Extensions.Item | Where-Object {$_.HRID -eq 'AltiumRevisionRetirement'})
if($own.Count -ne 1 -or $own[0].ShortDescription -ne $entry.ShortDescription -or $own[0].Version -ne $entry.Version) {throw 'Installed metadata verification failed.'}
$receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $backup 'installed-files.json') -Encoding utf8
Write-Output "Installed files verified at $destination"
Write-Output 'English description, help link and Fluidra thumbnails verified; other extension registrations preserved.'
Write-Output "Backup and receipt: $backup"
Write-Output 'Restart Altium Designer to load the extension, then open Revision Retirement from the menu. No lifecycle change was performed.'
