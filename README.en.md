<p align="center">
    <img src="./ContextMenuMgr.Frontend/Assets/AppIcon.png" style="height: 100px; width: 100px" />
</p>
<h1 align="center">
  <span>Context Menu Manager Plus</span>
</h1>
<p align="center">
  <span align="center">Context Menu Manager Plus is a powerful utility that help you manage you context menu on Windows and avoid third party to add rubish to your context menu.</span>
</p>

[中文版 README](./README.md)

> [!WARNING]
> A significant portion of this project was generated with AI assistance and then continuously revised, tested, and reshaped by hand. It may still contain gaps, edge-case regressions, or behavior that does not fully match expectations.
> If you hit bugs, compatibility issues, missing documentation, or surprising behavior, please open an Issue. Reproduction steps, logs, screenshots, and Windows version details are especially helpful.

## Overview

`Context Menu Manager Plus` is a Windows context menu management tool.

It is not just a simple enable/disable switcher. It is designed around the following goals:

- manage classic Windows context menu items
- manage Windows 11 modern context menu items
- detect new context menu entries added by third-party software
- intercept new items first, then let the user review them
- protect context-menu-related registry locations from unwanted modifications
- provide global search, page-level filtering, and runtime menu analysis
- keep the app usable through a coordinated frontend / backend service / tray host model

In short, the goal is:

> Turn the Windows context menu from “software can add whatever it wants” into “the user decides what is allowed to stay”.

The project is built with:

- `.NET 10`
- `WPF`
- `WPF-UI`
- `Named Pipe IPC`
- `Windows Service`
- native Win32 tray host
- isolated ProbeHost helper process

---

## Core Features

### Intercept First, Review Second

This is the most important design goal of the project.

When a new context menu item is detected, the app tries not to let it silently become active. Instead, it follows this workflow as much as possible:

1. the backend service detects a new menu item
2. the item is marked as pending review
3. the item is intercepted or disabled first
4. the item appears on the approvals page
5. the user manually decides what to do with it

Available review actions include:

- `Allow`: allow and enable the item
- `Keep disabled`: keep the item but leave it disabled
- `Remove`: remove the item from the registry or from the review queue

This approval-first workflow is the main difference between this project and ordinary context menu managers.

---

### Backend-Driven Multi-Process Model

The project uses a backend-driven architecture.

Main processes:

- `ContextMenuManagerPlus.exe`
  - WPF frontend
  - handles UI and user interaction
  - exits when the window is closed
- `ContextMenuManagerPlus.Service.exe`
  - backend Windows Service
  - handles scanning, state management, registry operations, approval logic, and IPC
- `ContextMenuManagerPlus.TrayHost.exe`
  - per-user tray host
  - handles tray icon, notifications, tray menu, and launching the frontend
- `ContextMenuMgr.ProbeHost.exe`
  - isolated runtime analysis helper
  - loads third-party Shell Extensions outside the main app
  - a crash in ProbeHost should not take down the frontend or backend service

The frontend is not a resident background process. The tray is owned by a separate TrayHost process, while the backend service owns the core logic.

---

### Classic Menu And Windows 11 Modern Menu Support

The project handles both major context menu systems:

- classic context menus
  - `shell`
  - `shellex\ContextMenuHandlers`
  - file, folder, directory background, desktop background, and other classic scenes
- Windows 11 modern context menus
  - Packaged COM
  - AppX / MSIX related entries
  - user-level block / restore behavior on the Windows 11 menu page

Classic menus and Windows 11 modern menus are registered differently, so they are processed through separate internal paths.

---

### Global Search

The title bar contains a global search box.

This is not a page filter. It is a global search-and-jump tool.

It supports searching by:

- menu item title
- command text
- registry path
- backend registry path
- DLL / EXE file path
- CLSID
- Windows 11 package information and COM Server path

Search results show:

- the actual menu item icon when available
- menu item title
- enabled / disabled state
- scene label or Windows 11 menu label
- a jump hint

Selecting a search result will:

1. navigate to the corresponding page
2. automatically fill the target page’s filter box with the selected item title
3. make the target page show only or mostly that item

Global search works from frontend in-memory data. It does not query the backend, registry, or file system on every keystroke.

