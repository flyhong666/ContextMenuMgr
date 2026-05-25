# ContextMenuMgr 开发者指南

相关文档：

- [AI 与维护者接手 Playbook](./ai-maintainer-playbook.md)
- [进程、权限与用户上下文链路](./process-and-privilege-flows.md)
- [注册表模型与传统右键菜单实现](./registry-model.md)
- [SpecialMenu 实现说明](./special-menus.md)
- [Windows 11 新右键菜单实现说明](./windows11-context-menu.md)
- [Deep Analysis 与 ProbeHost 实现说明](./deep-analysis-probehost.md)
- [构建与发布说明](./build-and-release.md)
- [排错指南](./troubleshooting.md)

## 1. 项目目标

ContextMenuMgr 是一个以“审核优先、用户控制”为核心的 Windows 右键菜单管理工具。它不是简单开关器：当前实现不仅枚举和启用/禁用传统右键菜单，还会监控新增项、自动隔离待审核项、维护删除备份、处理 Windows 11 packaged context menu、管理 ShellNew / SendTo / WinX 等特殊入口，并提供隔离进程中的 Deep Analysis。

开发时要把“菜单项展示状态”、“后端状态库”、“注册表真实状态”、“用户上下文”和“桌面 Session”分开考虑。很多功能看似都是开关，但底层路径完全不同。

## 2. 项目进程模型

| 项目 | 路径 | 输出 exe | 职责 | 不应该做的事情 |
| --- | --- | --- | --- | --- |
| Frontend | `ContextMenuMgr.Frontend` | `ContextMenuManagerPlus.exe` | WPF UI、导航、搜索、设置、通过 named pipe 调用后端、启动 UAC bootstrapper、启动 ProbeHost | 不直接做高权限注册表修改；不直接加载第三方 Shell Extension DLL。 |
| Backend Service | `ContextMenuMgr.Backend` | `ContextMenuManagerPlus.Service.exe` | LocalSystem 服务、pipe server、注册表目录、监控、审核、SpecialMenu、Win11、AutoStart、Explorer restart、拉起 TrayHost/Frontend | 不在服务 Session 直接显示 UI；不把 SYSTEM 的 `HKCU` 当成用户 hive。 |
| TrayHost | `ContextMenuMgr.TrayHost` | `ContextMenuManagerPlus.TrayHost.exe` | 每用户托盘进程、后台通知、从通知打开前端或审核页 | 不承担注册表写入和菜单管理核心逻辑。 |
| ProbeHost | `ContextMenuMgr.ProbeHost` | `ContextMenuMgr.ProbeHost.exe` | Deep Analysis 的隔离 COM 探测进程，多架构发布 | 不提权、不写注册表、不执行菜单命令。 |
| Contracts | `ContextMenuMgr.Contracts` | `ContextMenuMgr.Contracts.dll` | pipe 契约、通知、菜单模型、Deep Analysis 模型、路径和服务元数据 | 不放具体平台操作实现。 |

## 3. 代码地图

