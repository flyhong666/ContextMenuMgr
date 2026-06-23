# 注册表模型与传统右键菜单实现

## 1. 本文目的

本文解释传统右键菜单如何在 ContextMenuMgr 中被扫描、建模、禁用、删除、恢复、审核和备份。这里的“传统右键菜单”主要指注册在 `shell` 和 `shellex\ContextMenuHandlers` 体系下的菜单项，不包含 Windows 11 packaged context menu，也不包含 ShellNew / SendTo / WinX 等 SpecialMenu。

相关主实现位于 `ContextMenuMgr.Backend/Services/ContextMenuRegistryCatalog.cs`、`ContextMenuRegistryMonitor.cs`、`ContextMenuStateStore.cs` 和 `RegistryBackupService.cs`，前后端传输模型位于 `ContextMenuMgr.Contracts/ContextMenuEntry.cs`。

## 2. Windows 传统右键菜单基础

传统右键菜单不是单一注册表路径，而是一组按对象类型分散的注册表入口：

| 概念 | 说明 |
| --- | --- |
| `shell` | 常见的 verb 菜单项，通常包含子键和 `command` 子键。适合“打开、编辑、用某程序处理”这类命令。 |
| `shellex\ContextMenuHandlers` | COM Shell Extension 处理器入口，通常通过 CLSID 指向第三方 DLL。 |
| `CLSID` | COM 类标识，Shell Extension 通常通过 `CLSID\{...}\InprocServer32` 找到实际 DLL。 |
| `command` | `shell` verb 的实际命令行。 |
| `MUIVerb` | 菜单显示名，可能是普通字符串，也可能引用资源。 |
| `LegacyDisable` | 常见禁用标记之一。不同入口的禁用策略不完全相同。 |
| `Extended` | 只在按住 Shift 时显示的标记。 |
| `NoWorkingDirectory` | 影响 Explorer 调用命令时的工作目录行为。 |
| `Icon` | 菜单图标来源，可包含路径和图标索引。 |
| `AppliesTo` | Explorer 条件表达式，决定菜单项适用范围。 |
| 用户级 Classes | 当前用户的 `Software\Classes`，服务端应通过 `HKEY_USERS\<SID>\Software\Classes` 定位。 |
| 机器级 Classes | `HKEY_LOCAL_MACHINE\SOFTWARE\Classes`，影响所有用户。 |

不要把 `HKCR` 当成真实单一路径。`HKEY_CLASSES_ROOT` 是用户级 Classes 与机器级 Classes 的合并视图，读取时方便，写入时必须明确写到用户级还是机器级。后端服务不能用 LocalSystem 的 `HKCU` 代替前端用户的 `HKEY_USERS\<SID>`。

## 3. 项目中的 MonitoredRoots

`ContextMenuRegistryCatalog` 用 `MonitoredRoots` 定义传统菜单扫描范围。当前代码覆盖以下 `ContextMenuCategory`：

| 分类 | 主要注册表路径 |
| --- | --- |
| `File` | `*\shell`、`*\shellex\ContextMenuHandlers`、`*\shellex\-ContextMenuHandlers` |
| `AllFileSystemObjects` | `AllFilesystemObjects\shell`、`AllFilesystemObjects\shellex\ContextMenuHandlers`、`AllFilesystemObjects\shellex\-ContextMenuHandlers` |
| `Folder` | `Folder\shell`、`Folder\shellex\ContextMenuHandlers`、`Folder\shellex\-ContextMenuHandlers` |
| `Directory` | `Directory\shell`、`Directory\shellex\ContextMenuHandlers`、`Directory\shellex\-ContextMenuHandlers` |
| `DirectoryBackground` | `Directory\Background\shell`、`Directory\Background\shellex\ContextMenuHandlers`、`Directory\Background\shellex\-ContextMenuHandlers` |
| `DesktopBackground` | `DesktopBackground\shell`、`DesktopBackground\shellex\ContextMenuHandlers`、`DesktopBackground\shellex\-ContextMenuHandlers` |
| `Drive` | `Drive\shell`、`Drive\shellex\ContextMenuHandlers`、`Drive\shellex\-ContextMenuHandlers` |
| `Library` | `LibraryFolder\shell`、`LibraryFolder\shellex\ContextMenuHandlers`、`LibraryFolder\Background\shell`、`LibraryFolder\Background\shellex\ContextMenuHandlers`、`UserLibraryFolder\shell`、`UserLibraryFolder\shellex\ContextMenuHandlers` 及对应 disabled mirror |
| `Computer` | `CLSID\{20D04FE0-3AEA-1069-A2D8-08002B30309D}\shell`、`...\shellex\ContextMenuHandlers`、`...\shellex\-ContextMenuHandlers` |
| `RecycleBin` | `CLSID\{645FF040-5081-101B-9F08-00AA002F954E}\shell`、`...\shellex\ContextMenuHandlers`、`...\shellex\-ContextMenuHandlers`、`...\shellex\PropertySheetHandlers` |

