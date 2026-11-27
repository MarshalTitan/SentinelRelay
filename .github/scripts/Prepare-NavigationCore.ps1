$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$pin = Get-Content -LiteralPath (Join-Path $root 'sentinelcore-packages.json') -Raw | ConvertFrom-Json
if ($pin.version -ne '0.4.1.2' -or $pin.commit -ne 'ffa1a6ed3c89337bfb901b566b9339e2e229d154') { throw 'Unexpected navigation UI dependency pin' }
$destination = Join-Path $root '.packages/SentinelCore/v0.4.1.2'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($package in $pin.packages) {
    $file = Join-Path $destination $package.file
    if (!(Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $package.sha256) {
        $candidate = $file + '.download'
        Invoke-WebRequest -Uri $package.url -OutFile $candidate
        if ((Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToLowerInvariant() -ne $package.sha256) { throw "Published Core package hash mismatch: $($package.file)" }
        Move-Item -LiteralPath $candidate -Destination $file -Force
    }
}
Write-Host 'Prepared immutable, hash-verified Sentinel Core navigation packages.'