| 区域 | 主要文件 | 职责 | 常见坑 |
| --- | --- | --- | --- |
| 前端 DI / 启动 | `App.Services.xaml.cs` | 注册服务、ViewModel、页面和 WPF-UI 导航 | 新服务要按生命周期选择 Singleton，不要绕过 DI 到处 new。 |
| 主窗口 / 导航 | `MainWindow.xaml`、`MainWindow.xaml.cs`、`ShellViewModel.cs` | 导航栏、全局搜索、刷新、Explorer restart、审批 badge | 搜索跳转需要同时导航和设置页面筛选。 |
| WorkspaceService | `ContextMenuWorkspaceService.cs` | 后端连接、快照加载、通知、普通菜单动作、服务状态 | 不要在页面 ViewModel 中重复写后端连接流程。 |
| BackendClient | `NamedPipeBackendClient.cs` | `PipeRequest` 发送、响应解析、通知订阅 | 新 runtime 操作优先新增 `PipeCommand`，保持结构化响应。 |
| BackendServiceManager | `BackendServiceManager.cs` | UAC bootstrapper、服务安装/卸载/停止/启动模式 | 只用于服务生命周期，不用于普通菜单开关。 |
| BackendRuntime | `BackendRuntime.cs` | 组合后端服务、启动 pipe/monitor、隔离新增项、确保 TrayHost | 新增项隔离和通知去重要保持稳定逻辑 key。 |
| NamedPipeBackendServer | `NamedPipeBackendServer.cs` | pipe ACL、请求分发、用户上下文解析、通知广播 | userContext 解析路径不能随意复用到无关功能。 |
| ContextMenuRegistryCatalog | `ContextMenuRegistryCatalog.cs` | 传统菜单枚举、开关、审核、删除备份、Registry Write Protection | 传统菜单和 Win11 新菜单不是同一模型。 |
| ContextMenuRegistryMonitor | `ContextMenuRegistryMonitor.cs` | 轮询快照、发现新增/被外部重新启用的项 | 登录后首个交互式快照用于重建 baseline，避免误报。 |
| ContextMenuStateStore | `ContextMenuStateStore.cs` | `%ProgramData%\ContextMenuMgr\context-menu-state.json` 状态库 | 审核状态不是纯 UI 状态。 |
| SpecialMenuService | `SpecialMenuService.cs` | ShellNew、SendTo、WinX、DragDrop、CommandStore、GuidBlock、IE MenuExt | ShellNew/SendTo/WinX 必须带正确用户上下文。 |
| Windows11ContextMenuCatalog | `Windows11ContextMenuCatalog.cs` | PackagedCom 和 AppxManifest 枚举、按 blocked list 判断启用状态 | 没有用户 SID 会跳过 Win11 snapshot。 |
| Windows11BlocksService | `Windows11BlocksService.cs` | HKLM 和用户级 Win11 blocked list 读写 | 用户级 blocked list 不能写 SYSTEM 的 `HKCU`。 |
| AutoStartService | `AutoStartService.cs` | 用户级 `StartWithWindows` policy 和旧 Run value 清理 | 开机启动同时影响服务启动模式和用户策略。 |
| FrontendAutostartLauncher | `FrontendAutostartLauncher.cs` | 服务跨 Session 启动 TrayHost/Frontend | UI 进程必须用 WTS token 进用户 Session。 |
| ContextMenuGlobalSearchService | `ContextMenuGlobalSearchService.cs` | 本地候选池、搜索评分、经典/Win11 结果合并 | 不要每次输入打后端。 |
| ContextMenuDeepAnalysisService | `ContextMenuDeepAnalysisService.cs` | 选择 ProbeHost 架构、启动隔离进程、解析结果 | ProbeHost 失败不能影响普通菜单管理。 |
| ProbeHost | `ContextMenuMgr.ProbeHost/src`、`ContextMenuMgr.ProbeHost.vcxproj` | native C++ COM 探测、SpecificHandler/WholeContextMenu、菜单枚举 | 不要把它改成提权进程或注册表写入工具。 |
| 构建脚本 | `build.ps1`、`Scripts/Build.Common.psm1`、`ContextMenuMgr.Frontend.csproj`、`Installer/build_Installer.iss` | 多目标发布、ProbeHost 多架构、安装包 | 改动后必须验证 x86/x64/arm64 ProbeHost 布局。 |

## 4. 启动与运行时流程

前端启动的大致流程：

```text
ContextMenuManagerPlus.exe
  -> App.Services.xaml.cs 建立 DI
  -> MainWindow / ShellViewModel
  -> ContextMenuWorkspaceService.InitializeAsync
  -> EnsureBackendReadyAsync
      -> NamedPipeBackendClient.PingAsync
      -> 如服务缺失或不可用，提示或调用 BackendServiceManager
  -> 同步日志级别
  -> SubscribeNotifications
  -> EnsureTrayHost
  -> GetSnapshot
  -> 分类页面从 WorkspaceService.Items 本地筛选展示
```

