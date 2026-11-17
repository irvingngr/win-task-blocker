# 🛡️ Task Blocker v2.0 — Acrylic Glass Edition (Windows 10/11)

Sucesor moderno, ligero y de código abierto del clásico **Task Blocker v1.5**, construido en **C# / .NET 8 (WPF)** con diseño **Wintoys** (barra integrada sin bordes) y cristal translúcido **Acrylic Blur** (estilo `example.jpg`).

---

## ✨ Características Principales

1. **Ventana Integrada Wintoys + Transparencia Acrylic Glass**:
   - Sin bordes antiguos de Windows: barra superior integrada con buscador global, indicador de estado en vivo y controles de ventana.
   - Efecto **Acrylic Blur** nativo de Windows 10/11 (`SetWindowCompositionAttribute` + `DwmSetWindowAttribute`) con control deslizante de opacidad en tiempo real y temas de cristal (*Nebulosa Violeta*, *Cristal Wintoys* y *Cian Ártico*).
2. **Búsqueda Predictiva + Íconos Reales de cada Programa**:
   - Extrae automáticamente el ícono nativo del ejecutable `.exe` (`Icon.ExtractAssociatedIcon`) o genera insignias vectoriales modernas si el programa aún no está instalado.
   - Campo de escritura predictiva con autocompletado instantáneo sobre los procesos activos.
3. **Apartado Dedicado de Bloqueo Condicional y Perfiles**:
   - **Guardián Condicional (Padre / Hijo)**: Permite bloquear procesos secundarios o telemetría (ej. `adobe crash processor.exe`, `adobeipcbroker.exe`, `ccxprocess.exe`) **SOLO cuando ningún programa principal (`Photoshop.exe`, `Illustrator.exe`, `Premiere Pro.exe`, `AfterFX.exe`) esté abierto**, y limpiarlos automáticamente en cuanto cierres el programa principal.
   - **Perfiles y Paquetes**: Activa o desactiva grupos enteros (*Suite Adobe*, *Logitech G HUB*, *Acceso Remoto*, *Modo Gaming*) con un solo clic.
4. **Gestor de Inicio Automático de Windows (Startup Manager)**:
   - Administra los programas que inician con Windows desde el Registro (`HKCU\...\Run`, `HKLM\...\Run`, `StartupApproved\Run`) y la carpeta `Startup` sin borrar las entradas al deshabilitarlas.
5. **100% Compatible con tu `config.ini` clásico**:
   - Lee y guarda directamente el formato `True|proceso.exe|conteo` (ej. `True|adobeipcbroker.exe|23`).

---

## 🚀 Cómo Compilar o Descargar el `.exe`

### Opción 1: Descargar desde GitHub Actions (Sin instalar nada)
Cada push ejecuta el flujo `.github/workflows/build-windows.yml` que genera el ejecutable **portable de un solo archivo** (`TaskBlocker.exe` + `config.ini`) listo para descargar desde la pestaña **Actions** del repositorio.

### Opción 2: Compilar localmente en Windows con .NET 8 SDK
```powershell
dotnet publish WinTaskBlocker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```
