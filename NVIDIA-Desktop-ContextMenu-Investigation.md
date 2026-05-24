# NVIDIA Desktop Context Menu Investigation Report

## 1. Summary

Short answer:

- Found: yes.
- Actual registry location: `HKEY_LOCAL_MACHINE\SOFTWARE\Classes\Directory\Background\shellex\ContextMenuHandlers`.
- Actual category root: `Directory\Background`, not `DesktopBackground`.
- Actual hive: HKLM, mirrored through HKCR. No NVIDIA desktop/background candidate was found under the current user's HKCU/HKU `Software\Classes` roots.
- Main suspected root cause: A. Category mapping problem. NVIDIA registers desktop-background handlers under `Directory\Background`, while ContextMenuMgr's `DesktopBackground` category only includes `DesktopBackground` roots. The entries should currently appear under the app's `DirectoryBackground` / background category, not under the `DesktopBackground` page.

Secondary findings:

- The NVIDIA App handler display name resolves from CLSID default as `DesktopContext Class`, not `NVIDIA App`; this can make it look missing unless the user searches by key/path/CLSID. This is E. Display-name/search problem.
- The service/HKCU user-context concern is real in code for user-level entries, but it is not the cause for the NVIDIA entries found on this machine because both NVIDIA handlers are under HKLM.
- The NVIDIA handlers are classic `shellex\ContextMenuHandlers` COM handlers. They are not shell verbs, `ExplorerCommandHandler`, or `DelegateExecute` items.
- No NVIDIA handler was found in `-ContextMenuHandlers`, and neither NVIDIA CLSID is currently present in `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked`.

## 2. Environment

OS and Windows version:

```text
[System.Environment]::OSVersion
Platform      : Win32NT
Version       : 10.0.26200.0
VersionString : Microsoft Windows NT 10.0.26200.0

Get-ComputerInfo
WindowsProductName : Windows 10 Pro
WindowsVersion     : 2009
OsBuildNumber      : 26200
```

User identity:

```text
whoami
zero_plfjy\plfjy

whoami /user
zero_plfjy\plfjy S-1-5-21-1260196130-996477025-225881392-1001

PowerShell SID
S-1-5-21-1260196130-996477025-225881392-1001
```

Process bitness:

```text
Is64BitOperatingSystem: True
Is64BitProcess: True
```

NVIDIA software presence from read-only uninstall registry inspection:

| Product | Version |
|---|---:|
| NVIDIA App | 11.0.7.247 |
| NVIDIA Backend | 11.0.7.247 |
| NVIDIA Graphics Driver | 596.49 |
| NVIDIA Container | 1.48 |
| NVIDIA LocalSystem/User/Session/AIUser Containers | 1.48 |
| NVIDIA MessageBus 3 for NvApp | 3.21 |
| NVIDIA ShadowPlay | 11.0.7.0 |
| NVIDIA PhysX System Software | 9.23.1019 |
| NVIDIA HD Audio Driver | 1.4.5.7 |
| NVIDIA Virtual Audio | 4.65.0.12 |

## 3. Registry Findings

Candidate matrix:

| Candidate | Actual Registry Path | Hive | Relative Root | Active/Disabled | Kind | Display Name Source | CLSID | Command/File | Visible in App? | Expected Category | Current App Category | Toggle Target | Likely Problem |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| NVIDIA App / NvAppDesktopContext | `HKLM\SOFTWARE\Classes\Directory\Background\shellex\ContextMenuHandlers\NvAppDesktopContext` | HKLM, also visible via HKCR | `Directory\Background\shellex\ContextMenuHandlers` | Active | Shell extension handler | CLSID default: `DesktopContext Class`; file metadata says NVIDIA App | `{F2E8B4A1-9C7D-4F6E-B3A5-8D2C1F4E9B7A}` | `C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvui.dll` | Expected under `DirectoryBackground`; not under `DesktopBackground` | Desktop Background UI | Directory Background UI | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked\{F2E8...}` | A, E |
| NVIDIA Control Panel / NvCplDesktopContext | `HKLM\SOFTWARE\Classes\Directory\Background\shellex\ContextMenuHandlers\NvCplDesktopContext` | HKLM, also visible via HKCR | `Directory\Background\shellex\ContextMenuHandlers` | Active | Shell extension handler | CLSID default: `NVIDIA CPL Context Menu Extension` | `{3D1975AF-48C6-4F8E-A182-BE0E08FA86A9}` | `C:\WINDOWS\System32\DriverStore\FileRepository\nvaki.inf_amd64_9f24f3c222af40d7\nvshext.dll` | Expected under `DirectoryBackground`; not under `DesktopBackground` | Desktop Background UI | Directory Background UI | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked\{3D1975...}` | A |

