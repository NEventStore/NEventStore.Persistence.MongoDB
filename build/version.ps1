Write-Output "Starting versioning script"
if(Get-Module -name BuildFunctions) 
{
    Remove-Module BuildFunctions
}
if(-not(Get-Module -name BuildFunctions)) 
{
    Import-Module -Name ".\Modules\BuildFunctions"
}
Write-Output "Invoking GitVersion.exe"
$Output = & ..\bin\GitVersion\Gitversion.exe | Out-String
Write-Output ("GitVersion returned:  " + $output)


$version = $output | ConvertFrom-Json
$assemblyVersion = $version.AssemblySemver
$assemblyFileVersion = $version.AssemblySemver
$assemblyInformationalVersion = ($version.SemVer + "/" + $version.Sha)

Write-Output "SemVer - Assembly and File: $assemblyVersion Informational: $assemblyInformationalVersion"

Write-Output $version.NuGetVersionV2
Write-Output ("##vso[task.setvariable variable=NugetVersion;]" + $version.NugetVersionV2)
Write-Output ("##vso[task.setvariable variable=AssemblyVersion;]" + $assemblyVersion)
Write-Output ("##vso[task.setvariable variable=FileInfoVersion;]" + $assemblyFileVersion)
Write-Output ("##vso[task.setvariable variable=AssemblyInformationalVersion;]" + $assemblyInformationalVersion)

#change build number.
Write-Output ("##vso[task.setvariable variable=build.buildnumber;]" + $version.FullSemVer)
Write-Output ("##vso[build.updatebuildnumber]" + $version.FullSemVer)

Update-SourceVersion -SrcPath ..\src -FilePattern 'VersionAssemblyInfo.cs' -assemblyVersion $assemblyVersion -fileAssemblyVersion $assemblyFileVersion -assemblyInformationalVersion $assemblyInformationalVersion