param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$ReleaseAssetUrl
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Version must contain four numeric components: $Version"
}
if ($ReleaseAssetUrl -notmatch '^https://github\.com/MarshalTitan/SentinelRelay/releases/download/v[^/]+/SentinelRelay\.zip$') {
    throw "Unexpected release URL: $ReleaseAssetUrl"
}

$entries = [System.Collections.ArrayList]@(
    @(Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json)
)
$matches = @($entries | Where-Object { $_.InternalName -eq 'SentinelRelay' })
if ($matches.Count -gt 1) {
    throw "Expected zero or one SentinelRelay entry; found $($matches.Count)."
}

$values = [ordered]@{
    Author = 'MTitan'
    Name = 'Sentinel Relay'
    Punchline = 'Private FFXIV-to-Discord chat relay with no hosted backend.'
    Description = 'Sends explicitly selected FFXIV chat channels directly to a per-character Discord webhook. All filters default off; no bot, paid hosting, or Discord-to-FFXIV control is required.'
    InternalName = 'SentinelRelay'
    AssemblyVersion = $Version
    TestingAssemblyVersion = $Version
    RepoUrl = 'https://github.com/MarshalTitan/SentinelRelay'
    IconUrl = 'https://raw.githubusercontent.com/MarshalTitan/SentinelRelay/main/assets/icon.png'
    ApplicableVersion = 'any'
    DalamudApiLevel = 15
    TestingDalamudApiLevel = 15
    IsHide = $false
    IsTestingExclusive = $false
    DownloadCount = 0
    LastUpdate = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    LoadPriority = 0
    DownloadLinkInstall = $ReleaseAssetUrl
    DownloadLinkUpdate = $ReleaseAssetUrl
    DownloadLinkTesting = $ReleaseAssetUrl
    Tags = @('chat', 'discord', 'relay', 'utility', 'sentinel')
}

if ($matches.Count -eq 0) {
    [void]$entries.Add([pscustomobject]$values)
}
else {
    $entry = $matches[0]
    foreach ($pair in $values.GetEnumerator()) {
        if ($entry.PSObject.Properties.Name -contains $pair.Key) {
            $entry.($pair.Key) = $pair.Value
        }
        else {
            $entry | Add-Member -NotePropertyName $pair.Key -NotePropertyValue $pair.Value
        }
    }
}

$relayEntries = @($entries | Where-Object { $_.InternalName -eq 'SentinelRelay' })
if ($relayEntries.Count -ne 1) {
    throw "Catalog upsert produced $($relayEntries.Count) SentinelRelay entries."
}

$json = ConvertTo-Json -InputObject @($entries) -Depth 30
$resolvedPath = (Resolve-Path -LiteralPath $ManifestPath).Path
[System.IO.File]::WriteAllText($resolvedPath, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
Write-Host "Upserted only SentinelRelay $Version."