Detailed registry facts:

- No NVIDIA/Nv candidates were found under:
  - `HKCU:\Software\Classes\DesktopBackground\...`
  - `HKCU:\Software\Classes\Directory\Background\...`
  - `HKEY_USERS\<current-user-sid>\Software\Classes\DesktopBackground\...`
  - `HKEY_USERS\<current-user-sid>\Software\Classes\Directory\Background\...`
  - `HKLM:\SOFTWARE\Classes\DesktopBackground\...`
  - WOW6432Node desktop/background roots.
- NVIDIA candidates were found under:
  - `HKLM:\SOFTWARE\Classes\Directory\Background\shellex\ContextMenuHandlers\NvAppDesktopContext`
  - `HKLM:\SOFTWARE\Classes\Directory\Background\shellex\ContextMenuHandlers\NvCplDesktopContext`
  - the equivalent merged HKCR view.
- No NVIDIA candidate was found under any inspected `shellex\-ContextMenuHandlers` disabled backup root.
- Read-only check of `HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked` found neither NVIDIA CLSID currently blocked.

Important raw candidate output:

```csv
"KeyName","SourceHive","LogicalRelativePath","ActiveOrDisabledBackup","DefaultValue","ShellExtensionHandlerClsid","FullRegistryPath"
"NvAppDesktopContext","HKLM","Directory\Background\shellex\ContextMenuHandlers","Active","{F2E8B4A1-9C7D-4F6E-B3A5-8D2C1F4E9B7A}","{F2E8B4A1-9C7D-4F6E-B3A5-8D2C1F4E9B7A}","...\HKEY_LOCAL_MACHINE\SOFTWARE\Classes\Directory\Background\shellex\ContextMenuHandlers\NvAppDesktopContext"
"NvCplDesktopContext","HKLM","Directory\Background\shellex\ContextMenuHandlers","Active","{3D1975AF-48C6-4f8e-A182-BE0E08FA86A9}","{3D1975AF-48C6-4f8e-A182-BE0E08FA86A9}","...\HKEY_LOCAL_MACHINE\SOFTWARE\Classes\Directory\Background\shellex\ContextMenuHandlers\NvCplDesktopContext"
"NvAppDesktopContext","HKCR","Directory\Background\shellex\ContextMenuHandlers","Active","{F2E8B4A1-9C7D-4F6E-B3A5-8D2C1F4E9B7A}","{F2E8B4A1-9C7D-4F6E-B3A5-8D2C1F4E9B7A}","...\HKEY_CLASSES_ROOT\Directory\Background\shellex\ContextMenuHandlers\NvAppDesktopContext"
"NvCplDesktopContext","HKCR","Directory\Background\shellex\ContextMenuHandlers","Active","{3D1975AF-48C6-4f8e-A182-BE0E08FA86A9}","{3D1975AF-48C6-4f8e-A182-BE0E08FA86A9}","...\HKEY_CLASSES_ROOT\Directory\Background\shellex\ContextMenuHandlers\NvCplDesktopContext"
```

## 4. CLSID / File Metadata Findings