---

### Multi-Field Page Filtering

Page-level filter boxes also support multi-field search.

Searchable fields include:

- display name
- key name
- command text
- registry path
- backend registry path
- Shell Extension CLSID
- DLL / EXE file path
- notes
- Windows 11 package name, publisher, COM Server path, and related metadata

Global search and page-level filtering try to share the same matching logic, so results feel consistent.

---

### Shell Extension Deep Analysis

For Shell Extension / DLL-based menu items, the app provides a “Deep Analysis” feature.

It attempts to load and probe the selected Shell Extension inside an isolated ProbeHost process, then tries to resolve the actual menu text inserted by that extension.

Important limitations:

- this is a best-effort feature
- not every Shell Extension can be initialized in isolation
- some extensions depend on Explorer’s full runtime environment
- some entries only appear for specific file types, states, or user sessions
- some entries are owner-drawn and may not expose normal text
- failure to analyze an item is usually normal and does not mean enable/disable behavior is broken

In other words, Deep Analysis failures usually do not need to be reported as bugs unless they crash the main app, break normal item management, or cause clearly incorrect behavior.

---

### ProbeHost Isolation

The project does not directly load third-party Shell Extension DLLs inside the frontend or backend service.

A Shell Extension is a third-party in-process COM component. It may:

- crash
- hang
- access invalid memory
- depend on Explorer internals
- require a different process architecture

To isolate that risk, the app uses a separate `ProbeHost` process for runtime analysis.

ProbeHost rules:

- starts only when the user clicks Deep Analysis
- does not participate in normal scanning
- does not write to the registry
- does not execute menu commands
- has a timeout
- reports structured results or failures to the frontend

Release packages may include multiple ProbeHost architectures:

- `x86`
- `x64`
- `arm64`

On Windows on ARM, the app chooses the helper based on the target DLL architecture, for example:

- ARM64 Shell Extension → ARM64 ProbeHost
- x64 Shell Extension → x64 ProbeHost
- x86 Shell Extension → x86 ProbeHost

---

### Registry Protection Enhancements

The Settings page includes context-menu-related registry protection switches.

The goal is to reduce unwanted menu item creation or modification by third-party software.

Important notes:

- this feature changes registry permissions
- some installers, driver installers, security software, archive tools, or cloud clients may fail to write context menu entries while protection is enabled
- if you need to install software that adds context menu entries, you may need to temporarily disable the protection
- if the app itself is blocked from editing a menu item by the protection, it will ask you to unlock the protection first

This is an advanced feature. It is not recommended to enable it blindly if you do not understand the consequences.

---

### Special Menu Management

The project also handles some special menus:

- New menu
- Send To
- Win + X menu

These areas behave differently from ordinary `shell` / `shellex` entries. Some operations involve user-level registry locations, ordering, hiding, restoring, or ACL-related state, so they are handled separately.

---

## Features

### Classic Context Menu Management

Supported scenes include:

- File
- All Objects
- Folder
- Directory
- Directory Background
- Desktop Background
- Drive
- Library
- This PC
- Recycle Bin

Supported actions include:

- enable / disable menu items
- delete menu items
- undo delete
- permanently remove delete backups
- edit some display names
- view command text
- open registry location
- open CLSID location
- open file location
- search and filter
- parse icons, display names, command text, and CLSID metadata

---

### Windows 11 Modern Context Menu Management

The app can scan and manage Windows 11 modern context menu entries.

Features include:

- enumerate Windows 11 modern menu items
- view package name, publisher, context types, COM Server path, and related information
- enable / disable user-level Windows 11 modern menu entries
- integrate with the approval workflow
- page-level search and filtering
- global search navigation

Windows 11 modern menu items use a different registration model from classic menus, so the app builds and manages their snapshots separately.

---

### Review Queue

New menu items are placed into the approvals page.

The approvals page supports:

- Allow
- Keep disabled
- Remove
- grouping multiple category sources for the same logical item

When a new pending item is detected:

1. backend broadcasts an event
2. TrayHost shows a system notification
3. the user clicks the notification
4. the frontend is launched and navigates to the approvals page

---

### External Change Tracking

The app tries to detect external changes, such as:

- third-party software adding new menu items
- enable/disable state changes while the guard was offline
- registry state diverging from the local state store
- known items being restored or deleted externally

This is not intended to replace a full system audit mechanism, but it helps users notice when software modifies the context menu unexpectedly.

---

### File Type And Rules Pages

The File Types page covers:

- shortcuts
- UWP shortcuts
- executables
- custom extensions
- perceived types
- directory types
- unknown types

The Other Rules page covers:

- Enhance Menu
- Detailed Edit
- custom registry paths
- other context-menu-related rules

These pages are intended for more detailed and lower-level registry rules.

---

### Settings Page

The Settings page provides:

- language
  - follow system
  - Simplified Chinese
  - English (United States)
- theme
  - follow system
  - light
  - dark
- log level
- start with Windows
- install / repair service
- uninstall service
- restart Explorer
- open logs folder
- open state folder
- open config folder
- registry protection enhancement switches

Theme settings are loaded and applied during startup so the app opens directly in the selected appearance.

---

## Architecture

### 1. Backend Service

Project: `ContextMenuMgr.Backend`  
Executable: `ContextMenuManagerPlus.Service.exe`

Backend Service is the main controller.

Responsibilities include:

- scanning and parsing context-menu-related registry entries
- building classic menu and Windows 11 modern menu snapshots
- saving and merging the local state store
- applying enable / disable / delete / restore operations
- applying approval decisions
- performing registry operations that require elevated permissions
- exposing Named Pipe IPC
- broadcasting state changes and approval notifications
- launching TrayHost for the active user session when appropriate

---

### 2. Frontend

Project: `ContextMenuMgr.Frontend`  
Executable: `ContextMenuManagerPlus.exe`

Frontend is the WPF UI layer.

Responsibilities include:

- main window
- category pages
- approvals page
- settings page
- Windows 11 modern menu page
- special menu pages
- global search
- page-level filtering
- Deep Analysis result window
- IPC with Backend
- single-instance activation and page navigation through control pipe

The frontend is UI-only:

- closing the window exits the frontend process
- it does not own the tray
- it does not directly host third-party Shell Extension analysis

---

### 3. Tray Host

Project: `ContextMenuMgr.TrayHost`  
Executable: `ContextMenuManagerPlus.TrayHost.exe`

TrayHost is a per-user tray process.

Responsibilities include:

- tray icon
- tray menu
- system notifications
- opening the frontend main window
- opening the approvals page
- requesting backend shutdown
- responding to localization refresh

TrayHost uses a native Win32 tray implementation and does not require the frontend window to stay alive.

---

### 4. ProbeHost

Project: `ContextMenuMgr.ProbeHost`  
Executable: `ContextMenuMgr.ProbeHost.exe`

ProbeHost is the isolated helper process for Deep Analysis.

Responsibilities include:

- receiving analysis requests from the frontend
- initializing a Shell Extension against a sample target
- attempting to call `IContextMenu.QueryContextMenu`
- enumerating generated menu text
- returning the result to the frontend
- reporting structured errors on failure

ProbeHost should not:

- write to the registry
- execute menu commands
- remain resident in the background
- participate in normal scanning
- be loaded as a library by the backend service

---

### 5. Shared Contracts

Project: `ContextMenuMgr.Contracts`

Responsibilities include:

- IPC request / response models
- shared contracts between Frontend, Backend, TrayHost, and ProbeHost
- notification types
- control commands
- enums and shared constants

---

## IPC And Process Coordination

### Backend Pipe

The backend pipe uses JSON-over-Named-Pipe request / response messages for:

- menu snapshot retrieval
- menu item state changes
- approval decisions
- registry protection settings
- special menu management
- asking backend to launch TrayHost
- clean backend shutdown

---

### Frontend Control Pipe

The frontend control pipe is used for:

- single-instance activation
- showing the main window
- navigating to a specific page
- opening the approvals page
- focusing a specific approval item
- clean frontend shutdown

---

### TrayHost Control Pipe

The TrayHost control pipe is used for:

- exiting TrayHost
- refreshing tray localization text
- responding to frontend or backend control requests

---

### ProbeHost Communication

ProbeHost is started by the frontend on demand.

