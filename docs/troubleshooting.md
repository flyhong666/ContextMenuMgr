# 排错指南

本文面向开发者和高级用户。ContextMenuMgr 的很多问题不能只看“权限”，还要同时判断用户 SID、用户注册表 hive 和 SessionId。LocalSystem 有高权限，但不等于正确用户；有用户 SID，也不等于进程在正确桌面 Session。

如果需要从零排查一个不明确问题，先阅读 [AI 与维护者接手 Playbook](./ai-maintainer-playbook.md)，按模板判定链路和证据。

主要日志位于 `%ProgramData%\ContextMenuMgr\Logs`：

| 日志 | 用途 |
| --- | --- |
| `frontend-debug.log` | 前端启动、导航、pipe 调用、Deep Analysis、全局搜索。 |
| `frontend-crash.log` | 前端未处理异常。 |
| `backend.log` | 后端服务、注册表操作、Win11、SpecialMenu、monitor。 |
| `trayhost.log` | 托盘进程、通知、打开前端、后端断开。 |
| ProbeHost stderr / result diagnostics | Deep Analysis 结果窗口和 `frontend-debug.log` 中的 ProbeHost 输出摘要。 |

`RuntimePaths` 当前使用 `%ProgramData%\ContextMenuMgr` 作为根目录，`frontend-settings.json`、`context-menu-state.json` 和 `DeletedBackups` 也在该根目录下。旧版本可能使用 `%LOCALAPPDATA%\ContextMenuMgr` 或 `%ProgramData%\ContextMenuMgr\Data` 下的 legacy 路径，当前代码保留迁移/兼容路径常量；排查历史安装时可以同时检查这些旧位置。

## 1. 前端无法连接后端

| 项目 | 内容 |
| --- | --- |
| 现象 | 前端显示后端不可达、加载占位数据或等待 pipe 超时。 |
| 可能原因 | 服务未安装、服务未运行、pipe server 未启动、UAC 安装被取消、pipe ACL 或启动异常。 |
| 优先查看的代码 | `MainViewModel.EnsureBackendReadyAsync`、`BackendServiceManager.cs`、`NamedPipeBackendClient.cs`、`NamedPipeBackendServer.cs`。 |
| 优先查看的日志 | `frontend-debug.log`、`backend.log`、bootstrap result file。 |
| 常见修复方向 | 检查 Windows Service 状态；重新执行 install/repair；确认 `ContextMenuManagerPlus.Service.exe` 路径正确；不要把 runtime pipe 操作改走 UAC bootstrapper。 |

## 2. 后端服务安装 / 修复失败

| 项目 | 内容 |
| --- | --- |
| 现象 | UAC 后仍无法安装服务，或返回 bootstrap 失败码。 |
| 可能原因 | 用户取消 UAC、服务路径无效、权限不足、result-file 写入失败、服务启动模式参数错误。 |
| 优先查看的代码 | `BackendServiceManager.cs`、`BackendServiceBootstrapper.cs`、`AutoStartService.cs`。 |
| 优先查看的日志 | `frontend-debug.log`、bootstrap result file、bootstrap log 如运行时生成。 |
| 常见修复方向 | 确认 `Verb=runas` 正常弹出；检查传入命令和 `--user-sid`；安装/修复只走链路 B，不要复用普通后端 pipe。 |

## 3. 前端因为后端异常而退出或无法加载

| 项目 | 内容 |
| --- | --- |
| 现象 | 前端启动后退出、页面空白或 snapshot 加载失败。 |
| 可能原因 | 后端 pipe 返回错误、JSON 契约不兼容、snapshot 中个别注册表路径异常、前端未处理异常。 |
| 优先查看的代码 | `ContextMenuWorkspaceService.cs`、`NamedPipeBackendClient.cs`、`NamedPipeBackendServer.cs`、`ContextMenuRegistryCatalog.cs`。 |
| 优先查看的日志 | `frontend-crash.log`、`frontend-debug.log`、`backend.log`。 |
| 常见修复方向 | 先确认 pipe response 的 `Success`、`ErrorCode` 和 payload；后端枚举异常应局部降级，不应让整个前端崩溃。 |

## 4. TrayHost 不出现