| CLSID | Registry Paths Found | CLSID Default | Server | File Exists | File Description | Product | Company | Version |
|---|---|---|---|---|---|---|---|---|
| `{F2E8B4A1-9C7D-4F6E-B3A5-8D2C1F4E9B7A}` | HKCR, HKLM Classes | `DesktopContext Class` | `C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvui.dll` | True | `NVIDIA User Experience Driver Component` | `NVIDIA App` | NVIDIA Corporation | Product `11.0.7.247`, file `8.17.15.5527` |
| `{3D1975AF-48C6-4F8E-A182-BE0E08FA86A9}` | HKCR, HKLM Classes | `NVIDIA CPL Context Menu Extension` | `C:\WINDOWS\System32\DriverStore\FileRepository\nvaki.inf_amd64_9f24f3c222af40d7\nvshext.dll` | True | `NVIDIA Display Shell Extension` | `NVIDIA Shell Extensions` | NVIDIA Corporation | `596.49` |

No user-level CLSID registrations were found for these CLSIDs in the inspected `HKEY_USERS\<current-user-sid>\Software\Classes\CLSID` locations.

Display-name implication:

- `NvCplDesktopContext` resolves well because the CLSID default contains `NVIDIA`.
- `NvAppDesktopContext` likely resolves as `DesktopContext Class`, because `GuidMetadataCatalog.ResolveClsidDisplayName` accepts any non-empty, non-GUID CLSID default before falling back to file metadata. The better human label, `NVIDIA App`, is available from file metadata but may not be reached.

## 5. Permission / ACL Findings

Read access:

- Current non-elevated user could read the NVIDIA HKLM/HKCR handler keys.
- ACLs on both NVIDIA handler keys include read access for `BUILTIN\Users`.
- ACLs include full control for `BUILTIN\Administrators` and `NT AUTHORITY\SYSTEM`.

Write implication:

- The current PowerShell process was not elevated, so admin write was not directly tested.
- The backend service is installed using `sc create` without an explicit `obj=` account, so Windows defaults it to LocalSystem. The source also treats LocalSystem as the privileged service identity.
- Based on ACLs, SYSTEM should be able to read and write the NVIDIA HKLM handler keys and should also be able to write the shell-extension blocked list.

Blocked-list read-only check:

```text
HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked
{F2E8B4A1-9C7D-4F6E-B3A5-8D2C1F4E9B7A}: Present=False
{3D1975AF-48C6-4f8e-A182-BE0E08FA86A9}: Present=False

ACL owner: BUILTIN\Administrators
SYSTEM: FullControl
Administrators: FullControl
Users: ReadKey
```

No ACL evidence points to G. Permission/ACL problem for this NVIDIA case.

## 6. Current Code Path

Snapshot route:

- Frontend category pages use `ContextMenuWorkspaceService.RefreshAsync`, which calls `_backendClient.GetSnapshotAsync`.
- Backend `NamedPipeBackendServer` handles `PipeCommand.GetSnapshot` by calling `_catalog.GetSnapshotAsync`.
- `GetSnapshotAsync` enumerates `MonitoredRoots`.

Category root mapping:

- `ContextMenuRegistryCatalog.MonitoredRoots` maps `DirectoryBackground` to:
  - `Directory\Background\shell`
  - `Directory\Background\shellex\ContextMenuHandlers`
  - `Directory\Background\shellex\-ContextMenuHandlers`
- The same array maps `DesktopBackground` only to:
  - `DesktopBackground\shell`
  - `DesktopBackground\shellex\ContextMenuHandlers`
  - `DesktopBackground\shellex\-ContextMenuHandlers`
- The desktop page is `DesktopContextMenuPageViewModel : CategoryPageViewModel(ContextMenuCategory.DesktopBackground, ...)`.
- `CategoryPageViewModel.FilterItem` requires `item.Category == Category`, so `DirectoryBackground` entries cannot appear on the `DesktopBackground` page.

Relevant code evidence:

- `ContextMenuRegistryCatalog.cs:38-43`: separate `DirectoryBackground` and `DesktopBackground` monitored roots.
- `CategoryNavigationPageViewModels.cs:54`: background page uses `ContextMenuCategory.DirectoryBackground`.
- `CategoryNavigationPageViewModels.cs:64`: desktop page uses `ContextMenuCategory.DesktopBackground`.
- `CategoryPageViewModel.cs:151`: filter excludes entries whose category differs from the page category.

