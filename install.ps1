# Registers the CAD Master add-in with SOLIDWORKS (needs one Windows admin prompt).
# Run build.ps1 first. Afterwards SOLIDWORKS loads CAD Master at start-up
# (Tools > Add-Ins lists it as "CAD Master").
$ErrorActionPreference = 'Stop'
$dll = Join-Path $PSScriptRoot 'bin\CADMaster.dll'
if (-not (Test-Path $dll)) { throw "Build first: $dll not found (run build.ps1)." }

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) {
    Write-Host 'Asking Windows for administrator rights to register the add-in...'
    $p = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`""
    exit $p.ExitCode
}

$regasm = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
& $regasm /codebase /nologo $dll
if ($LASTEXITCODE -ne 0) { Write-Host 'RegAsm failed.'; Start-Sleep 8; exit 1 }
$key = 'HKLM:\SOFTWARE\SolidWorks\AddIns\{C4D7A1E2-5B39-4F6A-9E1D-2A8B7C3F5D10}'
if (Test-Path $key) { Write-Host 'CAD Master is registered. Restart SOLIDWORKS, or tick it in Tools > Add-Ins.' }
else { Write-Host 'Registration did not create the SOLIDWORKS add-in key.'; Start-Sleep 8; exit 1 }
Start-Sleep 3
