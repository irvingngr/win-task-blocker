<p align="center">
  <img src="Assets/logo.png" alt="Win Task Blocker Logo" width="128" height="128" />
</p>

<h1 align="center">Win Task Blocker v2.0.0 — Acrylic Glass Edition</h1>

<p align="center">
  <strong>Modern, ultra-lightweight process blocker, conditional dependency guardian, and Windows startup manager for Windows 10 &amp; Windows 11.</strong>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20(x64)-0078D4?style=flat-square&logo=windows" alt="Windows 10/11" />
  <img src="https://img.shields.io/badge/.NET-8.0%20WPF-512BD4?style=flat-square&logo=dotnet" alt=".NET 8.0" />
  <img src="https://img.shields.io/badge/Languages-English%20%26%20Spanish-22C55E?style=flat-square" alt="Bilingual EN/ES" />
  <img src="https://img.shields.io/badge/Version-2.0.0-A855F7?style=flat-square" alt="v2.0.0" />
</p>

---

## 🌐 Bilingual Interface (English & Spanish)

**Win Task Blocker** is fully available in **both English and Spanish** (`EN` / `ES`):
- Launches in **English** by default.
- Switch between **English** and **Spanish** at any time with a single click using the **flag toggle button** in the top titlebar. Your language preference is automatically saved.

---

## ✨ Key Features

1. **Native Acrylic Glass Interface & Custom Crystal Finishes**:
   - Borderless integrated header with native Windows 10/11 DWM Acrylic Blur (`SetWindowCompositionAttribute` + `DwmSetWindowAttribute`) and rounded corners.
   - Real-time **Crystal Opacity Slider** (`30%` – `95%`) and 4 selectable crystal finishes (**Purple Nebula**, **Midnight Dark**, **Windows Blue**, and **Arctic Cyan**), automatically persisted across sessions.
2. **Real-Time Low-CPU Interception & Predictive Search**:
   - Event-driven WMI + lightweight process snapshot engine that intercepts and terminates blocked executables in real time with minimal resource usage.
   - **Predictive Autocomplete Search**: Filter active processes or type executable names with instant suggestions, native `.exe` icon extraction (`Icon.ExtractAssociatedIcon`), and block counters.
   - **Context-Aware Top Search Bar**: Filters across **Blocked Tasks**, **Conditional Rules**, and **Windows Startup** entries, and automatically hides on tabs where search is not needed.
3. **Conditional Blocking & Dynamic Profiles**:
   - **Parent/Child Dependency Guardian**: Block background services or telemetry (such as `adobe crash processor.exe`, `adobeipcbroker.exe`, `ccxprocess.exe`) **ONLY when their main parent applications (`Photoshop.exe`, `Illustrator.exe`, `Premiere Pro.exe`, `AfterFX.exe`) are closed**, and automatically clean them up when the parent app exits.
   - **Dynamic Quick Profiles**: Group processes into profiles (*Adobe Suite*, *Logitech G HUB*, *Remote Access*, *Gaming / Focus Mode*, or custom profiles). Adding, editing, or deleting profiles immediately updates the **Quick Profiles** sidebar.
4. **Windows Automatic Startup Manager**:
   - Inspect, enable, disable, add, or remove startup programs across the Windows Registry (`HKCU\...\Run`, `HKLM\...\Run`, `StartupApproved\Run`) and the Windows `Startup` folder without losing registry entries when disabled.
5. **Single-Instance Guard & System Tray Integration**:
   - Enforces a single running instance via a system-wide `Mutex`. Launching the executable a second time automatically brings the active instance to the foreground (even if minimized to the System Tray).
6. **100% Legacy `config.ini` Compatible**:
   - Reads and writes the classic `True|process.exe|count` format (`config.ini`) alongside extended JSON state (`taskblocker.state.json`) for conditional rules and visual preferences.

---

## 📦 Download & Build

### Download Portable Release
Download the latest single-file portable executable (`WinTaskBlocker.exe`) directly from the [**Releases**](https://github.com/irvingngr/win-task-blocker/releases) page. No installation required.

### Build from Source (.NET 8 SDK)
```powershell
dotnet publish WinTaskBlocker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

---

## 👤 Credits & Version Control

- **Crafted by**: **Irving Ngr** 🇻🇪
- **Current Version**: `v2.0.0`
- **Compatibility**: Legacy Task Blocker v1.5 `config.ini` format
