$platform = $args[0]
if (!$platform) {
    Write-Host "Missing Platform: windows | linux"
    exit(1)
}

Write-Host "Run the CI environment"

docker-compose -f docker-compose.ci.$platform.db.yml -p nesci up --detach