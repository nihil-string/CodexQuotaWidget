param(
    [string] $Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
if ($Runtime -notmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$') {
    throw "Runtime must be a RID such as win-x64 or win-arm64."
}

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\CodexQuotaWidget\CodexQuotaWidget.csproj"
$publishRoot = [System.IO.Path]::GetFullPath((Join-Path $root "artifacts\publish"))
$publishDir = [System.IO.Path]::GetFullPath((Join-Path $publishRoot $Runtime))
if (-not $publishDir.StartsWith($publishRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Publish directory escaped the artifacts root."
}

$releaseDir = Join-Path $root "artifacts\release"
$zipPath = Join-Path $releaseDir "CodexQuotaWidget-$Runtime.zip"

New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

dotnet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
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
