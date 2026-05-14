param (
    [switch]$KeepOpen
)

Write-Host "Iniciando CardSwitchPlatform Environment..." -ForegroundColor Cyan

$baseDir = "d:\Jonathan\desarrollo\ZitronSystem"

# Function to start a dotnet project in a new window with a specific title
function Start-Microservice {
    param (
        [string]$Name,
        [string]$ProjectPath
    )
    Write-Host "Levantando Microservicio: $Name" -ForegroundColor Yellow
    $args = "-NoExit -Command `"& { `$host.ui.RawUI.WindowTitle = '$Name'; cd '$ProjectPath'; dotnet run }`""
    Start-Process powershell -ArgumentList $args
}

# Wait until Postgres accepts connections (TCP check on fixed port 5432)
function Wait-Postgres {
    param([int]$MaxWaitSeconds = 90)
    Write-Host "Esperando a que Postgres este listo (puerto 5432)..." -ForegroundColor Gray
    $elapsed = 0
    while ($elapsed -lt $MaxWaitSeconds) {
        try {
            $tcp = New-Object System.Net.Sockets.TcpClient
            $tcp.Connect("localhost", 5432)
            $tcp.Close()
            Write-Host "Postgres listo." -ForegroundColor Green
            return $true
        } catch {
            Start-Sleep -Seconds 3
            $elapsed += 3
        }
    }
    Write-Host "ADVERTENCIA: Postgres no respondio en $MaxWaitSeconds segundos." -ForegroundColor Red
    return $false
}

# Wait until SQL Server accepts connections
function Wait-SqlServer {
    param([int]$MaxWaitSeconds = 120)
    Write-Host "Esperando a que SQL Server este listo (puerto 11433)..." -ForegroundColor Gray
    $elapsed = 0
    while ($elapsed -lt $MaxWaitSeconds) {
        try {
            $tcp = New-Object System.Net.Sockets.TcpClient
            $tcp.Connect("localhost", 11433)
            $tcp.Close()
            Write-Host "SQL Server listo." -ForegroundColor Green
            return $true
        } catch {
            Start-Sleep -Seconds 3
            $elapsed += 3
        }
    }
    Write-Host "ADVERTENCIA: SQL Server no respondio en $MaxWaitSeconds segundos." -ForegroundColor Red
    return $false
}

# Start infrastructure
Write-Host "Levantando Infraestructura Base (Kafka, Postgres, SQLServer) via Docker..." -ForegroundColor Magenta
Start-Process docker -ArgumentList "compose -f $baseDir\backend\deploy\docker-compose.yml up -d" -Wait

# Health checks reales en lugar de sleep fijo
Wait-Postgres
Wait-SqlServer

# Extra buffer para Kafka (no tiene health-check TCP simple)
Write-Host "Esperando 5 segundos adicionales para Kafka..." -ForegroundColor Gray
Start-Sleep -Seconds 5

# Start Backend Microservices
Start-Microservice -Name "CardSwitch_CardVault" -ProjectPath "$baseDir\backend\services\CardVault\src\CardVault.Api"
Start-Microservice -Name "CardSwitch_IsoSwitch" -ProjectPath "$baseDir\backend\services\IsoSwitch\src\IsoSwitch.Api"
Start-Microservice -Name "CardSwitch_IsoAudit" -ProjectPath "$baseDir\backend\services\IsoAudit\src\IsoAudit.Api"

# Start Frontend
Write-Host "Levantando Frontend (Angular)..." -ForegroundColor Yellow
$frontendArgs = "-NoExit -Command `"& { `$host.ui.RawUI.WindowTitle = 'CardSwitch_Frontend'; cd '$baseDir\frontend'; npm run start }`""
Start-Process powershell -ArgumentList $frontendArgs

Write-Host "==========================================" -ForegroundColor Green
Write-Host "Ambiente levantado en ventanas separadas." -ForegroundColor Green
Write-Host "Frontend:     http://localhost:4200" -ForegroundColor Green
Write-Host "CardVault API: http://localhost:5101" -ForegroundColor Green
Write-Host "IsoSwitch API: http://localhost:5201" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
