# Downloads the build dependencies that are not stored in the repo:
#   - the C# compiler (Roslyn, NuGet Microsoft.Net.Compilers.Toolset) into tools\roslyn
#   - the WebView2 SDK (NuGet Microsoft.Web.WebView2) into lib\webview2
# build.ps1 runs this automatically when something is missing. Safe to run again.
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$RoslynVersion = '5.9.0'
$WebView2Version = '1.0.4258.31'

function Get-NuGetPackage([string]$id, [string]$version) {
    $cache = Join-Path $env:TEMP "cadmaster-deps\$id.$version"
    if (-not (Test-Path $cache)) {
        $url = "https://api.nuget.org/v3-flatcontainer/$($id.ToLower())/$version/$($id.ToLower()).$version.nupkg"
        Write-Host "Downloading $id $version"
        New-Item -ItemType Directory -Force $cache | Out-Null
        $zip = "$cache.zip"
        Invoke-WebRequest $url -OutFile $zip -UseBasicParsing
        Expand-Archive $zip $cache -Force
        Remove-Item $zip
    }
    return $cache
}

$roslyn = Join-Path $root 'tools\roslyn'
if ($Force -or -not (Test-Path (Join-Path $roslyn 'csc.exe'))) {
    $pkg = Get-NuGetPackage 'Microsoft.Net.Compilers.Toolset' $RoslynVersion
    if (Test-Path $roslyn) { Remove-Item $roslyn -Recurse -Force }
    Copy-Item (Join-Path $pkg 'tasks\net472') $roslyn -Recurse
    Write-Host "Roslyn $RoslynVersion -> tools\roslyn"
}

$wv = Join-Path $root 'lib\webview2'
if ($Force -or -not (Test-Path (Join-Path $wv 'Microsoft.Web.WebView2.WinForms.dll'))) {
    $pkg = Get-NuGetPackage 'Microsoft.Web.WebView2' $WebView2Version
    New-Item -ItemType Directory -Force $wv | Out-Null
    Copy-Item (Join-Path $pkg 'lib\net462\Microsoft.Web.WebView2.Core.dll') $wv -Force
    Copy-Item (Join-Path $pkg 'lib\net462\Microsoft.Web.WebView2.WinForms.dll') $wv -Force
    Copy-Item (Join-Path $pkg 'runtimes\win-x64\native\WebView2Loader.dll') $wv -Force
    Write-Host "WebView2 SDK $WebView2Version -> lib\webview2"
}
