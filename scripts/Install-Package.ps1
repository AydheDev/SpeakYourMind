function Install-SYMPackage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$StagedMod,
        [Parameter(Mandatory)][string]$Target,
        [Parameter(Mandatory)][string]$BackupRoot,
        [Parameter(Mandatory)][string]$ExpectedHash
    )
    $ErrorActionPreference = 'Stop'
    function Assert-SYMFolder([string]$folder) {
        $about = Join-Path $folder 'About/About.xml'
        if (!(Test-Path -LiteralPath $about) -or ([xml](Get-Content -LiteralPath $about -Raw)).ModMetaData.packageId -ne 'aydhe.speakyourmind') {
            throw "Not an identifiable SpeakYourMind folder: $folder"
        }
    }
    Assert-SYMFolder $StagedMod
    if ((Get-FileHash -LiteralPath (Join-Path $StagedMod '1.7/Assemblies/SpeakYourMind.dll') -Algorithm SHA256).Hash -ne $ExpectedHash) {
        throw 'Staged binary does not match its manifest. Installed mod was not changed.'
    }
    $hadPrevious = Test-Path -LiteralPath $Target
    if ($hadPrevious) { Assert-SYMFolder $Target }
    [IO.Directory]::CreateDirectory((Split-Path -Parent $Target)) | Out-Null
    [IO.Directory]::CreateDirectory($BackupRoot) | Out-Null
    $stamp = [DateTime]::Now.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
    $backup = Join-Path $BackupRoot ('previous-' + $stamp)
    if ($hadPrevious) { Move-Item -LiteralPath $Target -Destination $backup }
    try {
        Move-Item -LiteralPath $StagedMod -Destination $Target
        if ((Get-FileHash -LiteralPath (Join-Path $Target '1.7/Assemblies/SpeakYourMind.dll') -Algorithm SHA256).Hash -ne $ExpectedHash) {
            throw 'Installed binary failed verification.'
        }
    } catch {
        if (Test-Path -LiteralPath $Target) { Move-Item -LiteralPath $Target -Destination (Join-Path $BackupRoot ('failed-' + $stamp)) }
        if ($hadPrevious) { Move-Item -LiteralPath $backup -Destination $Target }
        throw
    }
    return [pscustomobject]@{ Target = $Target; Backup = $(if ($hadPrevious) { $backup } else { '' }); Hash = $ExpectedHash }
}
