# 🔧 Configurar Servicio para Impresión PDF

## ⚠️ Problema

Cuando ejecutas la aplicación **manualmente** (doble clic), funciona perfectamente. Pero cuando la ejecutas **como servicio de Windows**, no funciona porque:

1. Los servicios de Windows se ejecutan en **Session 0** (aislada de la sesión del usuario)
2. No tienen acceso a la interfaz gráfica del usuario
3. Las impresoras PDF virtuales como "Microsoft Print to PDF" requieren diálogos interactivos

## ✅ Solución

Hay **2 formas** de solucionarlo:

---

## 📋 **Opción 1: Configurar Servicio para Interactuar con Escritorio** (Más Fácil)

### Paso 1: Abrir Servicios de Windows

- Presiona `Win + R`
- Escribe: `services.msc`
- Presiona Enter

### Paso 2: Buscar el Servicio

- Busca **"LitePrint API Service"** en la lista
- Haz **click derecho** sobre él
- Selecciona **"Propiedades"**

### Paso 3: Configurar Inicio de Sesión

1. Ve a la pestaña **"Iniciar sesión"**
2. Selecciona **"Esta cuenta"**
3. Ingresa tu **usuario de Windows** (ejemplo: `TU-PC\TuUsuario` o `DOMINIO\Usuario`)
4. Ingresa tu **contraseña de Windows**
5. **IMPORTANTE**: Marca la casilla **"Permitir al servicio interactuar con el escritorio"**
6. Haz clic en **"Aplicar"** y luego **"Aceptar"**

### Paso 4: Reiniciar el Servicio

- Haz **click derecho** en "LitePrint API Service"
- Selecciona **"Reiniciar"**

---

## 📋 **Opción 2: Ejecutar Servicio como tu Usuario** (Recomendado)

### Paso 1: Abrir Servicios de Windows

- Presiona `Win + R`
- Escribe: `services.msc`
- Presiona Enter

### Paso 2: Buscar el Servicio

- Busca **"LitePrint API Service"**
- Haz **click derecho** → **"Propiedades"**

### Paso 3: Cambiar Cuenta del Servicio

1. Ve a la pestaña **"Iniciar sesión"**
2. Selecciona **"Esta cuenta"**
3. Haz clic en **"Examinar"**
4. Busca y selecciona **TU usuario de Windows**
5. Haz clic en **"Aceptar"**
6. Ingresa tu **contraseña de Windows** dos veces
7. Haz clic en **"Aplicar"** y luego **"Aceptar"**

### Paso 4: Reiniciar el Servicio

- Haz **click derecho** → **"Reiniciar"**

---

## 🔍 **Verificar que Funciona**

### Verificar Estado del Servicio:

```powershell
Get-Service LitePrintService
```

### Probar Impresión:

1. Usa el endpoint `/print` desde Postman o tu aplicación
2. Revisa los logs en: `http://localhost:9005/logs`
3. Si ves el diálogo de "Guardar como" de Windows, ¡está funcionando!

---

## ⚠️ **Notas Importantes**

1. **Seguridad**: Ejecutar el servicio como tu usuario le da más permisos. Asegúrate de que tu usuario tenga los permisos necesarios.

2. **Contraseña**: Si cambias tu contraseña de Windows, deberás actualizar la configuración del servicio.

3. **Impresoras PDF**: Incluso con esta configuración, "Microsoft Print to PDF" puede mostrar el diálogo de "Guardar como". Esto es **normal** y esperado.

4. **Impresoras Físicas**: Las impresoras físicas conectadas deberían funcionar sin problemas.

---

## 🐛 **Si Todavía No Funciona**

### Verificar Logs:

```powershell
# Ver logs en tiempo real
Invoke-RestMethod -Uri "http://localhost:9005/logs" | ConvertTo-Json -Depth 10
```

### Verificar que el Servicio Está Corriendo:

```powershell
sc.exe query LitePrintService
```

### Reinstalar el Servicio:

1. Detener el servicio:

   ```powershell
   Stop-Service LitePrintService
   sc.exe delete LitePrintService
   ```

2. Ejecutar el script de instalación de nuevo:

   ```powershell
   cd C:\ruta\a\printer_api\LitePrintApi
   .\install-service.ps1
   ```

3. Configurar el servicio según las opciones de arriba

---

## 💡 **Alternativa: Usar Impresora Física**

Si las impresoras PDF virtuales siguen dando problemas, puedes:

- Usar una **impresora física** conectada o en red
- Instalar una impresora PDF alternativa como **CutePDF Writer** o **PDFCreator** que permitan configurar rutas automáticas

---

¡Listo! Con estos pasos, el servicio debería poder imprimir correctamente. 🎉