| 项目 | 内容 |
| --- | --- |
| 现象 | 托盘图标缺失，新增菜单没有通知。 |
| 可能原因 | 服务没有在用户 Session 启动 TrayHost、StartWithWindows policy 关闭、SessionId 错误、TrayHost 启动后连接后端失败。 |
| 优先查看的代码 | `FrontendAutostartLauncher.cs`、`BackendWindowsService.cs`、`BackendRuntime.cs`、`TrayHostRunner.cs`。 |
| 优先查看的日志 | `backend.log`、`trayhost.log`。 |
| 常见修复方向 | 检查服务是否拿到活动 SessionId；确认 `WTSQueryUserToken` / `CreateProcessAsUser` 结果；不要尝试从服务 Session 直接显示托盘 UI。 |

## 5. 开机启动不生效

| 项目 | 内容 |
| --- | --- |
| 现象 | 登录后没有托盘，服务启动模式或前端设置看似正确但无效。 |
| 可能原因 | 服务启动模式和用户级 `StartWithWindows` policy 不一致；用户 SID 写错；旧 Run value 干扰。 |
| 优先查看的代码 | `AutoStartService.cs`、`BackendServiceBootstrapper.cs`、`FrontendAutostartLauncher.cs`、`SettingsPageViewModel.cs`。 |
| 优先查看的日志 | `frontend-debug.log`、`backend.log`、`trayhost.log`。 |
| 常见修复方向 | 分开检查服务启动模式和用户级策略；bootstrapper 的 `install-or-repair`、`set-startup-mode` 需要 `--user-sid`。 |

## 6. Win11 新菜单禁用后刷新状态丢失

| 项目 | 内容 |
| --- | --- |
| 现象 | 点击禁用后短暂生效，刷新 snapshot 后又显示启用。 |
| 可能原因 | userContext 丢失、写到了错误用户 hive、只读了 HKLM blocked list、CLSID 规范化不一致。 |
| 优先查看的代码 | `Windows11ContextMenuCatalog.cs`、`Windows11BlocksService.cs`、`NamedPipeBackendServer.cs`、`Windows11ContextMenuService.cs`。 |
| 优先查看的日志 | `backend.log` 中 `Win11ContextMenuSetEnabled`、`Win11UserBlockedRead`、`Win11ContextMenuEnumerateSummary`。 |
| 常见修复方向 | 确认写入路径是 `HKEY_USERS\<frontend sid>\Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked`；snapshot 也必须带同一 userContext。 |

## 7. ShellNew 锁定后无法解锁

| 项目 | 内容 |
| --- | --- |
| 现象 | ShellNew order key 锁住后 unlock 或 repair 失败。 |
| 可能原因 | ACL deny rule 阻止写入、所有者异常、继承关闭、写到了错误 SID。 |
| 优先查看的代码 | `SpecialMenuService.SetShellNewOrderLockAsync`、`RepairShellNewOrderAclAsync`。 |
| 优先查看的日志 | `backend.log`。 |
| 常见修复方向 | 使用 ShellNew ACL repair 路径；必要时检查 ownership fallback；不要把它当 Registry Write Protection 的问题处理。 |

## 8. ShellNew 排序失败

| 项目 | 内容 |
| --- | --- |
| 现象 | 页面移动顺序后 Explorer “新建”菜单顺序未变。 |
| 可能原因 | Explorer ShellNew order key 被锁、Classes 排序值不一致、Explorer 缓存未刷新、user SID 错误。 |
| 优先查看的代码 | `SpecialMenuService.MoveShellNewAsync`、`SetShellNewOrderLockAsync`、`ShellChangeNotifier`。 |
| 优先查看的日志 | `backend.log`。 |
| 常见修复方向 | 检查是否临时解锁成功；确认路径是 `HKEY_USERS\<sid>`；必要时刷新 Explorer 或重启 Explorer。 |

## 9. SendTo 或 WinX 修改无效

