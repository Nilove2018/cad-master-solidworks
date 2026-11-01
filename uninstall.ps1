# Developer uninstall: removes CAD Master's registration (per user, plus the SOLIDWORKS add-in entry,
# which needs administrator approval once). Close SOLIDWORKS first.
# Your settings, logs and chats in %LOCALAPPDATA%\CADMaster are kept.
# Note: this also unregisters an installed CAD Master (CADMaster-Setup.exe) for this user.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'tools\registration.ps1')

Unregister-CADMasterUser
Write-Host "Removed the registration for $env:USERNAME."
Write-Host 'Asking Windows for administrator approval to remove CAD Master from SOLIDWORKS...'
Set-CADMasterMachineKey -Remove
Write-Host 'CAD Master is unregistered.'
