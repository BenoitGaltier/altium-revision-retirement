[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$NativeSdkAssembly)
$ErrorActionPreference='Stop'
# Developer-only contract verification: inspect a local licensed vendor constant without running its code.
# This script is not invoked by the installer and the referenced assembly is not added to the package.
if($PSVersionTable.PSVersion.Major -lt 7) {throw 'Run this developer-only metadata check with PowerShell 7.'}
$path=[IO.Path]::GetFullPath($NativeSdkAssembly)
if(-not (Test-Path -LiteralPath $path -PathType Leaf)) {throw 'Select the installed native DXPServerSDK.dll.'}
$assembly=[Reflection.Assembly]::LoadFrom($path)
$type=$assembly.GetType('Altium.Sdk.DxpAppServer.Common.Constants+VaultConstants+AclOperations',$true)
$field=$type.GetField('LifecycleStateChangeOperationGuid',[Reflection.BindingFlags]'Public,NonPublic,Static')
if(-not $field -or -not $field.IsLiteral) {throw 'The installed SDK does not expose the expected constant contract.'}
$vendor=[Guid]$field.GetRawConstantValue()
$project=Split-Path -Parent $PSScriptRoot
$source=[IO.File]::ReadAllText((Join-Path $project 'src\WorkspacePermissions.cs'))
$match=[regex]::Match($source,'public const string LifecycleOperationGuid = "([A-Fa-f0-9-]+)";')
if(-not $match.Success -or $vendor -ne [Guid]$match.Groups[1].Value) {throw 'The extension operation GUID does not match the installed vendor SDK constant.'}
Write-Output ('PASS native vendor constant matches the extension: '+$vendor.ToString('D')+'; '+$assembly.GetName().Name+' '+$assembly.GetName().Version)
