param(
    [string] $Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\CodexQuotaWidget\CodexQuotaWidget.csproj"
$publishDir = Join-Path $root "artifacts\publish\$Runtime"
$releaseDir = Join-Path $root "artifacts\release"

dotnet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $publishDir

New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
$zipPath = Join-Path $releaseDir "CodexQuotaWidget-$Runtime.zip"
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

$packageFiles = @(
    (Join-Path $publishDir "*"),
    (Join-Path $root "README.md"),
    (Join-Path $root "SECURITY.md"),
    (Join-Path $root "LICENSE"),
    (Join-Path $root "ACKNOWLEDGEMENTS.md")
)
Compress-Archive -Path $packageFiles -DestinationPath $zipPath -CompressionLevel Optimal
$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath
[pscustomobject]@{
    File = $zipPath
    SHA256 = $hash.Hash.ToLowerInvariant()
    Size = (Get-Item -LiteralPath $zipPath).Length
}
