# Removes the CAD Master add-in registration (needs one Windows admin prompt).
# Close SOLIDWORKS first. Your settings and logs in %LOCALAPPDATA%\CADMaster are kept.
$ErrorActionPreference = 'Stop'
$dll = Join-Path $PSScriptRoot 'bin\CADMaster.dll'

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) {
    $p = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`""
    exit $p.ExitCode
}

& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe" /unregister /nologo $dll
$guid = '{C4D7A1E2-5B39-4F6A-9E1D-2A8B7C3F5D10}'
Remove-Item "HKLM:\SOFTWARE\SolidWorks\AddIns\$guid" -Recurse -ErrorAction SilentlyContinue
Remove-Item "HKCU:\Software\SolidWorks\AddInsStartup\$guid" -Recurse -ErrorAction SilentlyContinue
Write-Host 'CAD Master is unregistered.'
Start-Sleep 3