| 项目 | 内容 |
| --- | --- |
| 现象 | SendTo 项或 WinX 项修改后 Explorer 不显示变化。 |
| 可能原因 | profile path 错误、RoamingAppData / LocalAppData 错误、`.lnk` hash 无效、软删除目录残留。 |
| 优先查看的代码 | `SpecialMenuService.cs` 中 SendTo / WinX 相关方法、`BackendUserContext.cs`。 |
| 优先查看的日志 | `backend.log`。 |
| 常见修复方向 | 确认 `BackendUserContext` 的 profile path；WinX 修改后确认 `.lnk` hash；SendTo / WinX 是文件系统链路，不是普通注册表链路。 |

## 10. Registry Write Protection 导致编辑失败

| 项目 | 内容 |
| --- | --- |
| 现象 | 编辑、启用、删除菜单项时报注册表保护错误。 |
| 可能原因 | Registry Write Protection 开启，阻止对受保护菜单注册表路径写入。 |
| 优先查看的代码 | `ContextMenuRegistryCatalog.cs`、`RegistryProtectionDialog`、`SettingsPageViewModel.cs`。 |
| 优先查看的日志 | `backend.log`、`frontend-debug.log`。 |
| 常见修复方向 | 提示用户到设置页解锁；应用自身操作如需临时解除保护，应走现有 best-effort unlock/relock 路径。不要和 ShellNew ACL Lock 混用。 |

## 第三方软件 / 驱动安装异常的归因原则

不要默认把第三方软件、驱动或安装器异常归因到本项目。先确认 Registry Write Protection 或 ShellNew ACL Lock 是否开启，再找对应时间点的 `backend.log` / `frontend-debug.log`，并确认目标注册表路径是否属于本项目保护范围。没有 Access Denied、UnauthorizedAccessException、项目日志或注册表路径证据时，不要下结论。必要时可以关闭相关保护或停止服务做 A/B 验证，详细流程见 [AI 与维护者接手 Playbook](./ai-maintainer-playbook.md)。

## 11. 重启 Explorer 没有效果

| 项目 | 内容 |
| --- | --- |
| 现象 | 点击 Restart Explorer 后当前桌面未刷新。 |
| 可能原因 | SessionId 错误、杀到了其他用户或服务 Session 的 explorer、Explorer 自动重启失败。 |
| 优先查看的代码 | `BackendRuntime.cs`、`NamedPipeBackendServer.cs`、前端触发重启的 ViewModel。 |
| 优先查看的日志 | `frontend-debug.log`、`backend.log`。 |
| 常见修复方向 | 确认请求携带正确前端 SessionId；不要杀所有 `explorer.exe`；这不是注册表写入链路。 |

## 12. ProbeHost 缺依赖

| 项目 | 内容 |
| --- | --- |
| 现象 | Deep Analysis 返回 `ProbeHostDependencyMissing` 或 stderr 有 `ContextMenuMgr.Contracts` 加载失败。 |
| 可能原因 | framework-dependent ProbeHost 目录只复制了 exe，缺少 dll/deps/runtimeconfig/contracts。 |
| 优先查看的代码 | `ContextMenuDeepAnalysisService.cs`、`ContextMenuMgr.Frontend.csproj`。 |
| 优先查看的日志 | `frontend-debug.log`、Deep Analysis 诊断详情。 |
| 常见修复方向 | 重新构建前端；检查 `ProbeHost\<arch>` 目录完整性；不要手工只复制 exe。 |

## 13. ProbeHost 架构不匹配

| 项目 | 内容 |
| --- | --- |
| 现象 | 返回 `ProbeHostExecutableArchitectureMismatch` 或 `ArchitectureMismatch`。 |
| 可能原因 | 目录里的 exe 架构错、目标 handler DLL 架构和 ProbeHost 不一致、发布包缺少目标架构。 |
| 优先查看的代码 | `ContextMenuDeepAnalysisService.SelectProbeHost`、`PeMachineTypeDetector`、`Scripts/Verify-ProbeHostArchitecture.ps1`。 |
| 优先查看的日志 | `frontend-debug.log`、Deep Analysis 诊断详情。 |
| 常见修复方向 | 运行架构验证；检查 `ProbeHost\x86`、`x64`、`arm64`；Release 包按 `Get-ProbeHostArchitectureMap` 携带架构。 |

## 14. Deep Analysis 分析失败

