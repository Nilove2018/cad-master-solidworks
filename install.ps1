# Developer install: registers the build in .\bin with SOLIDWORKS, for the current user.
# (End users run CADMaster-Setup.exe instead.) Run build.ps1 first. Restart SOLIDWORKS afterwards.
#
# Windows asks for administrator approval once, only to add CAD Master to SOLIDWORKS' add-in list
# (HKLM). Everything else is per user. Running the public installer later points it at the
# installed version; run this script again to switch back to your build.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'tools\registration.ps1')
$bin = Join-Path $PSScriptRoot 'bin'

Register-CADMasterUser $bin
Write-Host "Registered for $env:USERNAME -> $bin"
if (Test-CADMasterMachineKey) { Write-Host 'SOLIDWORKS add-in entry already present.' }
else {
    Write-Host 'Asking Windows for administrator approval to add CAD Master to SOLIDWORKS...'
    Set-CADMasterMachineKey (Join-Path $bin 'icons\addin_20.png')
    Write-Host 'SOLIDWORKS add-in entry created.'
}
Write-Host 'Restart SOLIDWORKS to load this build.'
