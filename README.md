# LitePrint API

API REST para impresión de PDFs usando Ghostscript en Windows.

## Endpoints

- **GET** `http://localhost:9005/healthz` - Health check
- **GET** `http://localhost:9005/printers` - Lista de impresoras disponibles
- **POST** `http://localhost:9005/print` - Imprimir PDF
- **GET** `http://localhost:9005/swagger` - Documentación API

## Compilación y Ejecución

### Desarrollo
```bash
dotnet run --project LitePrintApi
```

### Producción - Servicio de Windows

#### Instalación Automática (Recomendado)
```powershell
# PowerShell como Administrador
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope Process
cd LitePrintApi
.\install-service.ps1
```

#### Instalación Manual
```powershell
# Compilar
dotnet publish -c Release -o C:\PrinterService

# Instalar servicio (PowerShell como Administrador)
sc.exe create LitePrintService binPath="C:\PrinterService\LitePrintApi.exe" start=auto DisplayName="LitePrint API Service"

# Iniciar servicio
sc.exe start LitePrintService

# Ver estado
sc.exe query LitePrintService

# Desinstalar (si es necesario)
sc.exe stop LitePrintService
sc.exe delete LitePrintService
```

### Notas Importantes
- El servicio se ejecutará en `http://localhost:9005`
- Los logs del servicio se pueden ver en el **Visor de Eventos** de Windows
- El servicio se iniciará automáticamente con Windows

## Requisitos

- Windows
- Ghostscript instalado (version 10.01.2 - 10.05.0)
- .NET 6.0 Runtime

## Instalación de Ghostscript

Descargar desde: https://www.ghostscript.com/download/gsdnld.html

El servicio busca automáticamente en:
- `C:\Program Files\gs\gs10.05.0\bin\gswin64c.exe`
- `C:\Program Files\gs\gs10.04.0\bin\gswin64c.exe`
- `C:\Program Files (x86)\gs\gs10.05.0\bin\gswin32c.exe`
- Y versiones anteriores 10.03.0, 10.02.1, 10.01.2