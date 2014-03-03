gci .\NEventStore\src -Recurse "packages.config" |% {
	"Restoring " + $_.FullName
	.\NEventStore\src\.nuget\nuget.exe i $_.FullName -o .\NEventStore\src\packages
}