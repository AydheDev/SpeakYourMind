$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../scripts/Install-Package.ps1')
$root = Join-Path ([IO.Path]::GetTempPath()) ('sym-install-test-' + [guid]::NewGuid().ToString('N'))
function New-Fixture([string]$name, [string]$content, [string]$id = 'aydhe.speakyourmind') {
    $path = Join-Path $root $name
    [IO.Directory]::CreateDirectory((Join-Path $path 'About')) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $path '1.7/Assemblies')) | Out-Null
    Set-Content -LiteralPath (Join-Path $path 'About/About.xml') -Value "<ModMetaData><packageId>$id</packageId></ModMetaData>"
    Set-Content -LiteralPath (Join-Path $path '1.7/Assemblies/SpeakYourMind.dll') -Value $content
    return $path
}
$count = 0
function Check([bool]$condition, [string]$message) { if (!$condition) { throw $message }; $script:count++ }
$script:failTarget = ''
function Get-FileHash {
    param([string]$LiteralPath, [string]$Algorithm)
    if ($script:failTarget -and $LiteralPath -eq $script:failTarget) { return [pscustomobject]@{ Hash = 'forced-verification-failure' } }
    Microsoft.PowerShell.Utility\Get-FileHash -LiteralPath $LiteralPath -Algorithm $Algorithm
}
try {
    $target = New-Fixture 'Mods/SpeakYourMind' 'old'
    $stage = New-Fixture 'stage1' 'new'
    $backups = Join-Path $root 'backups'
    $hash = (Get-FileHash -LiteralPath (Join-Path $stage '1.7/Assemblies/SpeakYourMind.dll') -Algorithm SHA256).Hash
    $result = Install-SYMPackage $stage $target $backups $hash
    Check ((Get-Content -LiteralPath (Join-Path $target '1.7/Assemblies/SpeakYourMind.dll') -Raw).Trim() -eq 'new') 'New version installed'
    Check ((Get-Content -LiteralPath (Join-Path $result.Backup '1.7/Assemblies/SpeakYourMind.dll') -Raw).Trim() -eq 'old') 'Old version backed up'
    $stage = New-Fixture 'stage2' 'bad'
    $rejected = $false
    try { Install-SYMPackage $stage $target $backups 'wrong' | Out-Null } catch { $rejected = $true }
    Check $rejected 'Bad staged hash refused'
    Check ((Get-Content -LiteralPath (Join-Path $target '1.7/Assemblies/SpeakYourMind.dll') -Raw).Trim() -eq 'new') 'Bad staging leaves installed version untouched'
    $unrelated = New-Fixture 'unrelated' 'other' 'different.mod'
    $hash = (Get-FileHash -LiteralPath (Join-Path $stage '1.7/Assemblies/SpeakYourMind.dll') -Algorithm SHA256).Hash
    $rejected = $false
    try { Install-SYMPackage $stage $unrelated $backups $hash | Out-Null } catch { $rejected = $true }
    Check $rejected 'Unrelated target refused'
    $script:failTarget = Join-Path $target '1.7/Assemblies/SpeakYourMind.dll'
    $rejected = $false
    try { Install-SYMPackage $stage $target $backups $hash | Out-Null } catch { $rejected = $true }
    $script:failTarget = ''
    Check $rejected 'Post-install verification failure reported'
    Check ((Get-Content -LiteralPath (Join-Path $target '1.7/Assemblies/SpeakYourMind.dll') -Raw).Trim() -eq 'new') 'Previous install restored after verification failure'
    Check (@(Get-ChildItem -LiteralPath $backups -Directory | Where-Object Name -like 'failed-*').Count -eq 1) 'Failed installation retained for inspection'
    $stage = New-Fixture 'stage3' 'fresh'
    $hash = (Get-FileHash -LiteralPath (Join-Path $stage '1.7/Assemblies/SpeakYourMind.dll') -Algorithm SHA256).Hash
    $result = Install-SYMPackage $stage (Join-Path $root 'fresh-install') $backups $hash
    Check (!$result.Backup -and (Test-Path -LiteralPath $result.Target)) 'Fresh installation works without previous version'
    "PASS: $count installer transaction checks"
} finally { Remove-Item -LiteralPath $root -Recurse -Force }
