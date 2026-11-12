#requires -Version 7.6
[CmdletBinding()]
param(
    [string]$RimWorldDir = '',
    [string]$HarmonyDll = '',
    [string]$BubblesDll = ''
)
$ErrorActionPreference = 'Stop'
if (!$IsWindows) { throw 'Run this installer on the Windows PC with RimWorld installed.' }
if ([Environment]::Version.Major -lt 10) { throw 'PowerShell 7.6+ running on .NET 10 is required.' }
function Assert-GameClosed {
    if (Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like 'RimWorld*' }) {
        throw 'Close RimWorld before installing. No running process will be stopped automatically.'
    }
}
Assert-GameClosed
$steamRoots = [Collections.Generic.List[string]]::new()
foreach ($key in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam')) {
    $entry = Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue
    if ($entry) {
        foreach ($name in @('SteamPath','InstallPath')) {
            $property = $entry.PSObject.Properties[$name]
            if ($property -and $property.Value) { $steamRoots.Add([string]$property.Value) }
        }
    }
}
if (${env:ProgramFiles(x86)}) { $steamRoots.Add((Join-Path ${env:ProgramFiles(x86)} 'Steam')) }
foreach ($root in @($steamRoots.ToArray())) {
    $vdf = Join-Path $root 'steamapps/libraryfolders.vdf'
    if (Test-Path -LiteralPath $vdf) {
        foreach ($match in [regex]::Matches((Get-Content -LiteralPath $vdf -Raw), '"path"\s+"([^"]+)"')) {
            $steamRoots.Add($match.Groups[1].Value.Replace('\\','\'))
        }
    }
}
$steamRoots = @($steamRoots | Select-Object -Unique)
if (!$RimWorldDir) {
    $games = @($steamRoots | ForEach-Object { Join-Path $_ 'steamapps/common/RimWorld' } | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'Assembly-CSharp.dll') })
    if ($games.Count -ne 1) { throw 'Could not identify exactly one RimWorld installation. Run with -RimWorldDir "D:\path\to\RimWorld".' }
    $RimWorldDir = $games[0]
}
$RimWorldDir = (Resolve-Path -LiteralPath $RimWorldDir).Path
if (!(Test-Path -LiteralPath (Join-Path $RimWorldDir 'Assembly-CSharp.dll'))) { throw 'This folder does not contain the RimWorld game assembly.' }
$version = (Get-Content -LiteralPath (Join-Path $RimWorldDir 'Version.txt') -Raw).Trim()
if ($version -notmatch '^1\.7(?:\.|\s|$)') { throw "This mod targets RimWorld 1.7; found $version." }
function Find-Dependency([string]$id, [string]$name) {
    $paths = @($steamRoots | ForEach-Object { Join-Path $_ "steamapps/workshop/content/294100/$id/1.7/Assemblies/$name" } | Where-Object { Test-Path -LiteralPath $_ })
    if ($paths.Count -ne 1) { throw "Could not identify exactly one $name. Supply -HarmonyDll and -BubblesDll explicitly for your installed mods." }
    return $paths[0]
}
if (!$HarmonyDll) { $HarmonyDll = Find-Dependency '2009463077' '0Harmony.dll' }
if (!$BubblesDll) { $BubblesDll = Find-Dependency '3830600373' 'Bubbles.dll' }
$HarmonyDll = (Resolve-Path -LiteralPath $HarmonyDll).Path
$BubblesDll = (Resolve-Path -LiteralPath $BubblesDll).Path
$target = Join-Path $RimWorldDir 'Mods/SpeakYourMind'
$source = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\','/')
$targetFull = [IO.Path]::GetFullPath($target).TrimEnd('\','/')
if ($source.Equals($targetFull, [StringComparison]::OrdinalIgnoreCase) -or $source.StartsWith($targetFull + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Keep this source checkout outside Mods/SpeakYourMind so the installer can replace that folder safely.'
}
if (Test-Path -LiteralPath $target) {
    $about = Join-Path $target 'About/About.xml'
    if (!(Test-Path -LiteralPath $about) -or ([xml](Get-Content -LiteralPath $about -Raw)).ModMetaData.packageId -ne 'aydhe.speakyourmind') {
        throw 'The target folder is not identifiable as SpeakYourMind. It has not been changed.'
    }
}
# Avoid installing a second copy under a different mod folder name.
$modsFolder = Join-Path $RimWorldDir 'Mods'
if (Test-Path -LiteralPath $modsFolder) {
    foreach ($folder in Get-ChildItem -LiteralPath $modsFolder -Directory) {
        if ($folder.FullName.Equals($targetFull, [StringComparison]::OrdinalIgnoreCase)) { continue }
        $about = Join-Path $folder.FullName 'About/About.xml'
        if (Test-Path -LiteralPath $about) {
            try { $id = ([xml](Get-Content -LiteralPath $about -Raw)).ModMetaData.packageId } catch { continue }
            if ($id -eq 'aydhe.speakyourmind') { throw "Another SpeakYourMind copy exists at $($folder.FullName). Move it outside Mods before installing to avoid duplicate package IDs." }
        }
    }
}
# Build/test/package first. Any error leaves the installed mod untouched.
& (Join-Path $PSScriptRoot 'build.ps1') -RimWorldDir $RimWorldDir -HarmonyDll $HarmonyDll -Tests
& (Join-Path $PSScriptRoot 'validate-integration.ps1') -RimWorldDir $RimWorldDir -HarmonyDll $HarmonyDll -BubblesDll $BubblesDll
& (Join-Path $PSScriptRoot 'build.ps1') -RimWorldDir $RimWorldDir -HarmonyDll $HarmonyDll
$archive = Join-Path $PSScriptRoot 'artifacts/SpeakYourMind-install.zip'
& (Join-Path $PSScriptRoot 'package.ps1') -Destination $archive
$backupRoot = Join-Path $RimWorldDir 'ModBackups/SpeakYourMind'
$stamp = [DateTime]::Now.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
$stage = Join-Path $backupRoot ('stage-' + $stamp)
Expand-Archive -LiteralPath $archive -DestinationPath $stage
$stagedMod = Join-Path $stage 'SpeakYourMind'
$manifest = Get-Content -LiteralPath (Join-Path $stagedMod '1.7/Assemblies/SpeakYourMind.build.json') -Raw | ConvertFrom-Json
Assert-GameClosed
. (Join-Path $PSScriptRoot 'scripts/Install-Package.ps1')
$result = Install-SYMPackage -StagedMod $stagedMod -Target $target -BackupRoot $backupRoot -ExpectedHash $manifest.binarySha256
"Installed SpeakYourMind $($manifest.version) at $($result.Target)"
if ($result.Backup) { "Previous version preserved at $($result.Backup)" }
"Verified DLL SHA256: $($result.Hash)"
'Launch RimWorld with Harmony, Interaction Bubbles Expanded and SpeakYourMind enabled. Use Voices in the bottom menu. Saves, API keys and mod settings were not changed.'