服务启动的大致流程：

```text
SCM 启动 ContextMenuManagerPlus.Service.exe --service
  -> BackendWindowsService.OnStart
  -> BackendRuntime.StartAsync
      -> FileLogger
      -> ContextMenuStateStore / RegistryBackupService / Catalog
      -> SpecialMenuService / Windows11BlocksService / AutoStartService
      -> NamedPipeBackendServer.Start
      -> ContextMenuRegistryMonitor.Start
      -> best-effort TryEnsureTrayHost
```

审核通知的大致流程：

```text
ContextMenuRegistryMonitor 轮询快照
  -> 发现新增或外部重新启用
  -> BackendRuntime.HandleNewItemDetectedAsync
  -> ContextMenuRegistryCatalog.QuarantineNewItemAsync
  -> BackendNotification(ItemDetected)
  -> Frontend / TrayHost 展示审核入口
  -> 用户 Allow / Deny / Remove
  -> PipeCommand.ApplyDecision
```

## 5. 传统右键菜单实现

传统菜单由 `ContextMenuRegistryCatalog` 枚举 `MonitoredRoots`。核心类型是 `ContextMenuEntry`，它记录 `Category`、`EntryKind`、`RegistryPath`、`BackendRegistryPath`、`SourceRootPath`、`CommandText`、`HandlerClsid`、启用状态、删除状态、审核状态和一致性问题。

当前传统菜单主要覆盖两类注册表模型：

| 类型 | 示例路径 | 处理方式 |
| --- | --- | --- |
| `shell` verb | `*\shell`、`Directory\Background\shell` | 读取命令、图标和属性；通过移动/改写相关键和值实现启用/禁用和属性修改。 |
| `shellex\ContextMenuHandlers` | `*\shellex\ContextMenuHandlers` | 读取 handler CLSID；禁用通常涉及 disabled container 或 blocked shell extensions。 |

`ContextMenuStateStore` 保存后端状态，不只是缓存。它用于标记 pending approval、删除备份、删除时间、被抑制的检测等。`RegistryBackupService` 在删除前调用 `reg.exe export` 保存 `.reg`，恢复时调用 `reg.exe import`。

外部变化检测由 `ContextMenuRegistryMonitor` 轮询实现。它会比较上一轮已知项和当前 snapshot，并对真正新增项或外部重新启用的项触发审核。该逻辑是 best-effort：Windows Shell 和第三方安装器的注册表写入可能有延迟，服务启动早于交互式用户 Session 时也可能缺少部分用户级项，所以代码在观察到交互式 Session 后会重建一次 baseline。

传统菜单和 Win11 新菜单不是同一套模型。不要把 `PackagedCom` 项当作普通 `shell` / `shellex` 项处理。

## 6. 新菜单项检测与审核

新增项审核不是单纯 UI 状态。流程是：

```text
Monitor 发现新增项
  -> Catalog 判断 DetectedChangeKind
  -> QuarantineNewItemAsync 立即禁用/隔离
  -> StateStore 标记 IsPendingApproval
  -> BackendNotification.ItemDetected
  -> TrayHost/Frontend 通知用户
  -> ApplyDecision: Allow / Deny / Remove
```

| 决策 | 当前行为 |
| --- | --- |
| `Allow` | 允许并清除 pending 状态。 |
| `Deny` | 保持禁用并清除或更新 pending 状态。 |
| `Remove` | 对待审核项走删除路径，必要时清理备份状态。 |

`ContextMenuApprovalIdentity` 为传统菜单和 Win11 packaged 菜单生成逻辑 key，用于分组和通知去重。Win11 项可能在多个 category 下出现，所以逻辑 key 不直接等于 category-specific ID。

## 7. Registry Write Protection

