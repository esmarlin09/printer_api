# 📋 Guía Completa: Instalación y Gestión del Servicio LitePrint

## ✅ Requisitos Previos

1. **Windows** (el servicio solo funciona en Windows)
2. **.NET 6.0 Runtime** instalado
3. **Ghostscript** instalado (versiones 10.01.2 - 10.05.0)
   - Descargar desde: https://www.ghostscript.com/download/gsdnld.html
4. **PowerShell** ejecutándose como **Administrador**

---

## 🚀 PASO 1: Compilar el Proyecto

### Opción A: Desde Visual Studio o terminal

```powershell
# Navegar a la carpeta del proyecto
cd C:\ruta\a\printer_api\LitePrintApi

# Compilar y publicar
dotnet publish -c Release -o C:\PrinterService
```

### Opción B: Usar el script automático (más fácil)

El script `install-service.ps1` ya compila automáticamente, así que puedes saltar este paso si usas la instalación automática.

---

## 🔧 PASO 2: Instalar el Servicio

### **Opción A: Instalación Automática (Recomendada)**

1. **Abrir PowerShell como Administrador:**

   - Presiona `Win + X`
   - Selecciona "Windows PowerShell (Administrador)" o "Terminal (Administrador)"

2. **Permitir ejecución de scripts:**

   ```powershell
   Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope Process
   ```

3. **Navegar a la carpeta del proyecto:**

   ```powershell
   cd C:\ruta\a\printer_api\LitePrintApi
   ```

   _(Reemplaza con tu ruta real)_

4. **Ejecutar el script de instalación:**

   ```powershell
   .\install-service.ps1
   ```

5. **El script hará automáticamente:**
   - ✅ Compilar el proyecto
   - ✅ Detener el servicio si existe
   - ✅ Eliminar el servicio antiguo
   - ✅ Crear el nuevo servicio
   - ✅ Iniciar el servicio

---

### **Opción B: Instalación Manual**

1. **Compilar (si no lo hiciste antes):**

   ```powershell
   dotnet publish -c Release -o C:\PrinterService
   ```

2. **Crear el servicio:**

   ```powershell
   sc.exe create LitePrintService binPath="C:\PrinterService\LitePrintApi.exe" start=auto DisplayName="LitePrint API Service"
   ```

3. **Iniciar el servicio:**

   ```powershell
   sc.exe start LitePrintService
   ```

4. **Verificar que se inició correctamente:**
   ```powershell
   sc.exe query LitePrintService
   ```

---

## ▶️ PASO 3: Verificar que el Servicio Está Funcionando

### **Ver el Estado del Servicio:**

```powershell
# Ver estado detallado
sc.exe query LitePrintService

# Ver si está corriendo (más simple)
Get-Service LitePrintService
```

**Deberías ver:**

```
SERVICE_NAME: LitePrintService
        STATE              : 4  RUNNING
```

### **Probar que la API responde:**

Abre un navegador y ve a:

- **Health Check:** http://localhost:9005/health
- **Swagger (Documentación):** http://localhost:9005/swagger
- **Listar Impresoras:** http://localhost:9005/printers

---

## 📊 PASO 4: Ver los Logs del Servicio

### **Método 1: Visor de Eventos de Windows (Recomendado)**

1. **Abrir el Visor de Eventos:**

   - Presiona `Win + R`
   - Escribe: `eventvwr.msc` y presiona Enter
   - O busca "Visor de eventos" en el menú de inicio

2. **Navegar a los logs:**

   - En el panel izquierdo, expande: **Windows Logs**
   - Haz clic en **Application**

3. **Filtrar por el servicio:**

   - En el panel derecho, haz clic en **"Filtrar registro actual"**
   - En "Origen del evento", busca y selecciona: **LitePrintService**
   - O busca en "Todos los eventos" escribiendo "LitePrint" en el campo de búsqueda

4. **Ver los logs:**
   - Verás eventos con nivel:
     - 🔵 **Información** (azul) - Operaciones normales
     - 🟡 **Advertencia** (amarillo) - Warnings
     - 🔴 **Error** (rojo) - Errores