`-ContextMenuHandlers` 是项目识别的 disabled mirror 形态之一，用于表示从启用位置移出的 Shell Extension handler。不要把所有禁用都简化成一个注册表值。

## 4. ContextMenuEntry 模型

`ContextMenuEntry` 是传统菜单、Win11 菜单和审核流程共享的传输模型。当前重要字段如下：

| 字段 | 作用 |
| --- | --- |
| `Id` | 项目内部稳定标识，不能只用显示名替代。 |
| `Category` | 菜单适用分类，对应 `ContextMenuCategory`。 |
| `EntryKind` | 菜单来源类型，例如 `ShellVerb` 或 `ShellExtension`。 |
| `KeyName` | 注册表子键名或逻辑 key。 |
| `DisplayName` | 前端显示名，可能来自 `MUIVerb`、默认值、CLSID 或推断值。 |
| `EditableText` | 可编辑显示文本，当前并非所有项都有。 |
| `RegistryPath` | 给用户或前端展示的注册表位置。 |
| `BackendRegistryPath` | 后端实际操作时使用的位置，可能不同于展示路径。 |
| `SourceRootPath` | 扫描根路径，用于判断来源和后续操作分流。 |
| `CommandText` | `shell` verb 的命令行，Shell Extension 通常为空。 |
| `CanEditCommandText` | 标识普通 legacy ShellVerb 是否允许编辑 `<verb>\command` 默认值；多命令父级、DelegateExecute、DropTarget、ExplorerCommandHandler、Shell Extension 和 Win11 项为 false。 |
| `HandlerClsid` | `shellex` handler 的 CLSID。 |
| `FilePath` | 解析出的命令程序或 COM server 路径，best-effort。 |
| `IconPath` / `IconIndex` | 图标路径和索引，best-effort。 |
| `IsEnabled` | 合并真实注册表和项目状态后的启用状态。 |
| `IsPresentInRegistry` | 当前真实注册表是否仍存在该项。 |
| `IsDeleted` | 项目状态库认为该项已删除。 |
| `IsPendingApproval` | 新增项或外部变化需要用户审核。 |
| `HasBackup` / `DeletedAtUtc` | 删除备份相关状态。 |
| `HasConsistencyIssue` / `ConsistencyIssue` | 状态库和真实注册表不一致时的诊断信息。 |
| `DetectedChangeKind` / `DetectedChangeDetails` | 监控发现的新增、删除、修改等变化。 |
| `IsWindows11ContextMenu` | 标识 Win11 packaged context menu。传统菜单通常为 `false`。 |
| `Notes` | 诊断和补充说明，不应参与主逻辑判断。 |

## 5. Snapshot 构建流程

当前后端 snapshot 是“真实注册表 + 状态库”的合并结果，不是单纯枚举注册表：

```text
注册表枚举
-> 构建 actual entries
-> 读取 ContextMenuStateStore
-> 合并 pending / deleted / backup / consistency 状态
-> 标记 enabled / disabled / deleted / pending
-> 检测新增项和外部变化
-> 合并 Windows 11 packaged entries
-> 返回前端 snapshot
```

`ContextMenuRegistryMonitor` 基于周期性 snapshot 比较发现变化。首次 baseline 和用户登录后的 baseline 重建很重要，否则容易把系统已有项误判为新安装项。

## 6. 启用 / 禁用策略

传统菜单的禁用方式按 `EntryKind` 和实际路径分流：

| 类型 | 当前实现倾向 |
| --- | --- |
| `shell` verb | 普通开关通过 `ShellVerbVisibility` 统一判断和写入，综合处理 `HideBasedOnVelocityId`、`ProgrammaticAccessOnly`、`LegacyDisable` 和相关 `CommandFlags`，避免只依赖 `LegacyDisable`。 |
| `shellex` handler | 可能在 `ContextMenuHandlers` 与 disabled mirror 路径之间移动。 |
| disabled mirror path | 用 `-ContextMenuHandlers` 识别被移出的 handler。 |
| Windows 自带标记 | `Extended`、`NoWorkingDirectory`、`NeverDefault` 等属于属性，不等于禁用状态。 |

不要承诺所有菜单项都能用同一种方式开关。某些项由第三方安装器、系统策略或 COM handler 自身逻辑控制，项目只能 best-effort 地修改注册表状态并记录结果。

Recycle Bin 页面额外投影一个虚拟传统项 `special:recyclebin:pintohome`，用于控制系统的“Pin to Quick access” verb。它的真实注册表位置是 `HKCR\Folder\shell\pintohome`，但只在 Recycle Bin 分类显示。启用状态不使用普通 shell verb 隐藏值，而是检查 `AppliesTo` 是否包含 `System.ParsingName:<>"::{645FF040-5081-101B-9F08-00AA002F954E}"`；禁用时只追加这个 Recycle Bin 排除条件，启用时只移除这个排除条件并保留其它 `AppliesTo` 子句。如果 `pintohome` key 不存在，快照不显示该虚拟项。