Registry Write Protection 是针对传统右键菜单受监控根的 ACL 防护。设置入口是 `GetRegistryProtectionSettingAsync` / `SetRegistryProtectionSettingAsync`，配置保存在 `%ProgramData%\ContextMenuMgr\backend-protection-settings.json`。

它和普通禁用的区别：

| 功能 | 作用 |
| --- | --- |
| 普通禁用 | 修改某个菜单项的注册表状态，让它不显示或不可用。 |
| Registry Write Protection | 修改受监控根的 ACL，阻止第三方新增/修改菜单项。 |

它和 ShellNew ACL Lock 的区别：

| 功能 | 目标 |
| --- | --- |
| Registry Write Protection | 传统菜单 `MonitoredRoots` 对应的注册表根。 |
| ShellNew ACL Lock | 用户 Explorer ShellNew order key：`Software\Microsoft\Windows\CurrentVersion\Explorer\Discardable\PostSetup\ShellNew`。 |

开启 Registry Write Protection 后，第三方安装器或软件更新器写右键菜单时可能失败或行为异常。前端的 `RegistryProtectionDialog` 会在普通菜单编辑、禁用、删除等操作前提示用户解锁。后端也有 preflight 和异常 fallback：如果检测到保护已开启，会返回 `PipeErrorCodes.RegistryWriteProtectionEnabled`。

代码中存在 app-owned operation 的临时 unlock/relock 路径：`RunWithRegistryWriteProtectionTemporarilyDisabledAsync` 会在后端内部操作前临时解除保护，完成后尝试恢复。这是 best-effort，relock 失败会记录错误并可能把警告追加到响应消息中。

## 8. SpecialMenu 实现

`SpecialMenuService` 管理的入口不是普通菜单项。`SpecialMenuKind` 当前包括 `ShellNew`、`SendTo`、`WinX`、`DragDrop`、`CommandStore`、`GuidBlock`、`InternetExplorer`。其中 `ShellNew`、`SendTo`、`WinX` 明确需要 `BackendUserContext`。

### ShellNew

ShellNew 由文件扩展名下的 `ShellNew` 子键和 Explorer 的排序键共同决定。当前实现会读取用户 `Software\Classes`，并使用 `HKEY_USERS\<sid>\Software\Classes` 来避免写到 SYSTEM 的 `HKCU`。

关键点：

| 内容 | 说明 |
| --- | --- |
| ShellNew 子键 | 典型位置是用户或机器 Classes 下的 `.<ext>\ShellNew`，可能有 `NullFile`、`Data`、`Command`、`Config\BeforeSeparator` 等值。 |
| Explorer ShellNew order key | `Software\Microsoft\Windows\CurrentVersion\Explorer\Discardable\PostSetup\ShellNew`，用于排序和锁定。 |
| Classes 排序 | `MoveShellNewAsync` 会基于当前 real items 和 order key 重写顺序。 |
| ACL lock / unlock | `SetShellNewOrderLockAsync` 给 order key 加或移除显式 Deny 规则，防止 Explorer 或第三方改排序。 |
| ACL fallback | `ResetShellNewOrderAcl` 可能需要 `SeTakeOwnershipPrivilege`、`SeRestorePrivilege` 等 fallback；失败要看日志，不要简单视为业务状态错。 |

ShellNew 的 ACL Lock 只作用于 ShellNew order key，不是全局 Registry Write Protection。

### SendTo

SendTo 是用户 profile 下的文件系统目录：`%APPDATA%\Microsoft\Windows\SendTo`。当前实现通过 `BackendUserContext.GetSendToPath()` 定位用户目录，创建/更新 `.lnk`，用 `DesktopIniStore` 维护显示名。删除不是直接永久删除，而是软删除到 `.deleted` 目录；恢复时再移回。

### WinX