It exchanges data through controlled request / result channels and is guarded by a timeout.  
If ProbeHost crashes, times out, or returns invalid output, the frontend converts that into a user-readable failure state.

---

## Main Registry Scopes

The project primarily targets:

- `HKEY_CLASSES_ROOT\*\shell`
- `HKEY_CLASSES_ROOT\*\shellex\ContextMenuHandlers`
- `HKEY_CLASSES_ROOT\AllFileSystemObjects\shell`
- `HKEY_CLASSES_ROOT\AllFileSystemObjects\shellex\ContextMenuHandlers`
- `HKEY_CLASSES_ROOT\Directory\shell`
- `HKEY_CLASSES_ROOT\Directory\shellex\ContextMenuHandlers`
- `HKEY_CLASSES_ROOT\Directory\Background\shell`
- `HKEY_CLASSES_ROOT\Directory\Background\shellex\ContextMenuHandlers`
- `HKEY_CLASSES_ROOT\Drive\shell`
- `HKEY_CLASSES_ROOT\Drive\shellex\ContextMenuHandlers`
- `CLSID`
- `PackagedCom`
- file-type, extension, perceived-type, and directory-type branches
- user-level `HKCU/HKEY_USERS\<SID>\Software\Classes` ranges
- user-level Windows 11 modern menu block lists

Windows context menu registration is complex. Different software, menu types, and Windows versions may behave differently.

---

## Repository Layout

```text
ContextMenuMgr/
├─ .github/                        # GitHub Actions
├─ artifacts/                      # Local intermediate build output
├─ build/                          # Publish output and installers
├─ ContextMenuMgr.Backend/         # Windows Service / backend core
├─ ContextMenuMgr.Contracts/       # Shared contracts
├─ ContextMenuMgr.Frontend/        # WPF frontend
├─ ContextMenuMgr.ProbeHost/       # Deep Analysis helper process
├─ ContextMenuMgr.TrayHost/        # Per-user tray host
├─ Installer/                      # Inno Setup scripts and related files
├─ Scripts/                        # Build script modules
├─ build.ps1                       # Main build script
├─ build.bat                       # Batch wrapper for build.ps1
├─ ContextMenuMgr.slnx             # Solution
├─ README.md                       # Chinese README
└─ README.en.md                    # English README
````

---

## Executables And Artifacts

Public-facing executables:

* Frontend: `ContextMenuManagerPlus.exe`
* Backend service: `ContextMenuManagerPlus.Service.exe`
* Tray host: `ContextMenuManagerPlus.TrayHost.exe`

Internal helper executable:

* ProbeHost: `ContextMenuMgr.ProbeHost.exe`

ProbeHost is usually placed under:

```text
ProbeHost/
├─ x86/
├─ x64/
└─ arm64/
```

Different release packages include different ProbeHost architectures as needed.

---

## Requirements

* Windows 10 / 11
* .NET SDK 10
* PowerShell 5.1 or later
* Inno Setup 6

  * the repo prefers the bundled compiler:

    * `Installer\Inno Setup 6\ISCC.exe`

---

## Local Development Build

```powershell
dotnet restore .\ContextMenuMgr.slnx --configfile .\NuGet.Config
dotnet build .\ContextMenuMgr.slnx --no-restore
```

If you only work on the frontend, you can also build the frontend project:

```powershell
dotnet build .\ContextMenuMgr.Frontend\ContextMenuMgr.Frontend.csproj
```

The frontend build attempts to copy the backend, TrayHost, ProbeHost, and other required runtime artifacts for local debugging.

---

## Publish And Installer Build

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Configuration Release
```

### What `build.ps1` does

The build script:

1. restores the solution
2. publishes:

   * Frontend
   * Backend
   * TrayHost
   * ProbeHost
3. builds artifacts for multiple architectures:

   * `win-x64`
   * `win-x86`
   * `win-arm64`
4. builds multiple distribution modes:

   * `self-contained`
   * `framework-dependent`
5. invokes Inno Setup to generate installers

ProbeHost is included by target package:

* `win-x86`

  * `x86`
* `win-x64`

  * `x64`
  * `x86`
