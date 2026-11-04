# OmniControl — Unified Windows PC Control Center

> **Your entire PC. One control center.**  
> A high-performance, production-quality Windows 11 desktop application combining system settings, administration utilities, hardware monitoring, performance management, process control, and device diagnostics into one unified Fluent Design interface.

---

## Architecture Overview

OmniControl is designed using **MVVM** and a clean, modular service-oriented architecture with strict privilege separation.

```
OmniControl.slnx
├── src/
│   ├── OmniControl.App/                  # WinUI 3 Desktop Client (Fluent Design Dark Theme)
│   │   ├── Views/                       # 13 Implemented Feature Pages
│   │   ├── ViewModels/                  # CommunityToolkit.Mvvm ViewModels
│   │   ├── Converters/                  # XAML Value Converters
│   │   ├── Styles/                      # Deep Charcoal / Navy Theme & Control Brushes
│   │   └── App.xaml.cs                  # Microsoft.Extensions.DependencyInjection configuration
│   │
│   ├── OmniControl.Core/                 # Domain Abstractions, Models, & Contracts
│   │   ├── Models/                      # Strongly typed Hardware, Process, Service, Storage DTOs
│   │   ├── Interfaces/                  # Clean service interfaces
│   │   ├── Results/                     # OperationResult & OperationResult<T>
│   │   └── AdminContract/               # Allowlisted IPC Contracts & OperationValidator
│   │
│   ├── OmniControl.Infrastructure/       # Native Windows Interop & API Integrations
│   │   ├── Hardware/                    # LibreHardwareMonitor, WMI, Win32 System Times & RAM
│   │   ├── Processes/                   # Real-time process inventory, delta CPU calculation, Task termination
│   │   ├── Startup/                     # Registry (HKCU/HKLM Run) & Startup Approved toggles
│   │   ├── EnvironmentVars/             # System/User env vars, PATH reordering, WM_SETTINGCHANGE broadcast
│   │   ├── Services/                    # ServiceControlManager, Start/Stop/Restart, Critical service guards
│   │   ├── AudioDisplay/                # Windows Core Audio COM APIs (master + per-app sessions) & DXVA2 brightness
│   │   ├── Network/                     # Adapter throughput, live Ping & DNS resolution tests
│   │   ├── Storage/                     # Drive capacities, temp file scanner & dry-run simulation, Recycle Bin
│   │   ├── Security/                    # Windows Defender, Firewall, and UAC inspection
│   │   ├── Persistence/                 # Local SQLite change history & backup checkpoints
│   │   └── WindowsInterop/              # Kernel32, User32, Shell32, Powrprof, Dxva2 P/Invoke & COM definitions
│   │
│   ├── OmniControl.AdminHelper/          # Dedicated Elevated Helper Process
│   │   ├── Communication/               # Named Pipe IPC Server (OmniControl_AdminHelper_Pipe)
│   │   └── Operations/                  # Allowlisted administrative operation executors
│   │
│   └── OmniControl.Tests/                # Automated Unit & System Integration Test Suite
│       ├── CoreAndInfrastructureTests.cs # Security validators, PATH duplicates, OperationResult tests
│       └── SystemIntegrationTests.cs     # Real Windows hardware, processes, services, storage, ping tests
```

---

## 13 Implemented Modules

1. **System Overview (Dashboard)**
   - Live CPU utilization, model, core/thread count, frequency, temperature.
   - Physical and committed RAM metrics with real-time memory pressure.
   - GPU utilization, dedicated VRAM, and thermal sensors.
   - Storage volume capacities with visual usage bars.
   - Live network upload and download throughput.
   - Windows build, system uptime, and battery telemetry.

2. **Performance Control**
   - Windows power scheme discovery and real-time active plan switching.
   - One-click performance profiles: *Balanced*, *High Performance*, *Power Saver*.
   - CPU clock speed and base vs current frequency monitoring.

3. **Process Manager (Task Manager Alternative)**
   - Searchable, sortable process inventory with real-time CPU% and memory metrics.
   - Application, Background, and Windows process categorization.
   - End Task (with protection against terminating essential Windows kernel processes).
   - Elevated process termination on demand via administrative helper.
   - Open File Location in File Explorer.

4. **Startup Manager**
   - Inventory of startup applications across `HKCU\Run`, `HKLM\Run`, `WOW6432Node`, and user/common Startup folders.
   - Safe, non-destructive Enable/Disable toggles using Windows `StartupApproved` binary registries.
   - Preserves original executable commands and arguments.

