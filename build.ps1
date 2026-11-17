$root = $PSScriptRoot
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host " Building CloudPulse APM .NET 10 Microservices" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

$projects = @(
    "$root\src\Apm.Shared\Apm.Shared.csproj",
    "$root\src\Apm.IngestionService\Apm.IngestionService.csproj",
    "$root\src\Apm.AnomalyEngineService\Apm.AnomalyEngineService.csproj",
    "$root\src\Apm.QueryService\Apm.QueryService.csproj",
    "$root\src\Apm.Gateway\Apm.Gateway.csproj"
)

$failed = $false

foreach ($proj in $projects) {
    Write-Host "`n--> Compiling $proj..." -ForegroundColor Yellow
    dotnet build "$proj" --no-restore
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed on $proj" -ForegroundColor Red
        $failed = $true
        break
    }
}

if (-not $failed) {
    Write-Host "`n[SUCCESS] All CloudPulse APM microservices built cleanly with 0 errors!" -ForegroundColor Green
} else {
    exit 1
}
