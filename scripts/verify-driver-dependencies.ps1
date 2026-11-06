[CmdletBinding(DefaultParameterSetName = 'Assets')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Assets')]
    [string]$TestDriverVersion,
    [Parameter(ParameterSetName = 'Assets')]
    [string]$TestAssetsPath = "$PSScriptRoot/../src/NEventStore.Persistence.MongoDB.Tests/obj/project.assets.json",
    [Parameter(ParameterSetName = 'Assets')]
    [string]$LibraryAssetsPath = "$PSScriptRoot/../src/NEventStore.Persistence.MongoDB/obj/project.assets.json",
    [Parameter(Mandatory, ParameterSetName = 'Package')]
    [string]$PackagePath,
    [Parameter(ParameterSetName = 'Package')]
    [switch]$RestoreConsumer
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Explicit compatibility contract: changing supported targets requires updating this guard.
$testFrameworks = @('net472', 'net8.0', 'net9.0', 'net10.0')
$libraryFrameworks = $testFrameworks + @('netstandard2.1', 'net6.0')

function Normalize-Framework([string]$framework) {
    $framework = $framework.Split('/')[0].ToLowerInvariant().Replace(',version=v', '').TrimStart('.')
    if ($framework.StartsWith('netframework')) { return 'net' + $framework.Substring(12).Replace('.', '') }
    return $framework.Replace('netcoreapp', 'net')
}

function Assert-Frameworks($actual, $expected, $source) {
    $normalized = @($actual | ForEach-Object { Normalize-Framework $_ } | Sort-Object -Unique)
    if (Compare-Object $normalized ($expected | Sort-Object -Unique)) {
        throw "$source target frameworks: expected $($expected -join ', '); found $($normalized -join ', ')"
    }
}

function Assert-Assets($path, $version, $frameworks) {
    $assets = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
    if (-not $assets.targets -or $assets.targets.Count -eq 0) { throw "$path has no target frameworks" }
    Assert-Frameworks @($assets.targets.Keys) $frameworks $path
    foreach ($target in $assets.targets.Keys) {
        foreach ($package in @('MongoDB.Driver', 'MongoDB.Bson')) {
            $matches = @($assets.targets[$target].Keys | Where-Object { $_.StartsWith("$package/", [StringComparison]::OrdinalIgnoreCase) })
            if ($matches.Count -ne 1 -or $matches[0] -cne "$package/$version" -or $assets.targets[$target][$matches[0]].type -ne 'package') {
                throw "$path [$target]: expected $package/$version; found $($matches -join ', ')"
            }
        }
    }
    Write-Host "Verified $path`: MongoDB.Driver and MongoDB.Bson $version in every target."
}

if ($PSCmdlet.ParameterSetName -eq 'Assets') {
    Assert-Assets $TestAssetsPath $TestDriverVersion $testFrameworks
    Assert-Assets $LibraryAssetsPath '3.0.0' $libraryFrameworks
} else {
    $zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $PackagePath).Path)
    try {
        $entries = @($zip.Entries | Where-Object { $_.FullName -like '*.nuspec' })
        if ($entries.Count -ne 1) { throw "$PackagePath must contain exactly one nuspec" }
        $reader = [IO.StreamReader]::new($entries[0].Open())
        try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($nuspec.package.metadata.id -ne 'NEventStore.Persistence.MongoDB') { throw 'Unexpected package identity' }
        $groups = @($nuspec.package.metadata.dependencies.group)
        Assert-Frameworks @($groups | ForEach-Object { $_.targetFramework }) $libraryFrameworks $PackagePath
        foreach ($group in $groups) {
            $drivers = @($group.dependency | Where-Object { $_.id -eq 'MongoDB.Driver' })
            if ($drivers.Count -ne 1 -or ($drivers[0].version -replace '\s', '') -ne '[3.0.0,4.0.0)') {
                throw "$PackagePath [$($group.targetFramework)]: expected MongoDB.Driver [3.0.0,4.0.0)"
            }
        }
        Write-Host "Verified $PackagePath`: MongoDB.Driver [3.0.0,4.0.0) for all supported frameworks."
    } finally { $zip.Dispose() }
}

# Restore the actual package as a consumer, isolated from repository MSBuild props
# and the global package cache so a previously published package cannot mask it.
if ($RestoreConsumer) {
    $temp = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString())
    New-Item -ItemType Directory $temp | Out-Null
    try {
        $project = Join-Path $temp 'Consumer.csproj'
        $version = [Security.SecurityElement]::Escape($nuspec.package.metadata.version)
        if ([string]::IsNullOrWhiteSpace($version)) { throw 'Packed nuspec has no version' }
        @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFrameworks>$($libraryFrameworks -join ';')</TargetFrameworks></PropertyGroup>
  <ItemGroup><PackageReference Include="NEventStore.Persistence.MongoDB" Version="[$version]" /></ItemGroup>
</Project>
"@ | Set-Content $project
        $source = New-Item -ItemType Directory "$temp/feed"
        Copy-Item -LiteralPath $PackagePath -Destination $source.FullName
        $escapedSource = [Security.SecurityElement]::Escape($source.FullName)
        $config = Join-Path $temp 'NuGet.Config'
        # Exact ID mapping takes precedence over the wildcard, so the adapter
        # can only come from this build, never the public feed.
        @"
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$escapedSource" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="local"><package pattern="NEventStore.Persistence.MongoDB" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content $config
        & dotnet restore $project --configfile $config --packages "$temp/packages" --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw "Packed consumer restore failed with exit code $LASTEXITCODE" }
        Assert-Assets "$temp/obj/project.assets.json" '3.0.0' $libraryFrameworks
    } finally {
        Remove-Item $temp -Recurse -Force
    }
}
