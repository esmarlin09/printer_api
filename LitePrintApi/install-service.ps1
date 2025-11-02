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

# Configurar servicio para permitir interactuar con escritorio
# NOTA: Esto puede no funcionar en versiones modernas de Windows
# Es mejor configurar manualmente el servicio para ejecutarse como tu usuario
Write-Host "Configurando servicio..." -ForegroundColor Yellow
# sc.exe config LitePrintService type= interact

# NOTA: Para que las impresoras PDF funcionen, el servicio debe ejecutarse como el usuario actual
# O configurar para permitir interactuar con el escritorio
Write-Host ""
Write-Host "IMPORTANTE: Para que las impresoras PDF funcionen correctamente:" -ForegroundColor Yellow
Write-Host "1. Abre 'services.msc' (Servicios)" -ForegroundColor Cyan
Write-Host "2. Busca 'LitePrint API Service'" -ForegroundColor Cyan
Write-Host "3. Click derecho -> Propiedades" -ForegroundColor Cyan
Write-Host "4. Pestaña 'Iniciar sesión'" -ForegroundColor Cyan
Write-Host "5. Selecciona 'Esta cuenta' y pon tu usuario de Windows y contraseña" -ForegroundColor Cyan
Write-Host "6. O marca 'Permitir al servicio interactuar con el escritorio'" -ForegroundColor Cyan
Write-Host ""

# Iniciar servicio
Write-Host "Iniciando servicio..." -ForegroundColor Yellow
sc.exe start LitePrintService

# Esperar y verificar
Start-Sleep -Seconds 5
sc.exe query LitePrintService

Write-Host "`nServicio instalado correctamente!" -ForegroundColor Green
Write-Host "API disponible en: http://localhost:9005" -ForegroundColor Cyan
Write-Host "Swagger: http://localhost:9005/swagger" -ForegroundColor Cyan
