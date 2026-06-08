# Builds everything shippable into dist/:
#   - Seerlens.nupkg                the `seerlens` dotnet tool
#   - Seerlens.Sdk.nupkg            the .NET SDK
#   - Seerlens.SemanticKernel.nupkg the Semantic Kernel tracing filter
#   - seerlens-<rid>.zip            self-contained builds for people without .NET
#
# Usage: pwsh build/pack.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

Write-Host "building dashboard..."
Push-Location dashboard
npm install --no-fund --no-audit
npm run build
Pop-Location

Remove-Item dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory dist | Out-Null

Write-Host "packing nuget packages..."
dotnet pack src/Seerlens.Sdk -c Release -o dist
dotnet pack src/Seerlens.SemanticKernel -c Release -o dist
dotnet pack src/Seerlens.Collector -c Release -o dist

foreach ($rid in 'win-x64', 'linux-x64', 'osx-arm64') {
    Write-Host "publishing $rid..."
    $out = "dist/seerlens-$rid"
    dotnet publish src/Seerlens.Collector -c Release -r $rid --self-contained `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $out

    # give the executable the friendly name
    if (Test-Path "$out/Seerlens.Collector.exe") { Rename-Item "$out/Seerlens.Collector.exe" 'seerlens.exe' }
    if (Test-Path "$out/Seerlens.Collector") { Rename-Item "$out/Seerlens.Collector" 'seerlens' }

    Compress-Archive -Path "$out/*" -DestinationPath "dist/seerlens-$rid.zip" -Force
    Remove-Item $out -Recurse -Force
}

Write-Host "`ndone. artifacts:"
Get-ChildItem dist | Select-Object Name, @{n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table
