param([string]$Destination = (Join-Path $PSScriptRoot 'artifacts/SpeakYourMind-0.1.0.zip'))
$ErrorActionPreference = 'Stop'
$binary = Join-Path $PSScriptRoot '1.7/Assemblies/SpeakYourMind.dll'
if (!(Test-Path -LiteralPath $binary) -or (Get-Item -LiteralPath $binary).Length -eq 0) { throw 'Run build.ps1 before packaging.' }
$stage = Join-Path $PSScriptRoot 'dist/SpeakYourMind'
[IO.Directory]::CreateDirectory($stage) | Out-Null
foreach ($folder in @('About','Defs','1.7','docs')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $folder) -Destination $stage -Recurse -Force }
foreach ($file in @('LoadFolders.xml','README.md')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $stage -Force }
$allowed = @('SpeakYourMind.dll')
foreach ($dll in Get-ChildItem -LiteralPath $stage -Recurse -Filter '*.dll') { if ($dll.Name -notin $allowed) { throw "Unexpected bundled assembly: $($dll.Name)" } }
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Destination))) | Out-Null
Compress-Archive -LiteralPath $stage -DestinationPath $Destination -Force
"Packaged $Destination"
