$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$sandbox=Join-Path $project ('.artifacts\it-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
$state=[pscustomobject]@{DesignerRunning=$false;Passed=0;Next=0}
$processMock={param([string]$Name,[string]$ErrorAction) if($state.DesignerRunning) {[pscustomobject]@{ProcessName='X2'}}}.GetNewClosure()
Set-Item -Path Function:Get-Process -Value $processMock
function Assert($v,[string]$message) {if(-not $v) {throw $message}}
function Put([string]$path,[string]$value) {[IO.Directory]::CreateDirectory((Split-Path -Parent $path)) | Out-Null;[IO.File]::WriteAllText($path,$value,[Text.UTF8Encoding]::new($false))}
function Reject($action,[string]$pattern) {try {& $action | Out-Null} catch {Assert ($_.Exception.Message -match $pattern) ('Unexpected error: '+$_.Exception.Message);return};throw 'Expected installation to stop.'}
function Test([string]$name,$action) {& $action;$state.Passed++;Write-Output ('PASS '+$name)}
function SaveManifest($f) {$f.Manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $f.Root 'manifest.json') -Encoding UTF8}
function Fixture([string]$registration='none') {
    $state.Next++;$root=Join-Path $sandbox ('f'+$state.Next+' source with spaces')
    Put (Join-Path $root 'scripts\build.ps1') "throw 'A compiler must never be invoked during installation.'"
    Copy-Item -LiteralPath (Join-Path $project 'scripts\install.ps1') -Destination (Join-Path $root 'scripts\install.ps1')
    Copy-Item -LiteralPath (Join-Path $project 'Install.ps1') -Destination (Join-Path $root 'Install.ps1')
    $extensionRoot=Join-Path $root 'target\Extensions';$target=Join-Path $extensionRoot 'AltiumRevisionRetirement'
    $registryPath=Join-Path $extensionRoot 'ExtensionsRegistry.xml'
    [xml]$registry='<Extensions><Item HRID="OtherExtension" Guid="different"><Title>Keep this entry</Title><Path>untouched</Path><Version>9.8.7</Version><Custom><Value>Sentinel</Value></Custom></Item></Extensions>'
    if($registration -ne 'none') {
        [xml]$own='<Item HRID="AltiumRevisionRetirement" Guid="f19cf220-6b04-4d43-a356-eebe90b392e6"><Path/><Title/><CreatedBy/><ShortDescription/><LongDescription/><Help/><ReadMe/><SmallImage/><LargeImage/><Version>0.1.3.0</Version><DateInstalled>0</DateInstalled></Item>'
        $entry=$registry.ImportNode($own.DocumentElement,$true);$entry.Path=[string]$target
        if($registration -eq 'mismatch') {$entry.Path=[string](Join-Path $extensionRoot 'different\AltiumRevisionRetirement')}
        if($registration -eq 'guid-conflict') {$entry.SetAttribute('Guid',[Guid]::NewGuid().ToString('D'))}
        [void]$registry.DocumentElement.AppendChild($entry)
        if($registration -eq 'duplicate') {[void]$registry.DocumentElement.AppendChild($entry.CloneNode($true))}
        Put (Join-Path $target 'AltiumRevisionRetirement.dll') 'old installed DLL'
    }
    [IO.Directory]::CreateDirectory($extensionRoot) | Out-Null;$registry.Save($registryPath)
    $payloadFiles=@(foreach($name in @('AltiumRevisionRetirement.dll','AltiumRevisionRetirement.ins','AltiumRevisionRetirement.rcs','fluidra-extension-small.png','fluidra-extension-large.png','USER_GUIDE.en.md')) {
        Put (Join-Path $root ('payload\'+$name)) ('new '+$name)
        [ordered]@{name=$name;sha256=(Get-FileHash -LiteralPath (Join-Path $root ('payload\'+$name))).Hash.ToLowerInvariant()}
    })
    $sdk=Join-Path $extensionRoot 'Altium Developer\SDK4.0'
    $dependencies=@(foreach($name in @('Altium.SDK.dll','Altium.SDK.Interfaces.dll')) {
        $path=if($name -eq 'Altium.SDK.dll') {Join-Path $sdk 'CSharp\Altium.SDK.dll'} else {Join-Path (Split-Path -Parent $sdk) $name}
        Put $path ('local licensed-library fixture '+$name)
        [ordered]@{name=$name;sha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant();assemblyIdentity='isolated test fixture'}
    })
    $manifest=[ordered]@{schemaVersion=1;extension=[ordered]@{hrid='AltiumRevisionRetirement';guid='f19cf220-6b04-4d43-a356-eebe90b392e6';version='0.1.4.0';title='Altium Revision Retirement';publisher='BenoitGaltier';shortDescription='Review superseded revisions';longDescription='English description';help='https://example.invalid/guide'};files=$payloadFiles;dependencies=$dependencies}
    $fixture=[pscustomobject]@{Root=$root;Installer=(Join-Path $root 'Install.ps1');Target=$target;ExtensionRoot=$extensionRoot;Registry=$registryPath;Sdk=$sdk;Backup=(Join-Path $root 'backups');Manifest=$manifest}
    SaveManifest $fixture
    return $fixture
}
function Install($f,[switch]$CheckOnly) {& $f.Installer -AltiumExtensionsPath $f.ExtensionRoot -BackupRoot $f.Backup -CheckOnly:$CheckOnly}
function Unchanged($f,[string]$hash) {Assert ((Get-FileHash -LiteralPath $f.Registry).Hash -eq $hash) 'The Altium registry changed during a blocked operation.'}

Test 'source download directs users to compiled Releases without running a compiler' {
    $f=Fixture;Remove-Item -LiteralPath (Join-Path $f.Root 'manifest.json')
    Reject {Install $f} 'Download altium-revision-retirement-<version>-windows-x64.zip'
    Assert (-not (Test-Path -LiteralPath $f.Target)) 'Source download deployed runtime files.'
}
Test 'CheckOnly verifies dependencies while Designer is open and writes nothing' {
    $f=Fixture;$before=(Get-FileHash -LiteralPath $f.Registry).Hash;$state.DesignerRunning=$true
    try {$output=@(Install $f -CheckOnly)} finally {$state.DesignerRunning=$false}
    Assert (($output -join "`n") -match 'Checks passed') 'CheckOnly did not complete.'
    Assert (-not (Test-Path -LiteralPath $f.Target)) 'CheckOnly created the extension directory.'
    Assert (-not (Test-Path -LiteralPath $f.Backup)) 'CheckOnly wrote a backup.'
    Unchanged $f $before
}
Test 'missing .NET Framework runtime gives instructions before target writes' {
    $f=Fixture;$before=(Get-FileHash -LiteralPath $f.Registry).Hash
    function Get-ItemProperty {param([string]$LiteralPath,[string]$Name,[string]$ErrorAction) [pscustomobject]@{Release=461808}}
    Reject {Install $f} '.NET Framework 4.8 or later is required'
    Unchanged $f $before
    Assert (-not (Test-Path -LiteralPath $f.Target)) 'Missing .NET runtime partially installed files.'
}
Test 'first installation registers the precompiled payload and preserves other extensions' {
    $f=Fixture;[xml]$before=Get-Content -LiteralPath $f.Registry -Raw;$other=$before.Extensions.Item.OuterXml
    Install $f | Out-Null
    [xml]$after=Get-Content -LiteralPath $f.Registry -Raw
    $own=@($after.Extensions.Item | Where-Object {$_.HRID -eq 'AltiumRevisionRetirement'})
    Assert ($own.Count -eq 1 -and $own[0].Version -eq '0.1.4.0') 'First registration failed.'
    Assert (($after.Extensions.Item | Where-Object {$_.HRID -eq 'OtherExtension'}).OuterXml -eq $other) 'Another extension changed.'
    Assert ((Get-Content -LiteralPath (Join-Path $f.Target 'AltiumRevisionRetirement.dll') -Raw) -eq 'new AltiumRevisionRetirement.dll') 'Precompiled DLL was not installed.'
    Assert (Test-Path -LiteralPath (Join-Path $f.Target 'USER_GUIDE.en.md')) 'English guide missing.'
    Assert (-not (Test-Path -LiteralPath (Join-Path $f.Sdk 'SDK.ticket'))) 'Runtime installation must not need an SDK ticket.'
    Assert (@(Get-ChildItem -LiteralPath $f.Backup -Filter 'installed-files.json' -Recurse).Count -eq 1) 'Hash receipt missing.'
}
Test 'update replaces only managed files and backs up the previous DLL' {
    $f=Fixture 'own';Put (Join-Path $f.Target 'custom-user-file.txt') 'keep me'
    Install $f | Out-Null
    Assert ((Get-Content -LiteralPath (Join-Path $f.Target 'custom-user-file.txt') -Raw) -eq 'keep me') 'Unmanaged file changed.'
    $old=@(Get-ChildItem -LiteralPath $f.Backup -Filter 'AltiumRevisionRetirement.dll' -Recurse)
    Assert ($old.Count -eq 1 -and (Get-Content -LiteralPath $old[0].FullName -Raw) -eq 'old installed DLL') 'Previous runtime was not backed up.'
}
Test 'repeated installation remains idempotent without duplicate registrations' {
    $f=Fixture;Install $f | Out-Null;Install $f | Out-Null
    [xml]$after=Get-Content -LiteralPath $f.Registry -Raw
    Assert (@($after.Extensions.Item | Where-Object {$_.HRID -eq 'AltiumRevisionRetirement'}).Count -eq 1) 'Repeated installation added a duplicate.'
}
Test 'open Designer blocks runtime and registry writes' {
    $f=Fixture 'own';$before=(Get-FileHash -LiteralPath $f.Registry).Hash;$state.DesignerRunning=$true
    try {Reject {Install $f} 'Close Altium Designer'} finally {$state.DesignerRunning=$false}
    Unchanged $f $before
    Assert ((Get-Content -LiteralPath (Join-Path $f.Target 'AltiumRevisionRetirement.dll') -Raw) -eq 'old installed DLL') 'Loaded runtime was overwritten.'
}
Test 'missing dependency names the library and Altium Developer without changing installation' {
    $f=Fixture;$before=(Get-FileHash -LiteralPath $f.Registry).Hash
    Remove-Item -LiteralPath (Join-Path $f.Sdk 'CSharp\Altium.SDK.dll')
    Reject {Install $f} 'Missing runtime library: Altium.SDK.dll.*Install Altium Developer'
    Unchanged $f $before
    Assert (-not (Test-Path -LiteralPath $f.Target)) 'Missing dependency partially installed the extension.'
}
Test 'incompatible local libraries block installation before any target write' {
    $f=Fixture;$before=(Get-FileHash -LiteralPath $f.Registry).Hash
    Put (Join-Path (Split-Path -Parent $f.Sdk) 'Altium.SDK.Interfaces.dll') 'incompatible .NET library'
    Reject {Install $f} 'Incompatible runtime library: Altium.SDK.Interfaces.dll'
    Unchanged $f $before
    Assert (-not (Test-Path -LiteralPath $f.Target)) 'Incompatible library partially installed the extension.'
}
Test 'corrupt payload is rejected before registration or backup writes' {
    $f=Fixture;$before=(Get-FileHash -LiteralPath $f.Registry).Hash
    Put (Join-Path $f.Root 'payload\AltiumRevisionRetirement.dll') 'corrupt'
    Reject {Install $f} 'Package integrity check failed'
    Unchanged $f $before
    Assert (-not (Test-Path -LiteralPath $f.Backup)) 'Corrupt payload wrote installation backups.'
}
Test 'path traversal in package file names is rejected' {
    $f=Fixture;$f.Manifest.files[0].name='..\outside.dll';SaveManifest $f
    Reject {Install $f} 'file list is incomplete or invalid'
}
Test 'wrong package identity cannot register another extension' {
    $f=Fixture;$f.Manifest.extension.hrid='OtherExtension';SaveManifest $f
    Reject {Install $f} 'Invalid extension package identity'
}
foreach($registration in @('duplicate','mismatch','guid-conflict')) {
    Test ('conflicting '+$registration+' registration blocks installation') {
        $f=Fixture $registration;$before=(Get-FileHash -LiteralPath $f.Registry).Hash
        Reject {Install $f} '(Duplicate extension registrations|different path or GUID)'
        Unchanged $f $before
    }
}
Test 'copy failure restores prior managed files before leaving the registration unchanged' {
    $f=Fixture 'own';$before=(Get-FileHash -LiteralPath $f.Registry).Hash
    $copyState=[pscustomobject]@{Count=0;Target=$f.Target;Backup=$f.Backup}
    $copyMock={param([string]$LiteralPath,[string]$Destination,[switch]$Force)
        if($Destination.StartsWith($copyState.Target+'\') -and -not $LiteralPath.StartsWith($copyState.Backup+'\')) {$copyState.Count++;if($copyState.Count -eq 2) {throw 'Simulated copy failure'}}
        Microsoft.PowerShell.Management\Copy-Item -LiteralPath $LiteralPath -Destination $Destination -Force:$Force
    }.GetNewClosure()
    Set-Item -Path Function:Copy-Item -Value $copyMock
    try {Reject {Install $f} 'Simulated copy failure'} finally {Remove-Item Function:Copy-Item}
    Unchanged $f $before
    Assert ((Get-Content -LiteralPath (Join-Path $f.Target 'AltiumRevisionRetirement.dll') -Raw) -eq 'old installed DLL') 'Prior runtime was not restored.'
    Assert (@(Get-ChildItem -LiteralPath $f.Target -File).Count -eq 1) 'New managed files were not removed during rollback.'
}
Test 'missing native build SDK produces an actionable developer error' {
    $f=Fixture
    Reject {& (Join-Path $project 'scripts\build.ps1') -AltiumSdkRoot (Join-Path $f.Root 'missing SDK')} 'Altium Developer SDK is missing or incomplete'
}
Test 'real native build and EPD generation handle spaces and trailing SDK separators' {
    $f=Fixture
    Copy-Item -LiteralPath (Join-Path $project 'scripts\build.ps1') -Destination (Join-Path $f.Root 'scripts\build.ps1') -Force
    Copy-Item -LiteralPath (Join-Path $project 'AltiumRevisionRetirement.EPD') -Destination $f.Root
    Put (Join-Path $f.Root 'AltiumRevisionRetirement.csproj') '<Project ToolsVersion="4.0" DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003"><PropertyGroup><OutputType>Library</OutputType><AssemblyName>AltiumRevisionRetirement</AssemblyName><TargetFrameworkVersion>v4.0</TargetFrameworkVersion><OutputPath>bin\Release\</OutputPath></PropertyGroup><ItemGroup><Compile Include="Fixture.cs"/></ItemGroup><Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets"/></Project>'
    Put (Join-Path $f.Root 'Fixture.cs') 'public sealed class InstallerBuildFixture {}'
    Put (Join-Path $f.Sdk 'SDK.ticket') 'Unused test ticket'
    $log=@(& (Join-Path $f.Root 'scripts\build.ps1') -AltiumSdkRoot ($f.Sdk+'\'))
    Assert (Test-Path -LiteralPath (Join-Path $f.Root 'bin\Release\AltiumRevisionRetirement.dll')) ('Native path arguments failed: '+($log -join "`n"))
    [xml]$local=Get-Content -LiteralPath (Join-Path $f.Root 'AltiumRevisionRetirement.local.EPD') -Raw
    Assert ($local.Main.ExtensionOptions.ExtensionSourceLocation -eq $f.Root) 'EPD source path is incorrect.'
    Assert ($local.Main.ExtensionVersionOptions.ExtensionTopLevelFolder -eq $f.Target) 'EPD destination is incorrect.'
}
Test 'source-only packaging does not invoke the native compiler or include local SDK files' {
    $f=Fixture
    foreach($name in @('README.md','README.fr.md','CHANGELOG.md','.gitignore','.gitattributes','AltiumRevisionRetirement.csproj')) {Put (Join-Path $f.Root $name) 'source fixture'}
    foreach($directory in @('src','Installation','tests','docs','assets')) {Put (Join-Path $f.Root ($directory+'\fixture.txt')) 'source fixture'}
    Copy-Item -LiteralPath (Join-Path $project 'AltiumRevisionRetirement.EPD') -Destination $f.Root
    Copy-Item -LiteralPath (Join-Path $project 'scripts\package.ps1') -Destination (Join-Path $f.Root 'scripts\package.ps1')
    Put (Join-Path $f.Root 'scripts\test.ps1') "Write-Output 'Fixture tests passed.'"
    Put (Join-Path $f.Root 'bin\Release\Altium.SDK.dll') 'excluded SDK'
    Put (Join-Path $f.Root 'SDK.ticket') 'excluded ticket'
    Put (Join-Path $f.Root 'AltiumRevisionRetirement.local.EPD') 'excluded local EPD'
    & (Join-Path $f.Root 'scripts\package.ps1') -SourceOnly | Out-Null
    $zip=@(Get-ChildItem -LiteralPath (Join-Path $f.Root 'dist') -Filter '*-source.zip');Assert ($zip.Count -eq 1) 'Source archive missing.'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive=[IO.Compression.ZipFile]::OpenRead($zip[0].FullName)
    try {Assert (@($archive.Entries | Where-Object {$_.FullName -match '(\.dll$|SDK\.ticket$|\.local\.EPD$)'}).Count -eq 0) 'Generated or licensed files leaked into source ZIP.'} finally {$archive.Dispose()}
}
Write-Output ($state.Passed.ToString()+' installer and packaging tests passed; no real Altium installation changed.')
