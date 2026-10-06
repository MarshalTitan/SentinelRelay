[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$expectedVersion = '0.3.1'
$expectedUiHash = 'e1a9ce4e1ce36042c0fcd53f4c23874d918640be10eef16c21f1cd436c6ba747'
$expectedCoreHash = 'acdbde4e83c3fef325f9a134a5697c2ab87fed1e00cabd3ceff904ba407a4d59'
$packageRoot = '.packages/SentinelCore/v0.3.1.0'
$uiPackage = Join-Path $packageRoot 'MarshalTitan.SentinelCore.UI.0.3.1.nupkg'
$corePackage = Join-Path $packageRoot 'MarshalTitan.SentinelCore.0.3.1.nupkg'

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
    throw 'packages.lock.json does not pin SentinelCore.UI and SentinelCore to 0.3.1.'
}

Write-Host "Validated SentinelCore.UI $expectedVersion and vendored package SHA-256 $uiHash."
