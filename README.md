<p align="center">
  <img src="Assets/logo.png" alt="Win Task Blocker Logo" width="128" height="128" />
</p>

<h1 align="center">Win Task Blocker</h1>

<p align="center">
  <strong>Modern, ultra-lightweight process blocker, conditional dependency guardian, and Windows startup manager for Windows 10 &amp; Windows 11.</strong>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20(x64)-0078D4?style=flat-square&logo=windows" alt="Windows 10/11" />
  <img src="https://img.shields.io/badge/.NET-8.0%20WPF%20ReadyToRun-0284C7?style=flat-square&logo=dotnet" alt=".NET 8.0" />
  <img src="https://img.shields.io/badge/Languages-English%20%26%20Spanish-22C55E?style=flat-square" alt="Bilingual EN/ES" />
  <img src="https://img.shields.io/badge/Version-2.0.1-38BDF8?style=flat-square" alt="v2.0.1" />
</p>

---

## 🌐 Bilingual Interface (English & Spanish)

**Win Task Blocker** is fully available in **both English and Spanish** (`EN` / `ES`):

---

## ✨ Key Features

1. **Native Acrylic Glass Interface & Dynamic Crystal Accents**:
2. **Instant Cold-Boot, Native Splash Screen & Minimized Windows Startup**:
   - Compiled with **ReadyToRun (R2R)** native precompilation and asynchronous background `.exe` icon resolution for near-instant cold startup, accompanied by a sleek **Splash Screen** featuring the official logo.
   - Includes **"Run on Windows startup (minimized)"** (`--startup --minimized`) and automatically starts in **Block Enabled** mode whenever the app launches (even if it was previously stopped).
3. **Real-Time Low-CPU Interception & Predictive Search**:
   - Event-driven WMI + lightweight process snapshot engine with clean **Block Enabled** / **Block Disabled** status and **Start** / **Stop** controls.
   - **Predictive Autocomplete Search**: Filter active processes or type executable names with instant suggestions, native `.exe` icon extraction (`Icon.ExtractAssociatedIcon`), and block counters.
   - **Context-Aware Top Search Bar**: Filters across **Blocked Tasks**, **Conditional Rules**, and **Windows Startup** entries, and automatically hides on tabs where search is not needed.
4. **Conditional Blocking & Dynamic Profiles**:
   - **Included Default Profiles**:
     - **Adobe Suite**: Blocks `adobe crash processor.exe`, `adobeipcbroker.exe`, `ccxprocess.exe`, `Adobe Desktop Service.exe`, `CoreSync.exe`, and `acrotray.exe` when `Photoshop.exe`, `Illustrator.exe`, `AfterFX.exe`, `Premiere Pro.exe`, and `Acrobat.exe` are closed.
     - **Gaming / Focus Mode**: Optionally blocks heavy background tools (`devenv.exe`, `java.exe`, `cscript.exe`) only while a game (`valorant.exe`, `cs2.exe`, `eldenring.exe`) is actively running.
   - **Dynamic Quick Profiles**: Adding, editing, or deleting profiles immediately updates the **Quick Profiles** sidebar.
5. **Windows Automatic Startup Manager**:
   - Inspect, enable, disable, add, or remove startup programs across the Windows Registry (`HKCU\...\Run`, `HKLM\...\Run`, `StartupApproved\Run`) and the Windows `Startup` folder without losing registry entries when disabled.
6. **Single-Instance Guard & System Tray Integration**:
   - Enforces a single running instance via a system-wide `Mutex`. Launching the executable a second time automatically brings the active instance to the foreground (even if minimized to the System Tray).
7. **100% Legacy `config.ini` Compatible**:
   - Reads and writes the classic `True|process.exe|count` format (`config.ini`) alongside extended JSON state (`taskblocker.state.json`) for conditional rules and visual preferences.

---

## 📦 Download & Build

### Download Portable Release
Download the latest single-file portable executable (`WinTaskBlocker.exe`) directly from the [**Releases**](https://github.com/irvingngr/win-task-blocker/releases) page. No installation required.

### Build from Source (.NET 8 SDK)
```powershell
dotnet publish WinTaskBlocker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:EnableCompressionInSingleFile=false -o publish
```

---

## 👤 Credits & Version Control

- **Crafted by**: **Irving Ngr**
- **Current Version**: `v2.0.1`
- **Compatibility**: Legacy Task Blocker v1.5 `config.ini` format
