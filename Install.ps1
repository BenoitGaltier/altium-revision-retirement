[CmdletBinding()]
param([switch]$CheckOnly,[string]$AltiumApplication,[string]$AltiumExtensionsPath,[string]$AltiumSdkRoot,[string]$BackupRoot)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'scripts\install.ps1') @PSBoundParameters