* `win-arm64`

  * `arm64`
  * `x64`
  * `x86`

For release packages, ProbeHost is generally published as self-contained single-file helpers to avoid failures caused by missing .NET runtimes for a specific architecture.

Default output directories:

* publish output: `build\publish\`
* installers: `build\dist\`

`build\dist\` also contains:

* `artifacts.txt`

which lists the generated installers.

---

## GitHub Actions

The repository includes:

* `.github/workflows/manual-release.yml`

Workflow behavior:

* supports manual dispatch
* supports tag-based releases
* calls `build.ps1`
* uploads build artifacts
* creates a draft release
* resolves release version and title from project version metadata

---

## Runtime Data, Logs, And State Store

### Frontend

Settings:

```text
%LocalAppData%\ContextMenuMgr\frontend-settings.json
```

Logs:

```text
%LocalAppData%\ContextMenuMgr\Logs\frontend-debug.log
%LocalAppData%\ContextMenuMgr\Logs\frontend-crash.log
```

---

### TrayHost

Log:

```text
%LocalAppData%\ContextMenuMgr\Logs\trayhost.log
```

---

### Backend

Log:

```text
%ProgramData%\ContextMenuMgr\Logs\backend.log
```

State store:

```text
%ProgramData%\ContextMenuMgr\Data\context-menu-state.json
```

Notes:

* the public product name is `Context Menu Manager Plus`
* the local data folder keeps the historical `ContextMenuMgr` name for compatibility

---

## Runtime And Recovery Notes

### Normal Flow

1. the frontend starts
2. the frontend connects to Backend Service
3. the frontend asks backend to ensure TrayHost exists
4. backend launches TrayHost for the current user session
5. the frontend loads menu snapshots and displays them

---

### Approval Notification Flow

1. Backend detects a new menu item
2. Backend adds it to the approval queue
3. Backend broadcasts a notification
4. TrayHost receives the event and shows a system notification
5. the user clicks the notification
6. TrayHost launches Frontend
7. Frontend opens the approvals page

---

### Recovery

If Backend Service exits unexpectedly:

* TrayHost may lose part of its functionality
* Frontend should not be forcibly closed
* users can open Settings and run:

  * Install / repair service

If registry protection blocks an operation:

* disable the related protection in Settings first
* finish editing or installing software
* enable protection again if needed

If ProbeHost Deep Analysis fails:

* normal enable/disable behavior is usually not affected
* diagnostics and logs may help
* do not report it as a bug solely because menu text cannot be resolved

---

## Notes

* This project modifies the registry. Use it carefully.
* Delete, disable, ACL protection, and restore operations may be affected by system permissions, security software, or Windows protection mechanisms.
* Some protected registry roots cannot have their ACL changed in ordinary ways. This is a Windows limitation.
* Security software may block delete, restore, registry-write, or Shell Extension probing operations.
* Icon, display-name, command-text, and CLSID metadata resolution are best-effort and cannot cover every third-party item perfectly.
* Shell Extension Deep Analysis is best-effort. Failure is often expected.
* User-level items, machine-level items, PackagedCom entries, ShellEx entries, and CLSID-based entries do not all behave the same way.
* Enabling registry protection may prevent some installers or driver installers from writing context-menu-related entries.
* Logs are usually more useful than UI messages when diagnosing edge cases.

## Reporting Issues

When opening an issue, it is very helpful to include:

- Windows version
- affected menu item name or registry path
- frontend log
- backend log
- trayhost log if the issue involves tray behavior
- reproduction steps
- screenshot or screen recording

## Contribution Guide

This project includes built-in development documentation (located in the `docs/` directory), along with `AGENTS.md` and `CLAUDE.md`. You can use majority Agent tools to contribute to this project.

## License

This project is licensed under GPL v3.0. See [LICENSE](./LICENSE).

## References And Acknowledgements

This project draws heavily from the following repositories:

- https://github.com/BluePointLilac/ContextMenuManager
- https://github.com/branhill/windows-11-context-menu-manager


## Stargazers over time
[![Stargazers over time](https://starchart.cc/PLFJY/ContextMenuMgr.svg?variant=adaptive)](https://starchart.cc/PLFJY/ContextMenuMgr)

​                    