Scene snapshot route:

- `NamedPipeBackendServer.cs:319-324` handles `GetSceneSnapshot` by calling `_catalog.GetSceneSnapshotAsync(request.SceneKind.Value, request.ScopeValue, cancellationToken)`.
- It does not call `ResolveFrontendUserContextAsync` for `GetSceneSnapshot`.
- `ContextMenuRegistryCatalog.GetSceneSnapshotAsync` calls `EnumerateEntries(roots)` without a user SID.
- `ContextMenuSceneKind` currently has file/custom/directory-type scene values only; it does not define `DesktopBackground`.

User context behavior:

- `SetEnabled` resolves frontend user context in `NamedPipeBackendServer.HandleSetEnabledAsync` before calling `ApplyDesiredStateAsync`.
- For classic shell items, `ApplyDesiredStateAsync` then calls `GetSnapshotAsync`, which also does not accept or pass a frontend user context.
- `EnumerateRootInstances(currentUserSid: null)` enumerates HKLM `SOFTWARE\Classes`, then all loaded `HKEY_USERS\<S-1-5-21-*>\Software\Classes` hives. It does not enumerate HKCR as a root instance and does not enumerate WOW6432Node menu roots.
- When a current user SID is supplied, `EnumerateRootInstances` can prioritize the frontend user's HKU classes, but this path is not used by `GetSnapshotAsync` or `GetSceneSnapshotAsync`.

Toggle route:

- Shell verbs: `SetShellVerbEnabled` writes visibility marker values on `item.BackendRegistryPath`.
- Shell extensions: `SetShellExtensionEnabled` does not move keys between `ContextMenuHandlers` and `-ContextMenuHandlers`. It writes or deletes a value named by CLSID under `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked`.
- `IsShellExtensionBlocked` reads the same blocked list to decide enabled state for shell extensions.

Identity preservation:

- `ContextMenuEntry.Id` is only `{stable source root}|{subkey name}`. It does not include the hive.
- `BackendRegistryPath` preserves the actual enumerated hive path. For the NVIDIA entries, this is the real HKLM path.
- For duplicate IDs across HKLM/HKU, `BuildSnapshotAsync` keeps one logical entry and `SelectPreferredActualEntry` lets later entries replace earlier entries unless choosing active over disabled. This can lose secondary hive instances.

Display-name route:

- Shell extension display names are resolved by `ShellMetadataResolver.ResolveShellExtensionDisplayNameDetails`.
- It checks CLSID display metadata first, then handler default value, then file metadata.
- Because `DesktopContext Class` is non-empty and non-GUID, the NVIDIA App entry may not fall through to the clearer `NVIDIA App` file metadata.

## 7. Root Cause Analysis

A. Category mapping problem: selected.

- Registry evidence: NVIDIA App and NVIDIA CPL handlers are under `Directory\Background\shellex\ContextMenuHandlers`, not under `DesktopBackground`.
- Code evidence: `DesktopBackground` and `DirectoryBackground` are separate categories, and the desktop page filters strictly by category.
- Why this causes the observed UI behavior: the real Windows desktop background context menu can use `Directory\Background`, but ContextMenuMgr projects that root to its `DirectoryBackground` page. Therefore the NVIDIA desktop item will not be shown under the app's `DesktopBackground` category.

B. User-context enumeration problem: not selected for the observed NVIDIA keys, but a valid adjacent risk.

- Registry evidence: NVIDIA keys are HKLM, not HKCU/HKU current SID.
- Code evidence: `GetSceneSnapshot` and `GetSnapshot` do not resolve/pass frontend user context; enumeration falls back to HKLM plus loaded HKU hives.
- Why it is not the NVIDIA root cause here: no NVIDIA desktop/background candidate was found only under HKCU/HKU. If a future NVIDIA version registers per-user only, this code path could miss or mis-prioritize it.

C. HKCR merged-view write problem: not selected for the observed NVIDIA keys.

