[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Prepare-NavigationCore.ps1')

$expectedVersion = '0.4.1.2'
$expectedUiHash = 'ee2aec0d063eb1ee1da815de58c58bb2c8ff2199ce395c118c1c3976606a4c1d'
$expectedCoreHash = '82057104351fb05bbcbc579a62a3373ce7f17b46a9f39b06e872c72262c4f949'
$packageRoot = '.packages/SentinelCore/v0.4.1.2'
$uiPackage = Join-Path $packageRoot 'MarshalTitan.SentinelCore.UI.0.4.1.2.nupkg'
$corePackage = Join-Path $packageRoot 'MarshalTitan.SentinelCore.0.4.1.2.nupkg'

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
    throw 'packages.lock.json does not pin SentinelCore.UI and SentinelCore to 0.4.1.2.'
}

Write-Host "Validated SentinelCore.UI $expectedVersion and vendored package SHA-256 $uiHash."
