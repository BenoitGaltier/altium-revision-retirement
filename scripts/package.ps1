[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'test.ps1')
& (Join-Path $PSScriptRoot 'build.ps1')
$output=Join-Path $project 'dist'
New-Item -ItemType Directory -Path $output -Force | Out-Null
# Source distribution: Altium SDK assemblies and SDK ticket are intentionally excluded.
$files=@('AltiumRevisionRetirement.csproj','AltiumRevisionRetirement.EPD','README.md','README.fr.md','CHANGELOG.md','.gitignore','.gitattributes','src','Installation','scripts','tests','docs','assets') | ForEach-Object {Join-Path $project $_}
$archive=Join-Path $output 'altium-revision-retirement-0.1.1-source.zip'
Compress-Archive -LiteralPath $files -DestinationPath $archive -Force
$hash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path -Leaf $archive)" | Set-Content -LiteralPath "$archive.sha256" -Encoding ascii
Write-Output $archive