- Registry evidence: the same handlers are present under real HKLM and merged HKCR.
- Code evidence: catalog enumeration uses HKLM/HKU root instances, not HKCR, so `BackendRegistryPath` for NVIDIA is the writable real HKLM path.
- Caveat: `ContextMenuEntry.Id` does not include hive, so duplicate logical entries can still collapse.

D. Disabled backup root problem: not selected for NVIDIA, but a code risk.

- Registry evidence: no NVIDIA candidate was found in `shellex\-ContextMenuHandlers`.
- Code evidence: disabled roots are monitored, but shell-extension enabled state is computed only from the global blocked list. If an item exists only in `-ContextMenuHandlers`, it may not be shown/toggled as expected.

E. Display-name/search problem: selected as secondary.

- Registry evidence: NVIDIA App CLSID default is `DesktopContext Class`; file metadata contains `NVIDIA App`.
- Code evidence: CLSID default is accepted before file metadata.
- Why this matters: the app may display the NVIDIA App handler as `DesktopContext Class`, making it look missing or unrelated in the UI.

F. Unsupported shell verb pattern: not selected.

- Registry evidence: the NVIDIA items are classic shell extension handler keys. No `ExplorerCommandHandler`, `DelegateExecute`, `DropTarget`, or command subkey was present on the NVIDIA candidate keys.

G. Permission/ACL problem: not selected.

- Registry evidence: current user read succeeds; ACLs grant SYSTEM full control.
- Toggle writes were not tested by design, but ACLs do not suggest a read/write blocker for the service.

H. Not a classic menu item: not selected.

- Registry evidence: both candidates are classic COM shell extensions under `Directory\Background\shellex\ContextMenuHandlers`.

## 8. Proposed Fix Options

1. Add dual projection for desktop background compatibility.

- Files to change: `ContextMenuRegistryCatalog.cs`, possibly frontend category text/help only if the UI wording needs clarification.
- Approach: show selected `Directory\Background` entries on the `DesktopBackground` page as compatibility projections while preserving original `SourceRootPath`, `RegistryPath`, `BackendRegistryPath`, handler CLSID, and toggle target. This avoids lying to the toggle code about the real registry location.
- Pros: directly addresses the NVIDIA evidence; avoids moving registry keys; keeps toggling tied to the original source.
- Risks: duplicate display if the same handler also exists under `DesktopBackground`; requires clear deduping and identity strategy.
- ShellNew/Win11/service startup impact: none expected if scoped to classic category projection.
- Test plan: install/check NVIDIA App, verify item appears under Desktop Background, verify no duplicate when both roots exist, verify toggle request targets the original HKLM `Directory\Background` handler/blocked CLSID, verify Directory Background page behavior remains sane.

2. Make DesktopBackground category include `Directory\Background` roots directly.

- Files to change: `ContextMenuRegistryCatalog.cs`.
- Approach: add `Directory\Background` descriptors to the `DesktopBackground` category.
- Pros: simple and likely fixes display quickly.
- Risks: can blur the distinction between folder-background and desktop-background categories; `ContextMenuEntry.Id` collisions may occur unless identities/projection metadata are handled carefully.
- ShellNew/Win11/service startup impact: none expected.
- Test plan: same as option 1, with extra attention to duplicate/cross-category behavior.

3. Improve display name resolution for NVIDIA-like CLSID defaults.

- Files to change: `GuidMetadataCatalog.cs`, possibly `ShellMetadataResolver.cs`.
- Approach: treat generic COM class names like `DesktopContext Class` as weak names and prefer file metadata/product metadata when it is more informative.
- Pros: makes `NvAppDesktopContext` understandable as NVIDIA App.
- Risks: heuristics can rename unrelated COM entries if too broad.
- ShellNew/Win11/service startup impact: none expected.
- Test plan: verify NvApp displays as NVIDIA App or NVIDIA User Experience Driver Component; verify existing known handlers do not regress.

4. Pass frontend user context into classic snapshot enumeration.

