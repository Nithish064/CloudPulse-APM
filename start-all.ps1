Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   CloudPulse APM - Starting All Microservices (.NET 10)  " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$root = $PSScriptRoot

$services = @(
    @{ Name = "Apm.IngestionService"; Path = "$root\src\Apm.IngestionService"; Port = 5001 },
    @{ Name = "Apm.AnomalyEngineService"; Path = "$root\src\Apm.AnomalyEngineService"; Port = 5002 },
    @{ Name = "Apm.QueryService"; Path = "$root\src\Apm.QueryService"; Port = 5003 },
    @{ Name = "Apm.Gateway"; Path = "$root\src\Apm.Gateway"; Port = 5000 }
)

$processes = @()

foreach ($svc in $services) {
    Write-Host "Starting $($svc.Name) on port $($svc.Port)..." -ForegroundColor Yellow
    $dllPath = "$($svc.Path)\bin\Debug\net10.0\$($svc.Name).dll"

    if (Test-Path $dllPath) {
        $p = Start-Process -FilePath "dotnet" -ArgumentList "`"$dllPath`" --urls http://localhost:$($svc.Port)" -PassThru
    } else {
        $p = Start-Process -FilePath "dotnet" -ArgumentList "run --no-build --project `"$($svc.Path)`" --urls http://localhost:$($svc.Port)" -PassThru
    }
    $processes += $p
    Start-Sleep -Milliseconds 600
}

Write-Host "`n[SUCCESS] All microservices launched!" -ForegroundColor Green
Write-Host "----------------------------------------------------------"
Write-Host " -> API Gateway & Web Dashboard:  http://localhost:5000" -ForegroundColor Cyan
Write-Host " -> Ingestion Service:            http://localhost:5001" -ForegroundColor Cyan
Write-Host " -> Anomaly Engine Service:       http://localhost:5002" -ForegroundColor Cyan
Write-Host " -> Query & Analytics Service:    http://localhost:5003" -ForegroundColor Cyan
Write-Host "----------------------------------------------------------"
Write-Host "Press Ctrl+C to terminate all services.`n"

try {
    while ($true) {
        Start-Sleep -Seconds 2
    }
}
finally {
    Write-Host "`nStopping microservices..." -ForegroundColor Yellow
    foreach ($p in $processes) {
        if (-not $p.HasExited) {
            Stop-Process -Id $p.Id -Force
        }
    }
    Write-Host "All microservices stopped." -ForegroundColor Red
}
