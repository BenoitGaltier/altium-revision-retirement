[CmdletBinding()]
param([switch]$CheckOnly,[string]$AltiumApplication,[string]$AltiumExtensionsPath,[string]$BackupRoot)
$ErrorActionPreference='Stop'
$package=Split-Path -Parent $PSScriptRoot
$manifestPath=Join-Path $package 'manifest.json'
if(-not (Test-Path -LiteralPath $manifestPath)) {
    throw 'This is a source download, not the compiled installer. Download altium-revision-retirement-<version>-windows-x64.zip from GitHub Releases, extract it, and run .\Install.ps1. Do not run build.ps1 or package.ps1 on the target PC. Releases: https://github.com/BenoitGaltier/altium-revision-retirement/releases/latest'
}
if(-not [Environment]::Is64BitProcess) {throw 'Use 64-bit Windows PowerShell to install this x64 Altium extension.'}
$framework=Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -Name Release -ErrorAction SilentlyContinue
if(-not $framework -or [int]$framework.Release -lt 528040) {throw '.NET Framework 4.8 or later is required. Install the Windows .NET Framework 4.8 runtime, then rerun Install.ps1. No developer pack or compiler is needed.'}
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$hrid='AltiumRevisionRetirement'
$extensionGuid='f19cf220-6b04-4d43-a356-eebe90b392e6'
if($manifest.schemaVersion -ne 2 -or $manifest.extension.hrid -ne $hrid -or $manifest.extension.guid -ne $extensionGuid) {throw 'Invalid extension package identity or format. Extract the complete current Windows release.'}
$version=[version][string]$manifest.extension.version
$managedNames=@('AltiumRevisionRetirement.dll','AltiumRevisionRetirement.ins','AltiumRevisionRetirement.rcs','fluidra-extension-small.png','fluidra-extension-large.png','USER_GUIDE.en.md','Altium.SDK.dll','Altium.SDK.Interfaces.dll','THIRD_PARTY_NOTICES.md')
$files=@($manifest.files)
if($files.Count -ne $managedNames.Count -or @($files.name | ForEach-Object {([string]$_).ToLowerInvariant()} | Select-Object -Unique).Count -ne $managedNames.Count -or @($files | Where-Object {$_.name -notin $managedNames}).Count -gt 0) {throw 'The package file list is incomplete or invalid.'}
foreach($file in $files) {
    $path=Join-Path $package ('payload\'+$file.name)
    if(-not (Test-Path -LiteralPath $path) -or $file.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) {throw "Package integrity check failed: $($file.name). Download and extract the compiled release ZIP again."}
}
if(-not $AltiumExtensionsPath) {
    $builds=@(Get-ChildItem 'HKLM:\SOFTWARE\Altium\Builds' | ForEach-Object {Get-ItemProperty $_.PSPath} | Where-Object {$_.Installed -eq $true})
    if($AltiumApplication) {$builds=@($builds | Where-Object {$_.Application -eq $AltiumApplication})}
    $selected=$builds | Sort-Object {[version]$_.FullBuild} -Descending | Select-Object -First 1
    if(-not $selected) {throw 'Altium Designer installation not found. Select the correct registry Application value with -AltiumApplication, or specify its -AltiumExtensionsPath.'}
    $application=[string]$selected.Application
    if(-not $application.StartsWith('Altium\',[StringComparison]::OrdinalIgnoreCase)) {$application='Altium\'+$application}
    $AltiumExtensionsPath=Join-Path $env:ProgramData ($application+'\Extensions')
    Write-Output ('Selected Altium Designer '+$selected.FullBuild+' ('+$application+').')
}
$extensionRoot=[IO.Path]::GetFullPath($AltiumExtensionsPath.TrimEnd('\','/'))
if((Split-Path -Leaf $extensionRoot) -ne 'Extensions') {throw '-AltiumExtensionsPath must name the installed Altium Extensions directory.'}
$registryPath=Join-Path $extensionRoot 'ExtensionsRegistry.xml'
if(-not (Test-Path -LiteralPath $registryPath)) {throw "Altium extension registry not found: $registryPath. Select a valid existing Altium installation."}
$destination=Join-Path $extensionRoot $hrid
if((Test-Path -LiteralPath $destination) -and ((Get-Item -LiteralPath $destination).Attributes -band [IO.FileAttributes]::ReparsePoint)) {throw 'The extension destination must not be a junction or symbolic link.'}
$libraryPaths=@{'Altium.SDK.dll'=(Join-Path $package 'payload\Altium.SDK.dll');'Altium.SDK.Interfaces.dll'=(Join-Path $package 'payload\Altium.SDK.Interfaces.dll')}
$dependencies=@($manifest.dependencies)
if($dependencies.Count -ne 2 -or @($dependencies.name | Select-Object -Unique).Count -ne 2) {throw 'Invalid runtime dependency list.'}
foreach($dependency in $dependencies) {
    if(-not $libraryPaths.ContainsKey([string]$dependency.name)) {throw 'Unexpected runtime dependency.'}
    $path=$libraryPaths[[string]$dependency.name]
    if($dependency.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $dependency.sha256) {throw "Bundled runtime library metadata mismatch: $($dependency.name). Download and extract the complete release again."}
    Write-Output ('Bundled runtime verified: '+$dependency.name+' ('+$dependency.assemblyIdentity+').')
}
[xml]$registry=Get-Content -LiteralPath $registryPath -Raw
if($registry.DocumentElement.Name -ne 'Extensions') {throw 'Unexpected Altium extension registry format.'}
$entries=@($registry.Extensions.Item | Where-Object {$_.HRID -eq $hrid})
if($entries.Count -gt 1) {throw 'Duplicate extension registrations found. No files were changed.'}
if($entries.Count -eq 1 -and ([IO.Path]::GetFullPath([string]$entries[0].Path) -ne $destination -or [string]$entries[0].Guid -ne $extensionGuid)) {throw 'This extension is already registered with a different path or GUID. No files were changed.'}
Write-Output ('Compiled extension '+$version+'; destination: '+$destination)
if($CheckOnly) {Write-Output 'Checks passed. No files or registrations changed. Close Altium Designer and run Install.ps1 without -CheckOnly to install.';return}
if(@(Get-Process X2 -ErrorAction SilentlyContinue).Count -gt 0) {throw 'Close Altium Designer before installation. The installer never closes documents or terminates Altium.'}
if(-not $BackupRoot) {$BackupRoot=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'AltiumRevisionRetirement\InstallationBackups'}
$backup=Join-Path ([IO.Path]::GetFullPath($BackupRoot)) ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($backup) | Out-Null
Copy-Item -LiteralPath $registryPath -Destination (Join-Path $backup 'ExtensionsRegistry.xml')
$registryHash=(Get-FileHash -LiteralPath $registryPath -Algorithm SHA256).Hash
$otherEntries=@($registry.Extensions.Item | Where-Object {$_.HRID -ne $hrid} | ForEach-Object {$_.OuterXml})
$sources=@{};$expectedHashes=@{};$previous=@{}
foreach($file in $files) {$sources[$file.name]=Join-Path $package ('payload\'+$file.name);$expectedHashes[$file.name]=$file.sha256}
foreach($name in $sources.Keys) {
    $target=Join-Path $destination $name
    $previous[$name]=Test-Path -LiteralPath $target
    if($previous[$name] -and ((Get-Item -LiteralPath $target).Attributes -band [IO.FileAttributes]::ReparsePoint)) {throw 'Managed runtime files must not be symbolic links.'}
    if($previous[$name]) {Copy-Item -LiteralPath $target -Destination (Join-Path $backup $name)}
}
$registryCommitted=$false
$temporary=Join-Path $extensionRoot ('ExtensionsRegistry.'+$hrid+'.'+[Guid]::NewGuid().ToString('N')+'.tmp')
try {
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    foreach($name in $sources.Keys) {
        $target=Join-Path $destination $name
        Copy-Item -LiteralPath $sources[$name] -Destination $target -Force
        if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $expectedHashes[$name]) {throw "Installed hash mismatch: $name"}
    }
    if($entries.Count -eq 0) {
        # Registration structure generated by native Altium Developer, limited to this HRID.
        [xml]$native='<Item HRID="AltiumRevisionRetirement" Guid="f19cf220-6b04-4d43-a356-eebe90b392e6"><Path/><Status>0</Status><VaultGuid/><CreatedBy/><CategoryGuid>793A1F67-0B22-4E01-A5DE-3176A1E8C60D</CategoryGuid><CategoryName/><ReadMe/><Help/><Requirements/><Title/><ShortDescription/><LongDescription/><SmallImage/><LargeImage/><Version/><VersionGuid/><ReleasedDate>0</ReleasedDate><ReleaseNotes/><DateInstalled>0</DateInstalled><PlatformVersions><DXP BuildNumber="1.0.6.0"/><EDP BuildNumber="10.0.6.0"/><MaxDXP BuildNumber="0.0.0.0"/><MaxEDP BuildNumber="0.0.0.0"/></PlatformVersions></Item>'
        $entry=$registry.ImportNode($native.DocumentElement,$true)
        [void]$registry.DocumentElement.AppendChild($entry)
    } else {$entry=$entries[0]}
    $entry.Path=[string]$destination
    $entry.Title=[string]$manifest.extension.title
    $entry.CreatedBy=[string]$manifest.extension.publisher
    $entry.ShortDescription=[string]$manifest.extension.shortDescription
    $entry.LongDescription=[string]$manifest.extension.longDescription
    $entry.Help=[string]$manifest.extension.help
    $entry.ReadMe=[string]$manifest.extension.help
    $entry.SmallImage=[string](Join-Path $destination 'fluidra-extension-small.png')
    $entry.LargeImage=[string](Join-Path $destination 'fluidra-extension-large.png')
    $entry.Version=[string]$version
    $entry.DateInstalled=[DateTime]::Now.ToOADate().ToString([Globalization.CultureInfo]::InvariantCulture)
    if((Get-FileHash -LiteralPath $registryPath -Algorithm SHA256).Hash -ne $registryHash) {throw 'Altium extension registry changed during installation. Registration was not replaced.'}
    $registry.Save($temporary)
    [xml]$candidate=Get-Content -LiteralPath $temporary -Raw
    $candidateOthers=@($candidate.Extensions.Item | Where-Object {$_.HRID -ne $hrid} | ForEach-Object {$_.OuterXml})
    if(($candidateOthers -join "`n") -ne ($otherEntries -join "`n")) {throw 'Another extension registration would change. Installation stopped.'}
    [IO.File]::Replace($temporary,$registryPath,(Join-Path $backup 'ExtensionsRegistry.replaced.xml'))
    $registryCommitted=$true
    @($sources.Keys | ForEach-Object {[pscustomobject]@{File=$_;SHA256=$expectedHashes[$_]}}) | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $backup 'installed-files.json') -Encoding UTF8
} catch {
    $failure=$_
    if(-not $registryCommitted) {
        foreach($name in $previous.Keys) {
            $target=Join-Path $destination $name
            if($previous[$name]) {Copy-Item -LiteralPath (Join-Path $backup $name) -Destination $target -Force}
            elseif(Test-Path -LiteralPath $target) {Remove-Item -LiteralPath $target -Force}
        }
    }
    throw "Installation failed: $($failure.Exception.Message) Backup: $backup. If access was denied, run PowerShell as administrator."
} finally {if(Test-Path -LiteralPath $temporary) {Remove-Item -LiteralPath $temporary -Force}}
Write-Output ('Installed and verified Altium Revision Retirement '+$version+'.')
Write-Output ('Backup and receipt: '+$backup)
Write-Output 'Restart Altium Designer, then open Tools > Revision Retirement. No compilation or workspace lifecycle change was performed.'
