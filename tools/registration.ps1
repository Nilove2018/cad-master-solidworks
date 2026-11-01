# Registration helpers shared by install.ps1 / uninstall.ps1. installer\CADMaster.iss writes the same keys.
#
# Tested 2026-11-01 on SOLIDWORKS 2025 SP1.2:
#   - SOLIDWORKS lists add-ins only from HKLM\SOFTWARE\SolidWorks\AddIns, so that one key needs admin once.
#   - The COM class can live per user (HKCU\Software\Classes\CLSID). HKCU overrides HKLM, including the
#     InprocServer32\<version> subkey that .NET reads the CodeBase from. Updates therefore only rewrite
#     HKCU and never need admin.

$Script:Guid = '{C4D7A1E2-5B39-4F6A-9E1D-2A8B7C3F5D10}'
$Script:ProgId = 'CADMaster.SwAddin'
$Script:AssemblyName = 'CADMaster, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null'
$Script:Title = 'CAD Master'
$Script:Description = 'Claude Code inside SOLIDWORKS: plan, design and automate with Claude'

# Point the per-user COM class at <AppDir>\CADMaster.dll and load CAD Master at SOLIDWORKS start-up.
function Register-CADMasterUser([string]$AppDir) {
    $dll = Join-Path $AppDir 'CADMaster.dll'
    if (-not (Test-Path $dll)) { throw "Not found: $dll" }
    $codeBase = 'file:///' + ((Resolve-Path $dll).Path -replace '\\', '/')
    $cls = "HKCU:\Software\Classes\CLSID\$Guid"
    $ip = "$cls\InprocServer32"
    foreach ($k in $ip, "$ip\1.0.0.0") {
        New-Item $k -Force | Out-Null
        foreach ($kv in @(@('Class', $ProgId), @('Assembly', $AssemblyName), @('RuntimeVersion', 'v4.0.30319'), @('CodeBase', $codeBase))) {
            New-ItemProperty $k -Name $kv[0] -Value $kv[1] -Force | Out-Null
        }
    }
    Set-Item $cls -Value $ProgId
    Set-Item $ip -Value 'mscoree.dll'
    New-ItemProperty $ip -Name ThreadingModel -Value Both -Force | Out-Null
    New-Item "$cls\ProgId" -Force -Value $ProgId | Out-Null
    New-Item "$cls\Implemented Categories\{62C8FE65-4EBB-45e7-B440-6E39B2CDBF29}" -Force | Out-Null
    New-Item "HKCU:\Software\Classes\$ProgId\CLSID" -Force -Value $Guid | Out-Null
    $start = "HKCU:\Software\SolidWorks\AddInsStartup\$Guid"
    New-Item $start -Force | Out-Null
    Set-ItemProperty $start -Name '(default)' -Value 1 -Type DWord
}

function Unregister-CADMasterUser {
    foreach ($k in "HKCU:\Software\Classes\CLSID\$Guid", "HKCU:\Software\Classes\$ProgId", "HKCU:\Software\SolidWorks\AddInsStartup\$Guid") {
        Remove-Item $k -Recurse -ErrorAction SilentlyContinue
    }
}

function Test-CADMasterMachineKey { Test-Path "HKLM:\SOFTWARE\SolidWorks\AddIns\$Guid" }

# Writes (or with -Remove deletes) the HKLM add-in key through one elevated regedit call.
# -Remove also deletes a machine-wide COM class left by older RegAsm-based installs.
function Set-CADMasterMachineKey([string]$IconPath, [switch]$Remove) {
    $esc = { param($s) $s -replace '\\', '\\' -replace '"', '\"' }
    $lines = @('Windows Registry Editor Version 5.00', '')
    if ($Remove) {
        $lines += "[-HKEY_LOCAL_MACHINE\SOFTWARE\SolidWorks\AddIns\$Guid]"
        $lines += "[-HKEY_LOCAL_MACHINE\SOFTWARE\Classes\CLSID\$Guid]"
        $lines += "[-HKEY_LOCAL_MACHINE\SOFTWARE\Classes\$ProgId]"
    } else {
        $lines += "[HKEY_LOCAL_MACHINE\SOFTWARE\SolidWorks\AddIns\$Guid]"
        $lines += '@=dword:00000000'
        $lines += '"Title"="' + (& $esc $Title) + '"'
        $lines += '"Description"="' + (& $esc $Description) + '"'
        $lines += '"Icon Path"="' + (& $esc $IconPath) + '"'
    }
    $reg = Join-Path $env:TEMP "cadmaster-machine-$PID.reg"
    $lines -join "`r`n" | Set-Content $reg -Encoding Unicode
    try {
        $p = Start-Process regedit.exe -ArgumentList '/s', "`"$reg`"" -Verb RunAs -Wait -PassThru
        if ($p.ExitCode -ne 0) { throw "regedit exited with $($p.ExitCode)" }
    } catch {
        throw "Windows administrator approval is needed once, so SOLIDWORKS lists CAD Master. ($($_.Exception.Message))"
    } finally { Remove-Item $reg -ErrorAction SilentlyContinue }
}
