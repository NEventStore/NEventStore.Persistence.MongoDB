[CmdletBinding()]
param(
    [ValidateSet('baseline', 'after')]
    [string]$SnapshotType = 'baseline',

    [string]$OptimizationId,

    [string]$Framework = 'net10.0',

    [string]$Filter = '*',

    [switch]$ShortRun,

    [string]$ConnectionString = 'mongodb://localhost:50002/NEventStore',

    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($SnapshotType -eq 'after' -and [string]::IsNullOrWhiteSpace($OptimizationId))
{
    throw "Parameter -OptimizationId is required when -SnapshotType after is used."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$benchmarkProject = Join-Path $repoRoot 'src/NEventStore.Persistence.MongoDB.Benchmark/NEventStore.Persistence.MongoDB.Benchmark.csproj'
$benchmarkDll = Join-Path $repoRoot "src/NEventStore.Persistence.MongoDB.Benchmark/bin/Release/$Framework/NEventStore.Persistence.MongoDB.Benchmark.dll"
$resultsDir = Join-Path $repoRoot 'BenchmarkDotNet.Artifacts/results'
$archiveRoot = Join-Path $repoRoot 'artifacts/benchmark-snapshots'

Write-Host "Repository root: $repoRoot"
Write-Host "Snapshot type: $SnapshotType"
Write-Host "Framework: $Framework"
Write-Host "Filter: $Filter"

if (-not $SkipBuild)
{
    Write-Host "Building benchmark project for $Framework..."
    dotnet build $benchmarkProject -c Release -f $Framework -v q
    if ($LASTEXITCODE -ne 0)
    {
        throw "dotnet build failed with exit code $LASTEXITCODE"
    }
}

if (-not (Test-Path $benchmarkDll))
{
    throw "Benchmark binary not found: $benchmarkDll"
}

if (Test-Path $resultsDir)
{
    Remove-Item $resultsDir -Recurse -Force
}
New-Item -ItemType Directory -Path $resultsDir -Force | Out-Null
New-Item -ItemType Directory -Path $archiveRoot -Force | Out-Null

[Environment]::SetEnvironmentVariable('NEventStore.MongoDB', $ConnectionString, 'Process')
Write-Host 'Running BenchmarkDotNet...'

$dotnetArgs = @(
    $benchmarkDll,
    '--filter', $Filter
)

if ($ShortRun)
{
    $dotnetArgs += @('--job', 'short')
}

& dotnet @dotnetArgs
if ($LASTEXITCODE -ne 0)
{
    throw "Benchmark execution failed with exit code $LASTEXITCODE"
}

$reportFiles = Get-ChildItem -Path $resultsDir -Filter '*-report-github.md' -File
if (-not $reportFiles)
{
    throw "No benchmark report files found in $resultsDir"
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmm'
$frameworkTag = $Framework -replace '[^A-Za-z0-9.-]', '-'

if ($SnapshotType -eq 'baseline')
{
    $archiveName = "benchmark-baseline-$frameworkTag-$timestamp.zip"
}
else
{
    $safeOptimizationId = $OptimizationId -replace '[^A-Za-z0-9._-]', '-'
    $archiveName = "benchmark-after-$safeOptimizationId-$frameworkTag-$timestamp.zip"
}

$archivePath = Join-Path $archiveRoot $archiveName

# Compress all report files; use array expansion for proper globbing in Compress-Archive
$filesToArchive = Get-ChildItem -Path $resultsDir -File | Select-Object -ExpandProperty FullName
Compress-Archive -Path $filesToArchive -DestinationPath $archivePath -Force

$runManifest = [ordered]@{
    snapshotType = $SnapshotType
    optimizationId = $OptimizationId
    framework = $Framework
    filter = $Filter
    shortRun = [bool]$ShortRun
    connectionString = $ConnectionString
    createdAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    archive = $archivePath
    reportCount = $reportFiles.Count
    reportFiles = $reportFiles.Name
}

$manifestPath = [System.IO.Path]::ChangeExtension($archivePath, '.json')
$runManifest | ConvertTo-Json -Depth 6 | Set-Content -Path $manifestPath -Encoding UTF8

Write-Host "Snapshot archive created: $archivePath"
Write-Host "Snapshot manifest created: $manifestPath"

Write-Host ''
Write-Host 'Use these values in docs/Performance-Investigation.md:'
Write-Host '- Before Snapshot table (if baseline)'
Write-Host '- After Snapshot table (if after)'