WinX 是用户 `LocalAppData` 下的目录：`%LOCALAPPDATA%\Microsoft\Windows\WinX`。当前实现支持 group 和 entry 的创建、更新、移动、软删除、恢复默认。`.lnk` 需要 `WinXHasher.HashLnk`，否则 Windows 可能不接受或不显示该快捷方式。排序通过重写文件名前缀和跨 group 移动完成。

## 9. Windows 11 新右键菜单

Windows 11 新右键菜单由 packaged COM 和 AppX manifest 驱动，不等于传统 `shell` / `shellex`。

`Windows11ContextMenuCatalog` 当前做法：

1. 仅在 `OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)` 时支持。
2. 从 `Registry.ClassesRoot\PackagedCom\Package` 找 package。
3. 从 package repository 找安装路径。
4. 解析 `AppxManifest.xml` 或 `AppxMetadata\AppxBundleManifest.xml`。
5. 提取 `ComServer`、CLSID、display name 和 `ContextTypes`。
6. 映射到 `ContextMenuCategory` 并生成 `ContextMenuEntry`。
7. 结合 HKLM blocked list 和用户 blocked list 判断 `IsEnabled`。

blocked list 有两层：

| 作用域 | 路径 |
| --- | --- |
| 机器级 | `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked` |
| 用户级 | `HKEY_USERS\<sid>\Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked` |

snapshot 和开关都需要 userContext。没有用户 SID 时，`Windows11ContextMenuCatalog.EnumerateEntriesAsync` 当前会跳过枚举；`Windows11BlocksService` 对用户级写入会返回缺少用户上下文，机器级 fallback 只适用于明确机器级操作。

## 10. AutoStart 与 TrayHost

开机启动同时涉及服务启动模式和用户级策略：

| 层 | 文件 | 当前行为 |
| --- | --- | --- |
| 用户级 policy | `AutoStartService`、`FrontendAutostartLauncher` | 写/读 `HKEY_USERS\<sid>\Software\ContextMenuMgr\Frontend\StartWithWindows`。 |
| 旧 Run key | `AutoStartService` | 读取时作为 fallback；写入时清理 `ContextMenuManagerPlus.TrayHost` 旧值。 |
| 服务启动模式 | `BackendServiceBootstrapper` | `install-or-repair` 和 `set-startup-mode` 根据 `--user-sid` 对应 policy 设置服务 `auto` 或 `demand`。 |
| 用户 Session 进程 | `FrontendAutostartLauncher` | 服务启动或 Session 事件时按 policy best-effort 拉起 TrayHost。 |

TrayHost 是每用户进程，因为托盘图标和通知必须出现在用户交互式桌面。服务只是负责在正确 Session 中启动它。开机启动必须带用户 SID，因为服务启动模式是机器级，用户是否希望随 Windows 启动是用户级。

## 11. Restart Explorer

Restart Explorer 通过 pipe 命令 `RestartExplorer` 进入后端。`NamedPipeBackendServer` 会调用 `ResolveFrontendUserSessionContextAsync`，必须拿到前端用户的 `SessionId`。`ExplorerRestartService.RestartExplorer` 只杀同一 Session 中的 `explorer.exe`。

不要杀所有 `explorer.exe`。多用户、远程桌面和服务 Session 场景下，杀错 Session 会影响其他用户，或者完全没有效果。也不要在服务 Session 里尝试启动/控制 Explorer UI。

## 12. 全局搜索与页面筛选

全局搜索由 `ContextMenuGlobalSearchService` 维护本地候选池。候选来源包括：

- `ContextMenuWorkspaceService.Items` 中的传统右键菜单项，范围是 `File`、`Folder`、`Directory`、`DirectoryBackground`、`DesktopBackground`、`Drive`、`Library`、`Computer`、`RecycleBin` 等 `ContextMenuCategory`。
- `Windows11ContextMenuService.CurrentItems` 中的 Win11 项。