| 项目 | 内容 |
| --- | --- |
| 现象 | 返回 `CoCreateHandlerFailed`、`ShellExtInitNotSupported`、`IContextMenuNotSupported`、`QueryContextMenuFailed`、`Timeout` 或 native crash。 |
| 可能原因 | 第三方 handler 不支持探测场景、依赖 Explorer 环境、崩溃、卡死或架构不匹配。 |
| 优先查看的代码 | `ContextMenuDeepAnalysisService.cs`、`ContextMenuMgr.ProbeHost/Program.cs`、`ContextMenuDeepAnalysisWindowViewModel.cs`。 |
| 优先查看的日志 | `frontend-debug.log`、ProbeHost stderr / result diagnostics。 |
| 常见修复方向 | 把失败视为 Deep Analysis 限制；不要影响普通菜单开关；必要时尝试 `WholeContextMenu` 但不要混淆结果语义。 |

## 15. 全局搜索搜得到但不跳转

| 项目 | 内容 |
| --- | --- |
| 现象 | AutoSuggestBox 有结果，回车或点击后页面未切换。 |
| 可能原因 | 导航目标 key 错误、搜索结果类型和页面不匹配、导航服务未初始化。当前候选池只覆盖传统右键菜单分类和 Win11 项，不覆盖 ShellNew / SendTo / WinX、FileTypes、OtherRules、Approvals、Settings。 |
| 优先查看的代码 | `ContextMenuGlobalSearchService.cs`、`ShellViewModel.cs`、`GlobalSearchNavigationFilterService.cs`。 |
| 优先查看的日志 | `frontend-debug.log`。 |
| 常见修复方向 | 检查搜索结果是否标记 Win11 或普通传统菜单；跳转逻辑应同时导航和传递筛选请求。 |

## 16. 全局搜索跳转后目标页面没有筛选

| 项目 | 内容 |
| --- | --- |
| 现象 | 搜索跳转到页面，但列表没有自动筛选到目标项。 |
| 可能原因 | `GlobalSearchNavigationFilterService` 请求未消费、页面 ViewModel 未订阅、目标页面先后加载顺序问题。 |
| 优先查看的代码 | `GlobalSearchNavigationFilterService.cs`、`CategoryPageViewModel.cs`、`Windows11ContextMenuPageViewModel.cs`、`SpecialMenuPageViewModel.cs`。 |
| 优先查看的日志 | `frontend-debug.log` 中 `GlobalSearchFilterApplied`。 |
| 常见修复方向 | 确认页面初始化时消费 pending request；跳转后筛选文本应设置为目标菜单项名称或过滤文本。 |

## 17. 主题启动时没有应用

| 项目 | 内容 |
| --- | --- |
| 现象 | 设置中保存的主题没有在启动时生效。 |
| 可能原因 | 设置文件未加载、`FrontendThemeService` 未在启动早期应用、WPF-UI `ApplicationThemeManager` 调用顺序问题。 |
| 优先查看的代码 | `FrontendSettingsService.cs`、`FrontendThemeService.cs`、`App.Services.xaml.cs`、`SettingsPageViewModel.cs`。 |
| 优先查看的日志 | `frontend-debug.log`。 |
| 常见修复方向 | 检查 `%ProgramData%\ContextMenuMgr\frontend-settings.json`；确认主题服务在窗口显示前应用设置。 |

## 18. 图标 / 显示名 / DLL 路径解析不准确

| 项目 | 内容 |
| --- | --- |
| 现象 | 菜单项名称、图标或 DLL 路径显示为推断值、空值或不准确。 |
| 可能原因 | `MUIVerb` 资源解析失败、COM CLSID 缺少 `InprocServer32`、路径包含环境变量或命令行参数、AppxManifest 数据不完整。 |
| 优先查看的代码 | `ContextMenuRegistryCatalog.cs`、`IconPreviewService.cs`、`Windows11ContextMenuCatalog.cs`、`ContextMenuDeepAnalysisService.cs`。 |
| 优先查看的日志 | `backend.log`、`frontend-debug.log`。 |
| 常见修复方向 | 把解析结果视为 best-effort；不要用显示名或图标路径作为唯一 identity；必要时用 Deep Analysis 辅助确认实际菜单文字。 |