### **Método 2: PowerShell (Ver logs en tiempo real)**

```powershell
# Ver los últimos 50 eventos
Get-EventLog -LogName Application -Source "LitePrintService" -Newest 50 | Format-Table TimeGenerated, EntryType, Message -AutoSize

# Ver solo errores
Get-EventLog -LogName Application -Source "LitePrintService" -EntryType Error -Newest 20

# Ver en tiempo real (como tail -f)
Get-EventLog -LogName Application -Source "LitePrintService" -Newest 1 -Wait
```

### **Método 3: Ver logs desde línea de comandos**

```powershell
# Ver últimos eventos
wevtutil qe Application /c:50 /rd:true /f:text /q:"*[System[Provider[@Name='LitePrintService']]]"
```

---

## 🛠️ COMANDOS ÚTILES PARA ADMINISTRAR EL SERVICIO

### **Iniciar el servicio:**

```powershell
sc.exe start LitePrintService
# O
Start-Service LitePrintService
```

### **Detener el servicio:**

```powershell
sc.exe stop LitePrintService
# O
Stop-Service LitePrintService
```

### **Reiniciar el servicio:**

```powershell
Restart-Service LitePrintService
```

### **Ver el estado:**

```powershell
sc.exe query LitePrintService
# O
Get-Service LitePrintService
```

### **Desinstalar el servicio:**

```powershell
# Primero detener
sc.exe stop LitePrintService

# Luego eliminar
sc.exe delete LitePrintService
```

---

## 🔍 TROUBLESHOOTING (Solución de Problemas)

### **El servicio no inicia:**

1. Verifica que .NET 6.0 Runtime esté instalado
2. Verifica que Ghostscript esté instalado
3. Revisa los logs en el Visor de Eventos
4. Asegúrate de ejecutar PowerShell como Administrador

### **No puedo ver los logs:**

1. Verifica que el servicio esté corriendo
2. Busca "LitePrintService" en el Visor de Eventos
3. Si no aparecen logs, verifica que el servicio tenga permisos para escribir en el Event Log

### **La API no responde:**

1. Verifica que el servicio esté corriendo: `sc.exe query LitePrintService`
2. Verifica que el puerto 9005 no esté bloqueado por firewall
3. Intenta acceder desde otro navegador o herramienta como Postman

### **Error al imprimir:**

1. Revisa los logs en el Visor de Eventos (Método 1 arriba)
2. Busca eventos con nivel "Error" (rojo)
3. Verifica que:
   - Ghostscript esté instalado correctamente
   - La impresora exista (usa `/printers` para listarlas)
   - El nombre de la impresora coincida exactamente

---

## 📝 NOTAS IMPORTANTES

- ✅ El servicio se iniciará automáticamente al reiniciar Windows
- ✅ El servicio escucha en: **http://localhost:9005**
- ✅ Todos los logs se guardan en el **Visor de Eventos de Windows**
- ✅ El servicio debe ejecutarse con permisos de administrador para acceder a las impresoras

---

## 🧪 PROBAR LA IMPRESIÓN

1. **Listar impresoras disponibles:**

   ```powershell
   Invoke-WebRequest -Uri "http://localhost:9005/printers" | Select-Object -ExpandProperty Content
   ```

2. **Probar impresión (ejemplo con Postman o curl):**

   ```powershell
   $body = @{
       Printer = "Nombre de tu impresora"
       Base64Pdf = "JVBERi0xLjQKJcfsj6IKNSAwIG9iago8PAovVHlwZSAvQ2F0YWxvZw..."
       Copies = 1
       RemoveMargins = $false
   } | ConvertTo-Json

   Invoke-RestMethod -Uri "http://localhost:9005/print" -Method Post -Body $body -ContentType "application/json"
   ```

3. **Revisar los logs inmediatamente después** para ver si hubo algún error.

---

¡Listo! Ahora deberías poder instalar, ejecutar y monitorear el servicio sin problemas. 🎉