普通 ShellVerb 的命令文本编辑不解析命令行、不拆分程序和参数、也不重写引号；`SetCommandText` 只把用户输入的字符串原样写到 `<verb>\command` 的默认 `REG_SZ`。后端会先检查 `CanEditCommandText` 和当前注册表形态，并经过 Registry Write Protection preflight；不支持 Shell Extension、Windows 11 packaged context menu、`SubCommands` / `ExtendedSubCommandsKey` 父级、`DelegateExecute`、`DropTarget\CLSID` 或 `ExplorerCommandHandler` 项。

传统分类页支持通过前端的 `CreateSceneMenuItem` 入口创建自定义 classic 菜单项。分类页把当前 `ContextMenuCategory` 映射为 `ContextMenuSceneKind.CustomRegistryPath` 和对应的 HKCR scene root（例如 `HKCR\*\shell`、`HKCR\Directory\shell`、`HKCR\Drive\shell`），再通过 Backend Pipe 交给 `FileTypeSceneMenuService.CreateSceneMenuItemAsync` 写入 `shell\<verb>\command`。首版只创建普通 shell verb 命令：前端负责生成带引号的命令行并追加 `%1` 或 `%V` 选中对象占位符，后端负责 Registry Write Protection preflight、创建不覆盖已有项的唯一 key、通知 Shell 关联变更，并在状态库中抑制本次新建项检测，避免把用户主动创建的项标为待审核。本入口不创建 ShellEx handler、不复制 CommandStore 引用，也不创建子菜单。

## 7. 删除、恢复与备份

删除不总是“立即永久删除”。`RegistryBackupService` 在删除前通过 `reg.exe export` 导出注册表备份，备份文件保存在 `RuntimePaths.DeletedBackupsDirectory`。Installer 包下这是 `%ProgramData%\ContextMenuMgr\DeletedBackups`；Portable 包下这是 `<应用目录>\Data\DeletedBackups`。

| 操作 | 说明 |
| --- | --- |
| 删除 | 尝试备份后删除真实注册表项，并在 `ContextMenuStateStore` 中记录删除状态。 |
| 恢复 | 优先通过备份导入恢复，再更新状态库。 |
| Undo delete | 面向用户的恢复入口，依赖备份存在。 |
| Purge backup | 清理已保留的删除备份，之后恢复能力会下降。 |

保留备份是为了降低误删风险，也让审核中的 Remove 操作可回滚。备份本身不是注册表真实状态，导入失败或目标权限变化仍可能导致恢复失败。

## 8. 状态库

`ContextMenuStateStore` 保存项目自己的状态，路径为 `RuntimePaths.StateDatabasePath`。它用于记录待审核、删除、备份、外部变化和一致性信息。

状态库不是注册表本身。真实注册表可能被第三方安装器、系统更新或用户手工修改，短时间内会与状态库不一致。snapshot 构建会尽量合并并标记不一致，但不要在代码里假设状态库一定代表当前系统真实状态。

## 9. 新增项检测与 Quarantine

`ContextMenuRegistryMonitor` 定期重新取 snapshot，并与 baseline 比较。发现新增项或被外部重新启用的项后，`BackendRuntime` 会调用隔离逻辑，并通过 `BackendNotification` 通知 TrayHost 和前端审核页。

```text
Monitor baseline
-> 周期性 snapshot
-> 对比新增 / 修改 / 删除
-> BackendRuntime.HandleNewItemDetectedAsync
-> QuarantineNewItemAsync
-> PipeNotificationKind.ItemDetected
-> TrayHost 通知 / 前端审核页
```

Quarantine 是后端状态，不只是 UI 徽标。首次 baseline 要谨慎处理，尤其是服务启动、用户登录、Session connect 后，避免把已有菜单全部当成新增项。

## 10. 常见坑

| 坑 | 正确处理 |
| --- | --- |
| 只看 `DisplayName` 判断同一项 | 使用 `Id`、`KeyName`、`RegistryPath`、`HandlerClsid` 等稳定信息。 |
| 把 `HKCR` 当真实写入路径 | 写入时明确选择 `HKEY_USERS\<SID>\Software\Classes` 或 `HKLM\SOFTWARE\Classes`。 |
| 混用用户级和机器级 Classes | 用户级操作必须带前端用户 SID，机器级操作由服务高权限执行。 |
| 用同一种开关策略处理 `ShellVerb` 和 `ShellExtension` | 按 `EntryKind` 和路径分流。 |
| 把 Registry Write Protection 当普通禁用 | Registry Write Protection 是权限保护功能，不是菜单项状态。 |
| 假设状态库和注册表永远一致 | 外部安装器、系统更新和手工修改都会造成短暂不一致。 |