- Files to change: `NamedPipeBackendServer.cs`, `ContextMenuRegistryCatalog.cs`.
- Approach: resolve frontend user context for `GetSnapshot`/`GetSceneSnapshot` and pass `userContext.Sid` into `EnumerateEntries`.
- Pros: fixes the real code risk for per-user HKCU registrations.
- Risks: changes baseline/dedup ordering for all classic menus; needs careful regression testing.
- ShellNew/Win11/service startup impact: should not touch ShellNew/Win11 logic directly, but snapshot timing at service startup may be affected.
- Test plan: create read-only test fixtures or controlled registry test keys under HKLM and HKU; verify current-user HKU wins; verify startup snapshots do not mark expected entries missing.

5. Improve `ContextMenuEntry` identity/source preservation.

- Files to change: contracts plus backend/frontend consumers: `ContextMenuEntry.cs`, `ContextMenuRegistryCatalog.cs`, state persistence migration if needed.
- Approach: preserve logical ID separately from source instance ID/hive, so HKLM/HKU/HKCR-like duplicates can be displayed or toggled deterministically.
- Pros: addresses a broad class of source-hive ambiguity.
- Risks: contract/state migration; larger blast radius.
- ShellNew/Win11/service startup impact: should be avoidable but requires broad regression.
- Test plan: duplicate handler keys across HKLM/HKU, disabled/active root pairing, delete/undo, approval queue, refresh.

6. Revisit shell-extension toggle strategy for disabled backup roots.

- Files to change: `ContextMenuRegistryCatalog.cs`.
- Approach: decide whether disabled `-ContextMenuHandlers` roots are first-class disable containers and pair move/restore semantics with blocked-list semantics.
- Pros: fixes the known mismatch if entries are found only under disabled backup roots.
- Risks: moving third-party keys is riskier than the current blocked-list strategy.
- ShellNew/Win11/service startup impact: none expected if scoped to classic shell extensions.
- Test plan: controlled handler under active root, disabled backup root, and blocked list; verify UI state and toggles.

Ranking based on current evidence:

1. Option 1: best fit. It handles the actual NVIDIA `Directory\Background` registration while preserving source paths.
2. Option 3: useful companion fix for the NVIDIA App label.
3. Option 4: important robustness fix, but not the cause on this machine.
4. Option 5: broader architectural cleanup if duplicate hive/source ambiguity is causing other reports.
5. Option 6: address separately if disabled backup evidence appears.
6. Option 2: simple but less precise than dual projection.

## 9. Recommended Next Step

Implement a narrow dual-projection fix: surface `Directory\Background` shell extension entries on the `DesktopBackground` page when they represent desktop-background-compatible handlers, but keep their original `BackendRegistryPath`, `SourceRootPath`, `HandlerClsid`, and toggle behavior intact. Pair that with a small display-name improvement so `NvAppDesktopContext` is recognizable as NVIDIA App instead of only `DesktopContext Class`.

Do not change ShellNew, Win11, service bootstrap/startup, or registry mutation behavior for the first fix pass.

## 10. Raw Diagnostic Output

Environment excerpts:

```text
Platform      : Win32NT
Version       : 10.0.26200.0
VersionString : Microsoft Windows NT 10.0.26200.0

WindowsProductName : Windows 10 Pro
WindowsVersion     : 2009
OsBuildNumber      : 26200

zero_plfjy\plfjy
SID: S-1-5-21-1260196130-996477025-225881392-1001

Is64BitOperatingSystem: True
Is64BitProcess: True
```

NVIDIA app/driver excerpts:

```text
NVIDIA App 11.0.7.247
NVIDIA Backend 11.0.7.247
NVIDIA Graphics Driver 596.49
NVIDIA Container 1.48
NVIDIA ShadowPlay 11.0.7.0
NVIDIA PhysX System Software 9.23.1019
```

Candidate registry key excerpts:

