properties {
    $base_directory = Resolve-Path .. 
	$publish_directory = "$base_directory\publish-net40"
	$build_directory = "$base_directory\build"
	$src_directory = "$base_directory\src"
	$output_directory = "$base_directory\output"
	$packages_directory = "$src_directory\packages"
	$sln_file = "$src_directory\NEventStore.Persistence.MongoDB.sln"
	$target_config = "Release"
	$framework_version = "v4.0"
    $build_number = 0
    $assemblyInfoFilePath = "$src_directory\VersionAssemblyInfo.cs"

	$xunit_path = "$base_directory\bin\xunit.runners.1.9.1\tools\xunit.console.clr4.exe"
	$ilMergeModule.ilMergePath = "$base_directory\bin\ilmerge-bin\ILMerge.exe"
	$nuget_dir = "$src_directory\.nuget"

	if($runPersistenceTests -eq $null) {
		$runPersistenceTests = $false
	}
}

task default -depends Build

task Build -depends Clean, UpdateVersion, Compile, Test

task Clean {
	Clean-Item $publish_directory -ea SilentlyContinue
    Clean-Item $output_directory -ea SilentlyContinue
}

task UpdateVersion {
    $version = Get-Version $assemblyInfoFilePath
    "Version: $version"
	$oldVersion = New-Object Version $version
	$newVersion = New-Object Version ($oldVersion.Major, $oldVersion.Minor, $oldVersion.Build, $build_number)
	Update-Version $newVersion $assemblyInfoFilePath
}

task Compile {
	EnsureDirectory $output_directory
	exec { msbuild /nologo /verbosity:quiet $sln_file /p:Configuration=$target_config /t:Clean }
	exec { msbuild /nologo /verbosity:quiet $sln_file /p:Configuration=$target_config /p:TargetFrameworkVersion=v4.0 }
}

task Test -precondition { $runPersistenceTests } {
	"Persistence Tests"
	EnsureDirectory $output_directory
	Invoke-XUnit -Path $src_directory -TestSpec '*Persistence.MongoDB.Tests.dll' `
    -SummaryPath $output_directory\persistence_tests.xml `
    -XUnitPath $xunit_path
}

task Package -depends Build {
	move $output_directory $publish_directory
    mkdir $publish_directory\plugins\persistence\mongo | out-null
    copy "$src_directory\NEventStore.Persistence.MongoDB\bin\$target_config\NEventStore.Persistence.MongoDB.???" "$publish_directory\plugins\persistence\mongo"
    copy "$src_directory\NEventStore.Persistence.MongoDB\bin\$target_config\readme.txt" "$publish_directory\plugins\persistence\mongo"
}

task NuGetPack -depends Package {
    $versionString = Get-Version $assemblyInfoFilePath
	$version = New-Object Version $versionString
	$packageVersion = $version.Major.ToString() + "." + $version.Minor.ToString() + "." + $version.Build.ToString() + "-build" + $build_number.ToString().PadLeft(5,'0')
	gci -r -i *.nuspec "$nuget_dir" |% { .$nuget_dir\nuget.exe pack $_ -basepath $base_directory -o $publish_directory -version $packageVersion }
}

function EnsureDirectory {
	param($directory)

	if(!(test-path $directory))
	{
		mkdir $directory
	}
}