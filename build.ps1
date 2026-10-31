# Builds CAD Master into .\bin (add-in DLL, test host, UI, plugin, icons, python helper).
# Needs no Visual Studio: uses the Roslyn compiler in tools\roslyn and the .NET Framework 4.8 assemblies
# (tools\fetch-deps.ps1 downloads the compiler and the WebView2 SDK on first build).
# Needs SOLIDWORKS installed: the interop DLLs come from its api\redist folder.
# SOLIDWORKS must not have the add-in loaded while building (it locks bin\CADMaster.dll).
param([string]$SolidWorksDir)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

if (-not $SolidWorksDir) {
    # Newest installed SOLIDWORKS from the registry, e.g. HKLM\SOFTWARE\SolidWorks\SOLIDWORKS 2025\Setup
    $SolidWorksDir = Get-ChildItem 'HKLM:\SOFTWARE\SolidWorks' -ErrorAction SilentlyContinue |
        Where-Object { $_.PSChildName -match '^SOLIDWORKS \d{4}$' } | Sort-Object PSChildName -Descending |
        ForEach-Object { (Get-ItemProperty (Join-Path $_.PSPath 'Setup') -ErrorAction SilentlyContinue).'SolidWorks Folder' } |
        Where-Object { $_ -and (Test-Path (Join-Path $_ 'api\redist\SolidWorks.Interop.sldworks.dll')) } | Select-Object -First 1
    if (-not $SolidWorksDir) { throw 'SOLIDWORKS not found. Pass -SolidWorksDir "<install folder>".' }
}
Write-Host "SOLIDWORKS: $SolidWorksDir"
& (Join-Path $root 'tools\fetch-deps.ps1')
$bin = Join-Path $root 'bin'
$fx = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$csc = Join-Path $root 'tools\roslyn\csc.exe'
$redist = Join-Path $SolidWorksDir 'api\redist'

New-Item -ItemType Directory -Force $bin | Out-Null
$dll = Join-Path $bin 'CADMaster.dll'
if (Test-Path $dll) {
    try { [IO.File]::Open($dll, 'Open', 'ReadWrite', 'None').Close() }
    catch { throw "bin\CADMaster.dll is in use. Close SOLIDWORKS (or the test host) and build again." }
}

$interop = 'SolidWorks.Interop.sldworks.dll', 'SolidWorks.Interop.swconst.dll', 'SolidWorks.Interop.swpublished.dll'
foreach ($i in $interop) { Copy-Item (Join-Path $redist $i) $bin -Force }
Copy-Item (Join-Path $root 'lib\webview2\*') $bin -Force

$common = @('/nologo', '/langversion:latest', '/platform:x64', '/optimize+', '/debug:portable', '/nowarn:1701,1702',
    "/reference:$fx\System.dll", "/reference:$fx\System.Core.dll", "/reference:$fx\System.Drawing.dll",
    "/reference:$fx\System.Windows.Forms.dll", "/reference:$fx\System.Web.Extensions.dll",
    "/reference:$bin\Microsoft.Web.WebView2.Core.dll", "/reference:$bin\Microsoft.Web.WebView2.WinForms.dll")
$common += $interop | ForEach-Object { "/reference:$bin\$_" }

Write-Host 'Compiling CADMaster.dll'
$src = Get-ChildItem (Join-Path $root 'src\CADMaster') -Recurse -Filter *.cs | ForEach-Object { $_.FullName }
& $csc @common /target:library "/out:$dll" @src
if ($LASTEXITCODE -ne 0) { throw 'CADMaster.dll failed to compile' }

Write-Host 'Compiling CADMaster.TestHost.exe'
$testHostSrc = @(Get-ChildItem (Join-Path $root 'src\TestHost') -Recurse -Filter *.cs | ForEach-Object { $_.FullName })
& $csc @common /target:winexe "/reference:$dll" "/out:$bin\CADMaster.TestHost.exe" @testHostSrc
if ($LASTEXITCODE -ne 0) { throw 'TestHost failed to compile' }

foreach ($d in 'ui', 'plugin', 'icons', 'python') {
    $from = Join-Path $root $d
    if (Test-Path $from) {
        $to = Join-Path $bin $d
        if (Test-Path $to) { Remove-Item $to -Recurse -Force }
        Copy-Item $from $to -Recurse -Force
    }
}
Write-Host "Built into $bin"
