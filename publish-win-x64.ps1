$ErrorActionPreference = 'Stop'

$projectPath = Join-Path $PSScriptRoot 'MixOverlays.csproj'
$outputPath = Join-Path $PSScriptRoot 'dist\win-x64'

dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $outputPath `
    -p:UseAppHost=true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false

Write-Host "Publication terminee : $outputPath\MixOverlays.exe"