5. **Environment Variables & PATH Editor**
   - User vs System variable scope management.
   - Add, edit, and delete environment variables with audit logging.
   - Ordered PATH items editor with **Move Up**, **Move Down**, duplicate detection, and path existence validation.
   - Broadcasts `WM_SETTINGCHANGE` to notify running desktop programs without requiring a reboot.

6. **Windows Services Manager**
   - Complete inventory of installed Windows services with state and startup modes.
   - Safe Start, Stop, and Restart actions.
   - Guard rails preventing accidental termination of critical OS services (`RpcSs`, `PlugPlay`, `EventLog`, `DcomLaunch`, `SamSs`).
   - SCM startup type modifications (Automatic, Manual, Disabled).

7. **Hardware & Sensors**
   - Detailed component information (CPU architecture, Motherboard model, BIOS release, RAM speed).
   - Live sensor inventory powered by `LibreHardwareMonitorLib` with safe fallback for unsupported hardware.
   - Temperatures, Clocks, Fan RPM, Voltages, and Load percentages.

8. **Audio & Display Control**
   - Windows Core Audio master volume slider and mute controls.
   - Per-application audio mixer adjusting volumes and mute states for individual running processes.
   - Physical monitor detection and hardware DDC/CI / WMI screen brightness control.

9. **Network Manager**
   - Discovered network adapters (Ethernet, Wi-Fi, Virtual).
   - IPv4, IPv6, Default Gateway, DNS servers, and MAC addresses.
   - Live upload/download bandwidth speed indicators.
   - Integrated Ping latency and DNS resolution diagnostic tools.

10. **Storage Manager**
    - Drive capacity analysis and file system detection.
    - Category breakdown (Downloads, Documents, Temporary Files, AppData).
    - Safe Temporary File Cleanup with **Dry-Run Simulation** before deletion.
    - Recycle Bin inspection and one-click emptying.
    - Large File Discovery (>50 MB) in user directories.

11. **Security Center**
    - Real-time posture assessment for Windows Defender, Windows Firewall, and User Account Control (UAC).
    - Automated security recommendations with actionable inspection triggers.

12. **Audit History & Backups**
    - Local SQLite database (`%LOCALAPPDATA%\OmniControl\OmniControl.db`) recording all modifications.
    - Full undo support for reversible operations (environment variables, services, power schemes).
    - System configuration checkpoint snapshots.

13. **Settings & Global Search**
    - Global multi-domain search (Ctrl+K or search bar) indexing processes, services, environment variables, startup apps, and settings.
    - Configurable hardware polling refresh intervals (500ms to 5000ms).
    - Minimization to system tray, startup preferences, and diagnostic log export.

---

## Security & Privilege Separation Model

In adherence to security best practices, OmniControl does **not** run permanently elevated as Administrator:
1. The WinUI 3 interface runs as a standard user process.
2. Privileged system changes (such as system-wide environment variables, machine startup entries, and service startup configurations) communicate via an authenticated **Named Pipe** (`OmniControl_AdminHelper_Pipe`) to `OmniControl.AdminHelper.exe`.
3. The helper executes with explicit UAC consent and strictly validates all requests against an **allowlisted operation contract** (`OperationValidator`), rejecting arbitrary shell or PowerShell injection attempts.

---

## Build and Test Instructions

### Prerequisites
- Windows 10 (version 1809 or higher) or Windows 11
- .NET 10 SDK or .NET 8 LTS
- Windows App SDK

### Build the Solution
```powershell
dotnet build OmniControl.slnx
```

### Run All Unit & System Integration Tests
```powershell
dotnet test src/OmniControl.Tests/OmniControl.Tests.csproj
```

### Launch the Application
You have several ways to start OmniControl:

1. **Direct Executable (Root Folder):**
   - Double-click **`OmniControl.exe`** (or **`OmniControl.lnk`**) in the workspace root directory.
   - Launches natively as a Windows GUI application with no console window.

2. **Direct Release Binary:**
   - Run: `src\OmniControl.App\bin\Release\net10.0-windows10.0.26100.0\win-x64\OmniControl.App.exe`

3. **Command Line / Scripts:**
   - Double-click `run.bat` or run:
     ```powershell
     .\run.ps1
     ```
   - Or run via dotnet CLI:
     ```powershell
     dotnet run --project src/OmniControl.App/OmniControl.App.csproj
     ```

