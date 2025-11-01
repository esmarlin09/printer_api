# LitePrint API

API REST para impresión de PDFs usando Ghostscript en Windows.

## Endpoints

- **GET** `/healthz` - Health check
- **GET** `/printers` - Lista de impresoras disponibles
- **POST** `/print` - Imprimir PDF
- **GET** `/swagger` - Documentación API

## Compilación y Ejecución

### Desarrollo
```bash
dotnet run --project LitePrintApi
```

### Producción
```bash
dotnet publish -c Release -o C:\PrinterService
```

### Ejecutar Minimizado

Opción 1: Usar el CMD
```cmd
start-minimized.cmd
```

Opción 2: Crear acceso directo
1. Crear un acceso directo a `LitePrintApi.exe`
2. Click derecho → Propiedades
3. En "Ejecutar" seleccionar "Minimizado"

### Agregar al Inicio de Windows

Para que la app inicie automáticamente con Windows:
1. Win + R → `shell:startup`
2. Crear acceso directo del ejecutable en esa carpeta
3. Configurar propiedades del acceso directo para "Minimizado"

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