当前全局搜索不覆盖 ShellNew / SendTo / WinX、FileTypes、OtherRules、Approvals、Settings 等页面。新增页面如果需要被全局搜索命中，必须显式扩展 `ContextMenuGlobalSearchService` 的候选池和跳转筛选逻辑。

候选会在 workspace item 集合变化、item 显示名/状态变化、Win11 items 变化、语言变化时重建。搜索字段包括 display name、key name、registry path、backend registry path、source root、command、CLSID、file path、状态标签、分类标签，以及 Win11 的 package / publisher / context types。

搜索结果会 best-effort 使用菜单项实际 icon；没有可用图标时使用分类或 Win11 fallback 图标。输入时 `ShellViewModel` 只调用本地 `Search`，不访问后端，所以可以快速响应。用户选择结果后，`GlobalSearchNavigationFilterService.RequestFilter` 保存目标页面、分类、是否 Win11、筛选文本和 itemId，再导航到目标页面。目标页面消费 pending filter，把页面筛选框设为选中菜单项名称。

不要把每次输入改成后端查询。后端快照应由刷新或通知驱动。

## 13. Deep Analysis 与 ProbeHost

Deep Analysis 用于观察 shell extension 在样本路径下实际向 `IContextMenu` 填充了哪些菜单项。它不改变菜单状态。

前端流程：

```text
用户点击深入分析
  -> ContextMenuDeepAnalysisService.SelectProbeHost
  -> 验证 ProbeHost 架构和依赖
  -> 写 request JSON 到临时目录
  -> 启动 ContextMenuMgr.ProbeHost.exe --request ... --result ...
  -> 捕获 stdout/stderr
  -> 优先解析 result file，其次解析 stdout
  -> UI 展示菜单项或失败 diagnostics
```

ProbeHost 支持 `x86`、`x64`、`arm64`。选择依据主要是 handler DLL 的 PE machine type；unknown 时当前实现倾向于当前前端进程架构。`ContextMenuMgr.Frontend.csproj` 和 `Scripts/Build.Common.psm1` 都有多架构 native ProbeHost 发布逻辑。

`SpecificHandler` 直接 `CoCreateInstance` 目标 handler 并尝试 `IShellExtInit` / `IContextMenu`。`WholeContextMenu` 让 Shell 对样本路径创建完整上下文菜单。失败是常见结果，尤其是第三方 handler 依赖真实文件类型、进程环境、扩展动词、Explorer 内部状态或特定架构时。

ProbeHost 不写注册表、不执行菜单命令、不提权。不要在前端或后端直接加载第三方 Shell Extension DLL。

## 14. 主题、设置与本地化

`RuntimePaths` 当前把运行时根目录定义为 `%ProgramData%\ContextMenuMgr`。`FrontendSettingsService` 读写 `%ProgramData%\ContextMenuMgr\frontend-settings.json`，并会从旧的 `%LOCALAPPDATA%\ContextMenuMgr\frontend-settings.json` 迁移。日志统一在 `%ProgramData%\ContextMenuMgr\Logs` 下，状态库是 `%ProgramData%\ContextMenuMgr\context-menu-state.json`，删除备份目录是 `%ProgramData%\ContextMenuMgr\DeletedBackups`。旧版本可能使用 legacy 路径，当前代码保留迁移/兼容路径常量。

`FrontendThemeService` 基于 `AppThemeOption` 调用 WPF-UI 的 `ApplicationThemeManager`。启动时 `Initialize` 会应用保存的主题；选择 System 时会启用 `SystemThemeWatcher`，Light/Dark 会关闭系统跟随。

`LocalizationService` 支持 System、`zh-CN`、`en-US`、`zh-TW`，会设置当前线程 culture 和 WPF `CurrentLanguage`。设置页切换语言后还会 best-effort 通知 TrayHost reload localization。

设置页和服务交互的重点：

