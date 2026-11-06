[CmdletBinding()]
param([switch]$SourceOnly,[switch]$SkipBuild)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'test.ps1')
$output=Join-Path $project 'dist'
New-Item -ItemType Directory -Path $output -Force | Out-Null
# Source distribution: Altium SDK assemblies and SDK ticket are intentionally excluded.
$files=@('AltiumRevisionRetirement.csproj','AltiumRevisionRetirement.EPD','Install.ps1','README.md','README.fr.md','CHANGELOG.md','.gitignore','.gitattributes','src','Installation','scripts','tests','docs','assets') | ForEach-Object {Join-Path $project $_}
[xml]$epd=Get-Content -LiteralPath (Join-Path $project 'AltiumRevisionRetirement.EPD') -Raw
$version=([version][string]$epd.Main.ExtensionVersionOptions.ExtensionVersion).ToString(3)
$archive=Join-Path $output "altium-revision-retirement-$version-source.zip"
Compress-Archive -LiteralPath $files -DestinationPath $archive -Force
$hash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path -Leaf $archive)" | Set-Content -LiteralPath "$archive.sha256" -Encoding ascii
Write-Output $archive
if($SourceOnly) {Write-Output 'Source ZIP created without a native SDK build. End users should download the compiled windows-x64 release asset.';return}
if(-not $SkipBuild) {& (Join-Path $PSScriptRoot 'build.ps1')}
$buildOutput=Join-Path $project 'bin\Release'
$dll=Join-Path $buildOutput 'AltiumRevisionRetirement.dll'
if(-not (Test-Path -LiteralPath $dll) -or [Diagnostics.FileVersionInfo]::GetVersionInfo($dll).FileVersion -ne [string]$epd.Main.ExtensionVersionOptions.ExtensionVersion) {throw 'The compiled DLL is missing or has a different version. Run scripts/build.ps1 before packaging.'}
$stage=Join-Path $output ('staging\'+[Guid]::NewGuid().ToString('N'))
$payload=Join-Path $stage 'payload'
[IO.Directory]::CreateDirectory($payload) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $stage 'scripts')) | Out-Null
Copy-Item -LiteralPath (Join-Path $project 'Install.ps1') -Destination $stage
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install.ps1') -Destination (Join-Path $stage 'scripts\install.ps1')
Copy-Item -LiteralPath (Join-Path $project 'docs\INSTALLATION.md') -Destination (Join-Path $stage 'README.md')
$sources=@{
    'AltiumRevisionRetirement.dll'=$dll
    'AltiumRevisionRetirement.ins'=(Join-Path $buildOutput 'AltiumRevisionRetirement.ins')
    'AltiumRevisionRetirement.rcs'=(Join-Path $buildOutput 'AltiumRevisionRetirement.rcs')
    'fluidra-extension-small.png'=(Join-Path $project 'assets\fluidra-extension-small.png')
    'fluidra-extension-large.png'=(Join-Path $project 'assets\fluidra-extension-large.png')
    'USER_GUIDE.en.md'=(Join-Path $project 'docs\USER_GUIDE.md')
    'Altium.SDK.dll'=(Join-Path $buildOutput 'Altium.SDK.dll')
    'Altium.SDK.Interfaces.dll'=(Join-Path $buildOutput 'Altium.SDK.Interfaces.dll')
    'THIRD_PARTY_NOTICES.md'=(Join-Path $project 'docs\THIRD_PARTY_NOTICES.md')
}
$payloadFiles=@(foreach($name in ($sources.Keys | Sort-Object)) {
    Copy-Item -LiteralPath $sources[$name] -Destination (Join-Path $payload $name)
    [ordered]@{name=$name;sha256=(Get-FileHash -LiteralPath (Join-Path $payload $name) -Algorithm SHA256).Hash.ToLowerInvariant()}
})
$dependencies=@(foreach($name in @('Altium.SDK.dll','Altium.SDK.Interfaces.dll')) {
    $path=Join-Path $buildOutput $name
    if(-not (Test-Path -LiteralPath $path)) {throw "Missing build-time reference for dependency metadata: $name"}
    [ordered]@{name=$name;assemblyIdentity=[Reflection.AssemblyName]::GetAssemblyName($path).FullName;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
})
$manifest=[ordered]@{
    schemaVersion=2
    extension=[ordered]@{
        hrid='AltiumRevisionRetirement';guid='f19cf220-6b04-4d43-a356-eebe90b392e6'
        version=[string]$epd.Main.ExtensionVersionOptions.ExtensionVersion
        title=[string]$epd.Main.ExtensionVersionOptions.ExtensionTitle
        publisher=[string]$epd.Main.ExtensionVersionOptions.ExtensionPublisherName
        shortDescription=[string]$epd.Main.ExtensionVersionOptions.ExtensionShortDescription
        longDescription=[string]$epd.Main.ExtensionVersionOptions.ExtensionLongDescription
        help=[string]$epd.Main.ExtensionVersionOptions.ExtensionHelp
    }
    platform='Windows x64 / .NET Framework 4.8'
    testedDesignerBuild='26.11.1.28'
    files=$payloadFiles;dependencies=$dependencies
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $stage 'manifest.json') -Encoding UTF8
$binaryArchive=Join-Path $output "altium-revision-retirement-$version-windows-x64.zip"
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $binaryArchive -Force
$binaryHash=(Get-FileHash -LiteralPath $binaryArchive -Algorithm SHA256).Hash.ToLowerInvariant()
"$binaryHash  $(Split-Path -Leaf $binaryArchive)" | Set-Content -LiteralPath "$binaryArchive.sha256" -Encoding ascii
Write-Output $binaryArchive
Write-Output 'Compiled release ready with the two Altium runtime DLLs. No Altium Developer installation or compiler is needed on the target PC. SDK sources, license files and tickets are excluded. Redistribution remains subject to the applicable Altium agreement.'
