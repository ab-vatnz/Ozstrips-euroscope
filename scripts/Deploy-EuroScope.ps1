param(
    [string]$Configuration = "Release",
    [string]$TargetRoot = "C:\Program Files (x86)\EuroScope\VATNZ-SKYLINE_2412\Plugins\OzStripsEuroScope"
)

$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $PSScriptRoot
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
$helperProject = Join-Path $repo "src\OzStripsEuroScope.OzStripsGui\OzStripsEuroScope.OzStripsGui.csproj"
$pluginProject = Join-Path $repo "src\OzStripsEuroScope.Plugin\OzStripsEuroScope.Plugin.vcxproj"
$helperOut = Join-Path $repo "src\OzStripsEuroScope.OzStripsGui\bin\$Configuration"
$pluginOut = Join-Path $repo "src\OzStripsEuroScope.Plugin\bin\Win32\$Configuration"

dotnet build $helperProject -c $Configuration
& $msbuild $pluginProject /p:Configuration=$Configuration /p:Platform=Win32 /m

function Use-FallbackTarget {
    $TargetRoot = Join-Path $repo "dist\OzStripsEuroScope"
    New-Item -ItemType Directory -Force -Path $TargetRoot | Out-Null
    Write-Warning "Could not write to Program Files. Falling back to $TargetRoot"
    return $TargetRoot
}

try {
    New-Item -ItemType Directory -Force -Path $TargetRoot | Out-Null
} catch [System.UnauthorizedAccessException] {
    $TargetRoot = Use-FallbackTarget
}

try {
    Copy-Item -LiteralPath (Join-Path $pluginOut "OzStripsEuroScope.dll") -Destination $TargetRoot -Force
    Copy-Item -LiteralPath (Join-Path $pluginOut "OzStripsEuroScope.pdb") -Destination $TargetRoot -Force -ErrorAction SilentlyContinue
    Get-ChildItem -LiteralPath $helperOut -Force | Copy-Item -Destination $TargetRoot -Recurse -Force
} catch [System.UnauthorizedAccessException] {
    $TargetRoot = Use-FallbackTarget
    Copy-Item -LiteralPath (Join-Path $pluginOut "OzStripsEuroScope.dll") -Destination $TargetRoot -Force
    Copy-Item -LiteralPath (Join-Path $pluginOut "OzStripsEuroScope.pdb") -Destination $TargetRoot -Force -ErrorAction SilentlyContinue
    Get-ChildItem -LiteralPath $helperOut -Force | Copy-Item -Destination $TargetRoot -Recurse -Force
}

Write-Host "Deployed OzStrips EuroScope to $TargetRoot"
Write-Host "Load OzStripsEuroScope.dll in EuroScope, then type .ozstrips to open the stripboard."
