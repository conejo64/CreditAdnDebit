Write-Host "Bajando CardSwitchPlatform Environment..." -ForegroundColor Red

# Find all powershell or cmd windows with our custom title and stop them
$processes = Get-Process | Where-Object { $_.MainWindowTitle -like "CardSwitch_*" }

if ($processes) {
    foreach ($p in $processes) {
        Write-Host "Matando proceso ventana: $($p.MainWindowTitle) (PID: $($p.Id))" -ForegroundColor Yellow
        Stop-Process -Id $p.Id -Force
    }
    Write-Host "==========================================" -ForegroundColor Green
    Write-Host "Ambiente deshabilitado (Ventanas cerradas)." -ForegroundColor Green
    Write-Host "==========================================" -ForegroundColor Green
}
else {
    Write-Host "No se encontraron ventanas activas con el título 'CardSwitch_'. Intentando buscar procesos huérfanos..." -ForegroundColor DarkYellow
}

$baseDir = "d:\Jonathan\desarrollo\ZitronSystem"
Write-Host "Bajando Infraestructura Base (Docker)..." -ForegroundColor Magenta
Start-Process docker -ArgumentList "compose -f $baseDir\backend\deploy\docker-compose.yml down" -Wait
Write-Host "Infraestructura Base abajo." -ForegroundColor Green
