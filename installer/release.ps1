# Builds a CAD Master release from the VERSION file:
#   build\release\CADMaster-Setup-<v>.exe   the installer, for new users
#   build\release\CADMaster-<v>.zip         the app folder, for the in-app updater
#   build\release\update.json               version, notes, file names, sizes and SHA-256 (the updater reads it)
# Release notes come from the "## <v>" section of CHANGELOG.md, which must exist.
#
#   .\installer\release.ps1             build everything locally
#   .\installer\release.ps1 -Publish    also create the GitHub release (as a draft; publish it on GitHub)
#   -Critical                           mark the update as an important fix (red bar in the app)
param([switch]$Publish, [switch]$Critical, [string]$MinSolidworks = '2025')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$version = (Get-Content (Join-Path $root 'VERSION') -TotalCount 1).Trim()
$repo = 'Nilove2018/cad-master-solidworks'

# Release notes: the CHANGELOG section "## <version>" or "## [<version>]", up to the next "## "
$changelog = Get-Content (Join-Path $root 'CHANGELOG.md')
$start = ($changelog | Select-String -Pattern ('^## \[?' + [regex]::Escape($version) + '\]?(\s|$)') | Select-Object -First 1).LineNumber
if (-not $start) { throw "CHANGELOG.md has no '## $version' section. Move the [Unreleased] items into it first." }
$notes = @()
for ($i = $start; $i -lt $changelog.Count -and $changelog[$i] -notmatch '^## '; $i++) { $notes += $changelog[$i] }
$notesText = ($notes -join "`n").Trim()
$highlights = @($notes | Where-Object { $_ -match '^- ' } | ForEach-Object { ($_ -replace '^- ', '').Trim() } | Select-Object -First 6)

# 1. Build
& (Join-Path $root 'build.ps1')
& (Join-Path $root 'tools\fetch-deps.ps1') -Installer

# 2. Stage what ships: everything in bin except the developer test host and debug symbols
$stage = Join-Path $root 'build\stage'
$out = Join-Path $root 'build\release'
# Empty the folders rather than delete them: a shell or Explorer window may have one open
foreach ($d in $stage, $out) { New-Item -ItemType Directory -Force $d | Out-Null; Get-ChildItem $d -Force | Remove-Item -Recurse -Force }
Copy-Item (Join-Path $root 'bin\*') $stage -Recurse
Get-ChildItem $stage -Recurse -File | Where-Object { $_.Name -like 'CADMaster.TestHost*' -or $_.Extension -eq '.pdb' } | Remove-Item
Copy-Item (Join-Path $root 'LICENSE'), (Join-Path $root 'TRADEMARKS.md'), (Join-Path $root 'THIRD-PARTY-NOTICES.md'), (Join-Path $root 'PRIVACY.md') $stage
$dll = Get-Item (Join-Path $stage 'CADMaster.dll')
if ($dll.VersionInfo.ProductVersion -ne $version) { throw "CADMaster.dll says $($dll.VersionInfo.ProductVersion), VERSION says $version" }

# 3. Zip for the updater
$zipName = "CADMaster-$version.zip"
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath (Join-Path $out $zipName) -CompressionLevel Optimal

# 4. Installer
$iscc = Join-Path $root 'tools\innosetup\ISCC.exe'
& $iscc /Q "/DAppVersion=$version" "/DStageDir=$stage" (Join-Path $PSScriptRoot 'CADMaster.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed' }
$setupName = "CADMaster-Setup-$version.exe"

# 5. Manifest for the in-app updater
$file = { param($n) $p = Join-Path $out $n; [ordered]@{ name = $n; size = (Get-Item $p).Length; sha256 = (Get-FileHash $p -Algorithm SHA256).Hash.ToLower() } }
$manifest = [ordered]@{
    version       = $version
    released      = (Get-Date -Format 'yyyy-MM-dd')
    critical      = [bool]$Critical
    minSolidworks = $MinSolidworks
    highlights    = $highlights
    notes         = $notesText
    zip           = & $file $zipName
    setup         = & $file $setupName
}
[IO.File]::WriteAllText((Join-Path $out 'update.json'), ($manifest | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding $false))
Get-ChildItem $out | ForEach-Object { '{0,-32} {1,8:N0} KB' -f $_.Name, ($_.Length / 1KB) }

# 6. GitHub release (draft): review it on GitHub, then press Publish
if ($Publish) {
    $notesFile = Join-Path $out 'notes.md'
    "$notesText`n`n---`nNew here? Download **$setupName** below and run it. Already installed? CAD Master offers this update itself." | Set-Content $notesFile -Encoding UTF8
    $pre = if ($version -match '-' -or $version -like '0.*') { '--prerelease' } else { $null }
    $assets = @((Join-Path $out $setupName), (Join-Path $out $zipName), (Join-Path $out 'update.json'))
    gh release create "v$version" @assets --repo $repo --title "CAD Master $version" --notes-file $notesFile --draft $pre
    if ($LASTEXITCODE -ne 0) { throw 'gh release create failed' }
    Write-Host "Draft release v$version created. Review and publish it at https://github.com/$repo/releases"
}
