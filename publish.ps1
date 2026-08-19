# Publishes LocalNEXUS as a self contained single file build into dist/, ready to hand to
# someone with no .NET install. Bundled llama.cpp binaries are copied alongside the exe with
# their expected relative path intact, so local and distributed inference work from the
# published folder exactly as they do from a development run.

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$project = Join-Path $root 'src\LocalNEXUS.App\LocalNEXUS.App.csproj'
$dist = Join-Path $root 'dist'

Write-Host "Publishing to $dist"

dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --output $dist

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

$vendorSource = Join-Path $root 'vendor\llama'
$vendorTarget = Join-Path $dist 'vendor\llama'

if (Test-Path (Join-Path $vendorSource 'llama-server.exe')) {
    New-Item -ItemType Directory -Force $vendorTarget | Out-Null
    Copy-Item (Join-Path $vendorSource '*') $vendorTarget -Recurse -Force
    Write-Host "Copied llama.cpp binaries into dist\vendor\llama"
}
else {
    Write-Warning "vendor\llama has no llama-server.exe. The published app will run, but local and distributed inference need a llama.cpp build placed in dist\vendor\llama. See vendor\llama\README.md."
}

Write-Host "Done. Run $(Join-Path $dist 'LocalNEXUS.exe')"
