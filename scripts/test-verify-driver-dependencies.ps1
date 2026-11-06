# Synthetic regression checks; no MongoDB, NuGet restore, or Pester required.
$ErrorActionPreference = 'Stop'
$verify = Join-Path $PSScriptRoot 'verify-driver-dependencies.ps1'
$temp = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString())
New-Item -ItemType Directory $temp | Out-Null
$testPath = Join-Path $temp 'tests.json'
$libraryPath = Join-Path $temp 'library.json'
$packagePath = Join-Path $temp 'fixture.nupkg'
$testTargets = @('.NETFramework,Version=v4.7.2', 'net8.0', 'net9.0', 'net10.0')
$libraryTargets = $testTargets + @('netstandard2.1', 'net6.0')
function Write-Assets($path, $targets, $version) {
    $data = @{ targets = @{} }
    foreach ($target in $targets) {
        $data.targets[$target] = @{
            "MongoDB.Driver/$version" = @{ type = 'package' }
            "MongoDB.Bson/$version" = @{ type = 'package' }
        }
    }
    $data | ConvertTo-Json -Depth 10 | Set-Content $path
}
function Assert-Fails($label, [scriptblock]$action, $message) {
    try { & $action } catch {
        if ($_.Exception.Message -notlike "*$message*") { throw }
        Write-Host "PASS: $label"
        return
    }
    throw "Expected failure: $label"
}
function Check-Assets($version) {
    & $verify -TestDriverVersion $version -TestAssetsPath $testPath -LibraryAssetsPath $libraryPath
}
function Write-Package($targets, $range) {
    $groups = foreach ($target in $targets) {
        "<group targetFramework='$target'><dependency id='MongoDB.Driver' version='$range'/></group>"
    }
    $xml = "<package xmlns='http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd'><metadata><id>NEventStore.Persistence.MongoDB</id><version>1.0.0</version><dependencies>$($groups -join '')</dependencies></metadata></package>"
    $zip = [IO.Compression.ZipFile]::Open($packagePath, 'Create')
    try {
        $writer = [IO.StreamWriter]::new($zip.CreateEntry('NEventStore.Persistence.MongoDB.nuspec').Open())
        try { $writer.Write($xml) } finally { $writer.Dispose() }
    } finally { $zip.Dispose() }
}
try {
    Write-Assets $libraryPath $libraryTargets '3.0.0'
    foreach ($version in @('3.0.0', '3.9.0')) {
        Write-Assets $testPath $testTargets $version
        Check-Assets $version
    }
    Assert-Fails 'test downgrade' { Check-Assets '3.0.0' } 'MongoDB.Driver'
    $data = Get-Content $testPath -Raw | ConvertFrom-Json -AsHashtable
    $data.targets['net10.0'].Remove('MongoDB.Bson/3.9.0')
    $data | ConvertTo-Json -Depth 10 | Set-Content $testPath
    Assert-Fails 'missing BSON' { Check-Assets '3.9.0' } 'MongoDB.Bson'
    Write-Assets $testPath @('net8.0') '3.9.0'
    Assert-Fails 'missing test frameworks' { Check-Assets '3.9.0' } 'target frameworks'
    Write-Assets $testPath ($testTargets + 'net8.0/linux-x64') '3.9.0'
    Check-Assets '3.9.0'
    $data = Get-Content $testPath -Raw | ConvertFrom-Json -AsHashtable
    $data.targets['net8.0/linux-x64'].Remove('MongoDB.Bson/3.9.0')
    $data.targets['net8.0/linux-x64']['MongoDB.Bson/3.0.0'] = @{ type = 'package' }
    $data | ConvertTo-Json -Depth 10 | Set-Content $testPath
    Assert-Fails 'BSON skew in RID target' { Check-Assets '3.9.0' } 'MongoDB.Bson'
    Write-Assets $testPath $testTargets '3.9.0'
    Write-Assets $libraryPath $libraryTargets '3.9.0'
    Assert-Fails 'production upgraded' { Check-Assets '3.9.0' } 'MongoDB.Driver'
    Write-Assets $libraryPath @() '3.0.0'
    Assert-Fails 'empty production targets' { Check-Assets '3.9.0' } 'target frameworks'
    Write-Package @('.NETFramework4.7.2', '.NETStandard2.1', 'net6.0', 'net8.0', 'net9.0', 'net10.0') '[3.0.0,4.0.0)'
    & $verify -PackagePath $packagePath
    Remove-Item $packagePath
    Write-Package $libraryTargets '[3.0.0, 4.0.0)'
    & $verify -PackagePath $packagePath
    Remove-Item $packagePath
    Write-Package $libraryTargets '[3.9.0,4.0.0)'
    Assert-Fails 'raised package floor' { & $verify -PackagePath $packagePath } 'MongoDB.Driver'
    Remove-Item $packagePath
    Write-Package @('net8.0') '[3.0.0,4.0.0)'
    Assert-Fails 'missing package frameworks' { & $verify -PackagePath $packagePath } 'target frameworks'
    Write-Host 'All dependency verifier regression checks passed.'
} finally {
    Remove-Item $temp -Recurse -Force
}
