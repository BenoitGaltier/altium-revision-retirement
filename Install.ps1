[CmdletBinding()]
param([switch]$CheckOnly,[string]$AltiumApplication,[string]$AltiumExtensionsPath,[string]$BackupRoot)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'scripts\install.ps1') @PSBoundParameters
