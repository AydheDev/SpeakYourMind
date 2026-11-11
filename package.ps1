param([string]$Destination = '')
$ErrorActionPreference = 'Stop'
$binary = Join-Path $PSScriptRoot '1.7/Assemblies/SpeakYourMind.dll'
if (!(Test-Path -LiteralPath $binary) -or (Get-Item -LiteralPath $binary).Length -eq 0) { throw 'Run build.ps1 before packaging.' }
$manifestPath = Join-Path $PSScriptRoot '1.7/Assemblies/SpeakYourMind.build.json'
if (!(Test-Path -LiteralPath $manifestPath)) { throw 'Build manifest missing. Run build.ps1 again.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash -ne $manifest.binarySha256) { throw 'The assembly does not match its build manifest. Rebuild before packaging.' }
foreach ($file in $manifest.files) {
    $inputPath = Join-Path $PSScriptRoot $file.path
    if (!(Test-Path -LiteralPath $inputPath) -or (Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash -ne $file.sha256) { throw "Source changed since compilation: $($file.path). Rebuild before packaging." }
}
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Source') -Recurse -Filter '*.cs' | Where-Object { $_.FullName -notmatch '[\\/]obj[\\/]' }
foreach ($source in $sources) {
    if ([IO.Path]::GetRelativePath($PSScriptRoot, $source.FullName).Replace('\','/') -notin $manifest.files.path) { throw 'New source files have not been compiled. Rebuild before packaging.' }
}
if (!$Destination) { $Destination = Join-Path $PSScriptRoot ("artifacts/SpeakYourMind-" + $manifest.version + '.zip') }
# A fresh staging directory prevents removed/stale files from leaking into a new package.
$stage = Join-Path $PSScriptRoot ('artifacts/package/' + [Guid]::NewGuid().ToString('N') + '/SpeakYourMind')
[IO.Directory]::CreateDirectory($stage) | Out-Null
foreach ($folder in @('About','Defs','docs')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $folder) -Destination $stage -Recurse -Force }
$assemblyFolder = Join-Path $stage '1.7/Assemblies'
[IO.Directory]::CreateDirectory($assemblyFolder) | Out-Null
Copy-Item -LiteralPath $binary, $manifestPath -Destination $assemblyFolder
foreach ($file in @('LoadFolders.xml','README.md')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $stage -Force }
$allowed = @('SpeakYourMind.dll')
foreach ($dll in Get-ChildItem -LiteralPath $stage -Recurse -Filter '*.dll') { if ($dll.Name -notin $allowed) { throw "Unexpected bundled assembly: $($dll.Name)" } }
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Destination))) | Out-Null
Compress-Archive -LiteralPath $stage -DestinationPath $Destination -Force
"Packaged $Destination"