| 设置 | 交互路径 |
| --- | --- |
| 日志级别 | 前端本地更新，并同步 Backend 和 TrayHost。 |
| 开机启动 | `FrontendStartupService` 通过 pipe 写用户 policy；若服务已安装，再通过 bootstrapper 设置服务启动模式。 |
| Registry Write Protection | 通过 workspace 调用后端 pipe；前端本地设置只做 UI 同步。 |
| 服务安装/卸载/停止 | `BackendServiceManager` 走 UAC bootstrapper。 |

## 15. 构建与发布

`build.ps1` 是总入口，会并行启动 `Scripts/Build-Target.ps1`，按平台和分发模式生成 installer 与 portable 包。版本来自前端 csproj 的 `InformationalVersion` / `FileVersion`；Debug 和 Beta 会追加 git short commit。

`Scripts/Build.Common.psm1` 负责实际发布：

| 内容 | 说明 |
| --- | --- |
| Frontend 发布 | 发布 `ContextMenuManagerPlus.exe`，并把 Backend、TrayHost、ProbeHost 复制进输出。 |
| Backend 发布 | 发布 `ContextMenuManagerPlus.Service.exe`。 |
| TrayHost 发布 | 发布 `ContextMenuManagerPlus.TrayHost.exe`。 |
| ProbeHost 发布 | 按平台发布多架构 native exe。`win-x64` 包含 x64 + x86；`win-arm64` 包含 arm64 + x64 + x86；framework-dependent portable 的 anycpu 路径由前端 csproj 构建 x86/x64/arm64。Release 脚本使用 MSBuild 构建 vcxproj，不再对 ProbeHost 使用 dotnet publish。 |
| 验证 | `Scripts/Verify-ProbeHostArchitecture.ps1` 检查目录标签和 PE machine type 是否匹配。 |
| Inno Setup | `Installer/build_Installer.iss` 打包完整发布目录，framework-dependent 模式启用 .NET dependency installer。 |

`ContextMenuMgr.Frontend.csproj` 也定义了构建时的 native ProbeHost 多架构目标：构建后只复制 `ContextMenuMgr.ProbeHost.exe` 到 `ProbeHost\x86`、`ProbeHost\x64`、`ProbeHost\arm64`，并把 `nlohmann-json-LICENSE.MIT` 复制到 `ThirdPartyNotices`。

构建脚本改动后必须验证多架构 ProbeHost。Deep Analysis 的很多失败最终来自发布布局缺文件或架构标签错误。

## 16. 开发规则

如果是新接手项目或让 AI Agent 修改代码，建议先阅读 `ai-maintainer-playbook.md`，按其中模板先判定链路和证据，再决定修改位置。

- 新 runtime 操作优先走 Backend Pipe，不要随便走 UAC bootstrapper。
- 用户级注册表必须使用 `HKEY_USERS\<sid>`。
- 需要 UI 的东西必须进用户 Session。
- SpecialMenu 必须带正确 `userContext`。
- Win11 blocked list 必须带 `userContext`。
- Registry Write Protection 和 ShellNew ACL Lock 不要混用。
- ProbeHost 失败不能影响普通菜单管理。
- 不要在前端或后端直接加载第三方 Shell Extension DLL。
- 全局搜索不要每次输入打后端。
- 构建脚本改动必须验证多架构 ProbeHost。
- `SessionId` 只解决桌面/进程位置问题，不解决用户注册表路径问题。
- `LocalSystem` 只代表高权限，不代表正确用户上下文。
- 新增 `PipeCommand` 时要同时考虑响应字段、日志、通知广播和前端异常处理。

## 文档维护原则

- 涉及权限链路、用户上下文、ProbeHost、构建脚本的改动，应同步更新 `docs`。
- 新增 `PipeCommand` 时，应检查 `process-and-privilege-flows.md` 的功能到链路表。
- 新增 `SpecialMenuKind` 时，应更新 `special-menus.md`。
- 修改 ProbeHost 架构选择或发布目录时，应更新 `deep-analysis-probehost.md` 和 `build-and-release.md`。
- 修改 `RuntimePaths` 时，应更新 `troubleshooting.md` 和本指南。
- 修改全局搜索支持范围时，应更新本指南和 `troubleshooting.md`。
- 文档描述必须以当前代码为准，不要复读旧 README。

