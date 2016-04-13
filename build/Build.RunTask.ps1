Param(
	[string]$task,
	[string]$version = "0.0.0.0",
	[bool]$runPersistenceTests = $true)

if($task -eq $null) {
	$task = read-host "Enter Task"
}

$scriptPath = $(Split-Path -parent $MyInvocation.MyCommand.path)

.$scriptPath\..\dependencies\RestorePackages.ps1
.$scriptPath\..\dependencies\NEventStore\build\psake.ps1 -scriptPath $scriptPath -t $task -properties @{ version=$version;runPersistenceTests=$runPersistenceTests }