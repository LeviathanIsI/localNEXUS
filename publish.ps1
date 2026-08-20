# Publishes LocalNEXUS as a self contained single file build into dist/, ready to hand to
# someone with no .NET install. The bundled engine binaries are copied alongside the exe with
# their expected relative paths intact, so local inference and the mesh work from the published
# folder exactly as they do from a development run.

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

$llamaSource = Join-Path $root 'vendor\llama'
$llamaTarget = Join-Path $dist 'vendor\llama'

if (Test-Path (Join-Path $llamaSource 'llama-server.exe')) {
    New-Item -ItemType Directory -Force $llamaTarget | Out-Null
    Copy-Item (Join-Path $llamaSource '*') $llamaTarget -Recurse -Force
    Write-Host "Copied llama.cpp binaries into dist\vendor\llama"
}
else {
    Write-Warning "vendor\llama has no llama-server.exe. The published app will run, but local inference needs a llama.cpp build placed in dist\vendor\llama. See vendor\llama\README.md."
}

$meshSource = Join-Path $root 'vendor\mesh'
$meshTarget = Join-Path $dist 'vendor\mesh'

$meshExecutable = @(
    (Join-Path $meshSource 'mesh-bundle\mesh-llm.exe'),
    (Join-Path $meshSource 'mesh-llm.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($meshExecutable) {
    New-Item -ItemType Directory -Force $meshTarget | Out-Null
    Copy-Item (Join-Path $meshSource '*') $meshTarget -Recurse -Force
    Write-Host "Copied Mesh LLM binaries into dist\vendor\mesh"
}
else {
    Write-Warning "vendor\mesh has no mesh-llm.exe. The published app will run and local inference works, but the Network tab needs a Mesh LLM build placed in dist\vendor\mesh. See vendor\mesh\README.md."
}

$uvSource = Join-Path $root 'vendor\uv'
$uvTarget = Join-Path $dist 'vendor\uv'

if (Test-Path (Join-Path $uvSource 'uv.exe')) {
    New-Item -ItemType Directory -Force $uvTarget | Out-Null
    Copy-Item (Join-Path $uvSource 'uv.exe') $uvTarget -Force
    Write-Host "Copied uv into dist\vendor\uv"
}
else {
    Write-Warning "vendor\uv has no uv.exe. The published app will run and GGUF models work, but the Python runtime that serves safetensors models cannot be set up. See vendor\uv\README.md."
}

# The dependency lockfiles are committed rather than fetched per machine, so unlike the engine
# binaries this copy is never conditional.
$pythonSource = Join-Path $root 'vendor\python'
$pythonTarget = Join-Path $dist 'vendor\python'

New-Item -ItemType Directory -Force $pythonTarget | Out-Null
Copy-Item (Join-Path $pythonSource '*.txt') $pythonTarget -Force
Write-Host "Copied the Python lockfiles into dist\vendor\python"

Write-Host "Done. Run $(Join-Path $dist 'LocalNEXUS.exe')"
