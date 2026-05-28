param(
    [string]$SourceRepo = "https://github.com/ab-vatnz/OzStrips-NZ-.git",
    [string]$SourceRef = "vatnz/main",
    [string]$VendorPath = "vendor/OzStrips"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$repoFull = [System.IO.Path]::GetFullPath($repoRoot)
$vendorFull = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $VendorPath))

if (-not $vendorFull.StartsWith($repoFull, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to update vendor path outside repository: $vendorFull"
}

$excludedDirectories = @(".git", ".github", "bin", "obj", ".vs", "dist")
$excludedFiles = @("*.suo", "*.user", "*.VC.db", "*.ipch", "VATNZ_SYNC.md")

function Test-ExcludedFile {
    param([string]$Name)

    foreach ($pattern in $excludedFiles) {
        if ($Name -like $pattern) {
            return $true
        }
    }

    return $false
}

function Copy-Tree {
    param(
        [string]$Source,
        [string]$Destination
    )

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null

    foreach ($item in Get-ChildItem -LiteralPath $Source -Force) {
        if ($item.PSIsContainer) {
            if ($excludedDirectories -contains $item.Name) {
                continue
            }

            Copy-Tree -Source $item.FullName -Destination (Join-Path $Destination $item.Name)
            continue
        }

        if (Test-ExcludedFile -Name $item.Name) {
            continue
        }

        Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $Destination $item.Name) -Force
    }
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ozstrips-vatnz-sync-" + [System.Guid]::NewGuid().ToString("N"))

try {
    git clone --depth 1 --branch $SourceRef $SourceRepo $tempRoot

    New-Item -ItemType Directory -Force -Path $vendorFull | Out-Null
    Get-ChildItem -LiteralPath $vendorFull -Force | Remove-Item -Recurse -Force

    Copy-Tree -Source $tempRoot -Destination $vendorFull

    Write-Host "Updated $VendorPath from $SourceRepo@$SourceRef"
    Write-Host "Review the vendor diff, then manually port any needed GUI changes into src/OzStripsEuroScope.OzStripsGui."
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
