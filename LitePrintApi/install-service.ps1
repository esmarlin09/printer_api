# Instalar LitePrint Service
# Ejecutar como Administrador: Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope Process

Write-Host "Instalando LitePrint Service..." -ForegroundColor Green

# Compilar
Write-Host "Compilando..." -ForegroundColor Yellow
dotnet publish -c Release -o C:\PrinterService

# Detener servicio si existe
Write-Host "Deteniendo servicio si existe..." -ForegroundColor Yellow
Stop-Service LitePrintService -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# Eliminar servicio si existe
Write-Host "Eliminando servicio antiguo si existe..." -ForegroundColor Yellow
sc.exe delete LitePrintService 2>$null

# Crear servicio
Write-Host "Creando servicio..." -ForegroundColor Yellow
sc.exe create LitePrintService binPath="C:\PrinterService\LitePrintApi.exe" start=auto DisplayName="LitePrint API Service"

# Iniciar servicio
Write-Host "Iniciando servicio..." -ForegroundColor Yellow
sc.exe start LitePrintService

# Esperar y verificar
Start-Sleep -Seconds 5
sc.exe query LitePrintService

Write-Host "`nServicio instalado correctamente!" -ForegroundColor Green
Write-Host "API disponible en: http://localhost:9005" -ForegroundColor Cyan
Write-Host "Swagger: http://localhost:9005/swagger" -ForegroundColor Cyan