## 17. 常见排错方向

| 问题 | 优先看什么文件 | 优先看什么日志 | 最可能混错哪条链路 |
| --- | --- | --- | --- |
| 前端连不上后端 | `NamedPipeBackendClient.cs`、`NamedPipeBackendServer.cs`、`BackendRuntime.cs` | `frontend-debug.log`、`backend.log`、`service-startup.log` | 把服务未启动当成 pipe 协议错误。 |
| 服务安装失败 | `BackendServiceManager.cs`、`BackendServiceBootstrapper.cs` | `bootstrap.log`、bootstrap result detail、`service-startup.log` | 链路 B result-file 或 UAC 被取消。 |
| 托盘不出现 | `FrontendAutostartLauncher.cs`、`BackendWindowsService.cs`、`TrayHostRunner.cs` | `backend.log`、`trayhost.log` | 链路 C 缺 SessionId 或用户 policy 关闭。 |
| 开机启动不生效 | `AutoStartService.cs`、`BackendServiceBootstrapper.cs`、`FrontendAutostartLauncher.cs` | `backend.log`、`bootstrap.log` | 只改服务启动模式，没写用户 `StartWithWindows`，或反过来。 |
| Win11 禁用后刷新丢失 | `Windows11ContextMenuCatalog.cs`、`Windows11BlocksService.cs` | `backend.log` 中 `Win11 command` 和 `OpenUserBlockedKey` | 丢了 `userContext`，写到错误用户 hive。 |
| ShellNew 锁住后解不开 | `SpecialMenuService.cs` | `backend.log` 中 `ResetShellNewOrderAcl`、`TryReadShellNewOrderLockState` | 把 ShellNew ACL Lock 当成 Registry Write Protection。 |
| SendTo / WinX 修改无效 | `SpecialMenuService.cs`、`DesktopIniStore.cs`、`WinXHasher.cs` | `backend.log` | 缺用户 profile 上下文，或 WinX `.lnk` 没重新 hash。 |
| 重启 Explorer 没反应 | `NamedPipeBackendServer.cs`、`ExplorerRestartService.cs` | `backend.log` 中 `RestartExplorerRequest` | 没拿到前端用户 SessionId，或目标 Session 没有 explorer。 |
| ProbeHost 缺失 | `ContextMenuDeepAnalysisService.cs`、`ContextMenuMgr.Frontend.csproj` | `frontend-debug.log` | 发布布局缺 native exe，或本机缺少 C++ build tools 导致未构建。 |
| ProbeHost 架构错 | `ContextMenuDeepAnalysisService.cs`、`Verify-ProbeHostArchitecture.ps1`、`Build.Common.psm1` | `frontend-debug.log` | `ProbeHost\x86/x64/arm64` 目录和实际 exe machine type 不匹配。 |
| 深入分析失败 | `ContextMenuDeepAnalysisService.cs`、`ContextMenuMgr.ProbeHost/src` | `frontend-debug.log` 的 ProbeHost diagnostics | 把 COM 探测失败当成菜单管理失败。 |
| 全局搜索搜得到但不跳转 | `ShellViewModel.cs`、`GlobalSearchNavigationFilterService.cs`、目标页面 ViewModel | `frontend-debug.log` 中 `GlobalSearchOpenResult`、`GlobalSearchFilterRequested` | 只导航，没消费 pending filter 或 target page type 不匹配。 |
| 主题启动时不生效 | `FrontendThemeService.cs`、`SettingsPageViewModel.cs` | `frontend-debug.log` 中 `ThemeStartupInitialize` | 设置服务没加载、System watcher 状态和显式主题混用。 |
