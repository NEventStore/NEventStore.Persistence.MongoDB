Param(
	[string]$task,
	[bool]$runPersistenceTests = $true)

if($task -eq $null) {
	$task = read-host "Enter Task"
}

$scriptPath = $(Split-Path -parent $MyInvocation.MyCommand.path)

.$scriptPath\..\build\psake.ps1 -scriptPath $scriptPath -t $task -properties @{ runPersistenceTests=$runPersistenceTests }