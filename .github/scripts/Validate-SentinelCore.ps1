[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Prepare-NavigationCore.ps1')

$expectedVersion = '0.4.1.3'
$expectedUiHash = 'e24369d5736eb3c10939de6c1e3f7565c06b34943a98dce4df9ec84a9662189c'
$expectedCoreHash = '71ecf832ac9c03d1e709bd84875e6a569abc9a71bc9def9bec11ccb2744a825d'
$packageRoot = '.packages/SentinelCore/v0.4.1.3'
$uiPackage = Join-Path $packageRoot 'MarshalTitan.SentinelCore.UI.0.4.1.3.nupkg'
$corePackage = Join-Path $packageRoot 'MarshalTitan.SentinelCore.0.4.1.3.nupkg'

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
    throw 'packages.lock.json does not pin SentinelCore.UI and SentinelCore to 0.4.1.3.'
}

Write-Host "Validated SentinelCore.UI $expectedVersion and vendored package SHA-256 $uiHash."