```text
FullRegistryPath:
  HKEY_LOCAL_MACHINE\SOFTWARE\Classes\Directory\Background\shellex\ContextMenuHandlers\NvAppDesktopContext
SourceHive: HKLM
LogicalRelativePath: Directory\Background\shellex\ContextMenuHandlers
ActiveOrDisabledBackup: Active
DefaultValue: {F2E8B4A1-9C7D-4F6E-B3A5-8D2C1F4E9B7A}
ShellExtensionHandlerClsid: {F2E8B4A1-9C7D-4F6E-B3A5-8D2C1F4E9B7A}
CurrentUserCanRead: True
SystemShouldBeAbleToReadBasedOnAcl: True

FullRegistryPath:
  HKEY_LOCAL_MACHINE\SOFTWARE\Classes\Directory\Background\shellex\ContextMenuHandlers\NvCplDesktopContext
SourceHive: HKLM
LogicalRelativePath: Directory\Background\shellex\ContextMenuHandlers
ActiveOrDisabledBackup: Active
DefaultValue: {3D1975AF-48C6-4f8e-A182-BE0E08FA86A9}
ShellExtensionHandlerClsid: {3D1975AF-48C6-4f8e-A182-BE0E08FA86A9}
CurrentUserCanRead: True
SystemShouldBeAbleToReadBasedOnAcl: True
```

CLSID metadata excerpts:

```text
CLSID: {F2E8B4A1-9C7D-4F6E-B3A5-8D2C1F4E9B7A}
DefaultValue: DesktopContext Class
InprocServer32Default: C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvui.dll
FileExists: True
FileDescription: NVIDIA User Experience Driver Component
ProductName: NVIDIA App
CompanyName: NVIDIA Corporation
ProductVersion: 11.0.7.247
FileVersion: 8.17.15.5527

CLSID: {3D1975AF-48C6-4F8E-A182-BE0E08FA86A9}
DefaultValue: NVIDIA CPL Context Menu Extension
InprocServer32Default: C:\WINDOWS\System32\DriverStore\FileRepository\nvaki.inf_amd64_9f24f3c222af40d7\nvshext.dll
FileExists: True
FileDescription: NVIDIA Display Shell Extension
ProductName: NVIDIA Shell Extensions
CompanyName: NVIDIA Corporation
ProductVersion: 596.49
FileVersion: 596.49
```

Blocked-list check:

```text
HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked

CLSID                                  Present
-----                                  -------
{F2E8B4A1-9C7D-4F6E-B3A5-8D2C1F4E9B7A} False
{3D1975AF-48C6-4f8e-A182-BE0E08FA86A9} False
```

Source-code excerpts by line reference:

```text
ContextMenuRegistryCatalog.cs:38-43
DirectoryBackground roots:
  Directory\Background\shell
  Directory\Background\shellex\ContextMenuHandlers
  Directory\Background\shellex\-ContextMenuHandlers
DesktopBackground roots:
  DesktopBackground\shell
  DesktopBackground\shellex\ContextMenuHandlers
  DesktopBackground\shellex\-ContextMenuHandlers

NamedPipeBackendServer.cs:319-324
GetSceneSnapshot calls _catalog.GetSceneSnapshotAsync(...) without frontend user context.

NamedPipeBackendServer.cs:472-482
SetEnabled resolves frontend user context and passes it to ApplyDesiredStateAsync.

ContextMenuRegistryCatalog.cs:154
GetSceneSnapshot enumerates entries with EnumerateEntries(roots), no user SID.

ContextMenuRegistryCatalog.cs:1717
GetSnapshot enumerates MonitoredRoots.

ContextMenuRegistryCatalog.cs:1799, 2555-2562
Shell-extension enabled state is !IsShellExtensionBlocked(handlerClsid), backed by HKLM Shell Extensions\Blocked.

ContextMenuRegistryCatalog.cs:2628-2646
SetShellExtensionEnabled writes/deletes CLSID values in HKLM Shell Extensions\Blocked.

ContextMenuRegistryCatalog.cs:3710-3747
EnumerateRootInstances uses HKLM\SOFTWARE\Classes and loaded HKU Software\Classes hives; no HKCR or WOW6432Node menu-root enumeration.
```
