[CmdletBinding()]
param([string]$AltiumExtensionsPath,[string]$BackupRoot,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'registry-xml.ps1')
function Read-Entry($Document,[string]$Hrid) {
    $entry=@($Document.Extensions.Item | Where-Object {$_.HRID -eq $Hrid})
    if($entry.Count -ne 1) {return [ordered]@{Registered=$false;MatchingEntries=$entry.Count}}
    $path=[string]$entry[0].Path
    $hasPath=-not [string]::IsNullOrWhiteSpace($path)
    return [ordered]@{Registered=$true;HRID=[string]$entry[0].HRID;Guid=[string]$entry[0].Guid;Version=[string]$entry[0].Version;Status=[string]$entry[0].Status;Path=$path;FolderExists=($hasPath -and (Test-Path -LiteralPath $path -PathType Container));InsExists=($hasPath -and (Test-Path -LiteralPath (Join-Path $path ($Hrid+'.ins')) -PathType Leaf))}
}
function Describe-Dll([string]$Path) {
    $info=[ordered]@{Path=$Path;Exists=(Test-Path -LiteralPath $Path -PathType Leaf)}
    if($info.Exists) {
        $info.SHA256=(Get-FileHash -LiteralPath $Path).Hash
        $info.FileVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo($Path).FileVersion
        try {$info.AssemblyIdentity=[Reflection.AssemblyName]::GetAssemblyName($Path).FullName} catch {$info.MetadataError=$_.Exception.Message}
        $bytes=[IO.File]::ReadAllBytes($Path)
        $info.TargetFrameworkStrings=@([regex]::Matches([Text.Encoding]::ASCII.GetString($bytes),'\.NET(?:Framework|CoreApp|Standard),Version=v[0-9.]+') | ForEach-Object {$_.Value} | Select-Object -Unique)
    }
    return $info
}
$build=$null
if(-not $AltiumExtensionsPath) {
    $build=Get-ChildItem 'HKLM:\SOFTWARE\Altium\Builds' | ForEach-Object {Get-ItemProperty $_.PSPath} | Where-Object {$_.Installed -eq $true} | Sort-Object {[version]$_.FullBuild} -Descending | Select-Object -First 1
    if(-not $build) {throw 'Altium Designer installation not found. Specify -AltiumExtensionsPath.'}
    $application=[string]$build.Application
    if(-not $application.StartsWith('Altium\',[StringComparison]::OrdinalIgnoreCase)) {$application='Altium\'+$application}
    $AltiumExtensionsPath=Join-Path $env:ProgramData ($application+'\Extensions')
}
$root=[IO.Path]::GetFullPath($AltiumExtensionsPath.TrimEnd('\','/'))
if((Split-Path -Leaf $root) -ne 'Extensions') {throw 'Select the existing Altium Extensions directory.'}
$registryPath=Join-Path $root 'ExtensionsRegistry.xml'
if(-not (Test-Path -LiteralPath $registryPath)) {throw 'Altium extension registry not found.'}
if(-not $BackupRoot) {$BackupRoot=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'AltiumRevisionRetirement\InstallationBackups'}
if(-not $OutputDirectory) {$OutputDirectory=Join-Path (Get-Location).Path ('AltiumDiagnostics-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6))}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if($output.StartsWith((Split-Path -Parent $root)+'\',[StringComparison]::OrdinalIgnoreCase) -or $output -eq (Split-Path -Parent $root)) {throw 'The diagnostics output must be outside the Altium installation directory.'}
if(Test-Path -LiteralPath $output) {throw 'Choose a new output directory to avoid overwriting an earlier report.'}
$beforeHash=(Get-FileHash -LiteralPath $registryPath).Hash
[IO.Directory]::CreateDirectory($output) | Out-Null
Copy-Item -LiteralPath $registryPath -Destination (Join-Path $output 'current-ExtensionsRegistry.xml')
$snapshot=$null;$parseError=$null
$explorer=[ordered]@{Registered=$false};$retirement=[ordered]@{Registered=$false}
try {
    $registry=Read-ExtensionRegistryXml $registryPath
    $snapshot=Get-OtherRegistrationSnapshot $registry 'AltiumRevisionRetirement'
    $explorer=Read-Entry $registry 'VaultExplorer'
    $retirement=Read-Entry $registry 'AltiumRevisionRetirement'
} catch {$parseError=$_.Exception.Message}
$report=[ordered]@{ToolVersion='1.0';Mode='Diagnostics only; Altium files are not modified';CollectedUTC=[DateTime]::UtcNow.ToString('o');PowerShellVersion=$PSVersionTable.PSVersion.ToString();Culture=[Globalization.CultureInfo]::CurrentCulture.Name;AltiumBuild=if($build) {[string]$build.FullBuild} else {$null};ExtensionsPath=$root;BackupRoot=[IO.Path]::GetFullPath($BackupRoot);RegistrySHA256=$beforeHash;VaultExplorer=$explorer;RevisionRetirement=$retirement;DLLs=@();RunningAltium=@();Backups=@()}
$report.RegistryReadError=$parseError
$report.RegistryLastWriteUTC=(Get-Item -LiteralPath $registryPath).LastWriteTimeUtc.ToString('o')
if($explorer.Registered -and $explorer.Path) {$report.DLLs+=Describe-Dll (Join-Path $explorer.Path 'VaultExplorer.dll')}
if($retirement.Registered -and $retirement.Path) {
    foreach($name in @('AltiumRevisionRetirement.dll','Altium.SDK.dll','Altium.SDK.Interfaces.dll')) {$report.DLLs+=Describe-Dll (Join-Path $retirement.Path $name)}
}
foreach($process in @(Get-Process -Name X2 -ErrorAction SilentlyContinue)) {
    $info=[ordered]@{ProcessId=$process.Id;Modules=@()}
    try {
        $info.ExecutablePath=$process.Path
        $info.StartedUTC=$process.StartTime.ToUniversalTime().ToString('o')
        $info.Modules=@($process.Modules | Where-Object {$_.ModuleName -match '^(AltiumRevisionRetirement|Altium\.SDK(?:\.Interfaces)?|VaultExplorer)\.dll$'} | ForEach-Object {[ordered]@{Name=$_.ModuleName;Path=$_.FileName}})
    } catch {$info.InspectionError=$_.Exception.Message}
    $report.RunningAltium+=$info
}
$backupDirectories=@(Get-ChildItem -LiteralPath $BackupRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 3)
foreach($directory in $backupDirectories) {
    $backupFile=Join-Path $directory.FullName 'ExtensionsRegistry.xml'
    if(-not (Test-Path -LiteralPath $backupFile)) {continue}
    $info=[ordered]@{Name=$directory.Name;Path=$backupFile;SuccessfulInstallReceipt=(Test-Path -LiteralPath (Join-Path $directory.FullName 'installed-files.json'))}
    try {
        Copy-Item -LiteralPath $backupFile -Destination (Join-Path $output ('before-'+$directory.Name+'.xml'))
        $before=Read-ExtensionRegistryXml $backupFile
        $info.SHA256=(Get-FileHash -LiteralPath $backupFile).Hash
        $info.VaultExplorer=Read-Entry $before 'VaultExplorer'
        $info.RevisionRetirement=Read-Entry $before 'AltiumRevisionRetirement'
        $info.OtherRegistrationsMatchCurrent=if($null -ne $snapshot) {[string]::Equals((Get-OtherRegistrationSnapshot $before 'AltiumRevisionRetirement'),$snapshot,[StringComparison]::Ordinal)} else {$null}
    } catch {$info.Error=$_.Exception.Message}
    $report.Backups+=$info
}
$report.RegistryUnchangedDuringCollection=((Get-FileHash -LiteralPath $registryPath).Hash -eq $beforeHash)
$report.CopiedRegistryMatchesSource=((Get-FileHash -LiteralPath (Join-Path $output 'current-ExtensionsRegistry.xml')).Hash -eq $beforeHash)
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $output 'diagnostics.json') -Encoding UTF8
$archive=$output+'.zip'
Compress-Archive -LiteralPath $output -DestinationPath $archive
Write-Output ('Altium files were not modified. Diagnostic ZIP: '+$archive)
Write-Output ('Explorer registered: '+$explorer.Registered+'; folder exists: '+$explorer.FolderExists+'; INS exists: '+$explorer.InsExists)
Write-Output ('Revision Retirement version: '+$retirement.Version)
