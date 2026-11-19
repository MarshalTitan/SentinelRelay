[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$expectedVersion = '0.3.1.1'
$expectedUiHash = '4d21d7c868f48fd082e0bcbd42be136a4611dfb6b9e4588dcef5dde7037d6057'
$expectedCoreHash = '2c34941081aea0fffce0609e15da38633aeacf4f0c0e4f68137932351856eb11'
$packageRoot = '.packages/SentinelCore/v0.3.1.1'
$uiPackage = Join-Path $packageRoot 'MarshalTitan.SentinelCore.UI.0.3.1.1.nupkg'
$corePackage = Join-Path $packageRoot 'MarshalTitan.SentinelCore.0.3.1.1.nupkg'

foreach ($package in @($uiPackage, $corePackage)) {
    if (-not (Test-Path -LiteralPath $package -PathType Leaf)) {
        throw "Required vendored Sentinel Core package is missing: $package"
    }
}

$uiHash = (Get-FileHash -LiteralPath $uiPackage -Algorithm SHA256).Hash.ToLowerInvariant()
$coreHash = (Get-FileHash -LiteralPath $corePackage -Algorithm SHA256).Hash.ToLowerInvariant()
if ($uiHash -ne $expectedUiHash) {
    throw "SentinelCore.UI package hash mismatch: $uiHash"
}
if ($coreHash -ne $expectedCoreHash) {
    throw "SentinelCore package hash mismatch: $coreHash"
}

[xml]$project = Get-Content -LiteralPath 'SentinelRelay.csproj' -Raw
$reference = @($project.Project.ItemGroup.PackageReference) |
    Where-Object { $_.Include -eq 'MarshalTitan.SentinelCore.UI' } |
    Select-Object -First 1
if ($null -eq $reference -or [string]$reference.Version -ne "[$expectedVersion]") {
    throw "SentinelRelay.csproj must pin MarshalTitan.SentinelCore.UI exactly to [$expectedVersion]."
}

$lock = Get-Content -LiteralPath 'packages.lock.json' -Raw | ConvertFrom-Json
$framework = $lock.dependencies.'net10.0-windows7.0'
$lockedUi = $framework.'MarshalTitan.SentinelCore.UI'
$lockedCore = $framework.'MarshalTitan.SentinelCore'
if ($lockedUi.requested -ne "[$expectedVersion, $expectedVersion]" `
    -or $lockedUi.resolved -ne $expectedVersion `
    -or $lockedCore.resolved -ne $expectedVersion) {
    throw 'packages.lock.json does not pin SentinelCore.UI and SentinelCore to 0.3.1.1.'
}

Write-Host "Validated SentinelCore.UI $expectedVersion and vendored package SHA-256 $uiHash."
