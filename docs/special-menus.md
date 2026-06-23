# SpecialMenu 实现说明

## 1. 为什么 SpecialMenu 不是普通右键菜单

SpecialMenu 指当前代码中由 `SpecialMenuService` 管理、但不适合放进 `ContextMenuRegistryCatalog` 的菜单面。它们不完全遵循传统 `shell` / `shellex\ContextMenuHandlers` 模型：

| SpecialMenu | 为什么特殊 |
| --- | --- |
| `ShellNew` | “新建”菜单来自扩展名下的 `ShellNew` 子键，同时还受 Explorer ShellNew order key 和排序值影响。 |
| `SendTo` | “发送到”菜单主要来自用户 profile 下的文件系统目录。 |
| `WinX` | Win+X 菜单来自用户 `LocalAppData` 下的分组目录、`.lnk` 文件和 hash。 |
| `DragDrop` | 右键拖放默认效果和 handler 配置不是普通菜单项。 |
| `CommandStore` | Explorer command store 是另一套命令注册表模型。 |
| `GuidBlock` | 通过 GUID blocked list 屏蔽对象，不是普通 verb。 |
| `InternetExplorer` | IE MenuExt 是旧式浏览器扩展入口。 |

因此不要用 `ContextMenuRegistryCatalog` 的普通开关、删除、恢复逻辑直接处理 SpecialMenu，除非代码已经明确把某个路径泛化。

## 2. SpecialMenuService 总览

`SpecialMenuService` 位于 `ContextMenuMgr.Backend/Services/SpecialMenuService.cs`。当前 `SpecialMenuKind` 定义在 `ContextMenuMgr.Contracts/SpecialMenuContracts.cs`：

| 类型 | 主要数据位置 | 用户上下文要求 |
| --- | --- | --- |
| `ShellNew` | `Software\Classes\<.ext>\ShellNew` 和 Explorer ShellNew order key | 需要用户 SID，排序和用户级 Classes 要写 `HKEY_USERS\<SID>`。 |
| `SendTo` | `%APPDATA%\Microsoft\Windows\SendTo` | 需要用户 profile path / RoamingAppData。 |
| `WinX` | `%LOCALAPPDATA%\Microsoft\Windows\WinX` | 需要用户 LocalAppData。 |
| `DragDrop` | 相关 Classes 注册表路径 | 当前实现可在服务侧处理，仍需谨慎区分用户级与机器级。 |
| `CommandStore` | Explorer CommandStore 注册表路径 | 不等同于普通右键菜单。 |
| `GuidBlock` | GUID blocked list | 用于屏蔽指定 GUID。 |
| `InternetExplorer` | IE MenuExt 注册表路径 | 旧式功能，当前实现按执行进程的 `HKCU` 处理，属于 best-effort；后续若要面向前端用户，应补用户上下文路径。 |

前端主要由 `SpecialMenuPageViewModel` 组装 `PipeRequest`，后端由 `NamedPipeBackendServer` 分发到 `SpecialMenuService`。

## 3. ShellNew

ShellNew 是 SpecialMenu 中最容易踩坑的部分。

| 概念 | 当前实现说明 |
| --- | --- |
| `ShellNew` 子键 | 位于扩展名 Classes 下，例如 `Software\Classes\.txt\ShellNew`。 |
| 文件扩展名与新建项 | 每个扩展名可能对应一个“新建”菜单项，显示名可能来自文件类型、友好名或项目推断。 |
| `NullFile` | 表示创建空文件。当前创建请求未提供 `DataText` 时倾向写入 `NullFile`。 |
| `Data` | 当前 `ShellNewCreateRequest` 支持 `DataText`，后端会写入二进制 `Data`。 |
| `Command` | `ShellNewUpdateRequest` 支持更新 `Command`。 |
| 创建请求字段 | 新建时会应用前端表单中的扩展名、显示名、图标路径、命令、数据文本和 `BeforeSeparator`；命令优先于 `Data`，没有命令和数据时写 `NullFile`。 |
| `FileName` | Windows ShellNew 支持这种方式，但当前代码重点处理的是 `NullFile`、`Data`、`Command` 和相关元数据。 |
| `Config\BeforeSeparator` | 当前更新请求支持 `BeforeSeparator`。 |
| Explorer ShellNew order key | `Software\Microsoft\Windows\CurrentVersion\Explorer\Discardable\PostSetup\ShellNew`，控制 Explorer 侧排序信息。 |
| Classes 排序值 | 排序会同时考虑扩展名、Explorer order key 和 Classes 相关数据。 |

创建 ShellNew 时，后端必须先基于前端用户上下文解析文件类型，而不能使用服务进程的 `Registry.ClassesRoot` 来验证 ProgId。解析顺序是用户 `HKU\<SID>\Software\Classes\.ext`、机器 `HKLM\SOFTWARE\Classes\.ext`、用户 `FileExts\.ext\UserChoice\ProgId`，再考虑 `OpenWithProgids`。ProgId 只在同一组用户 / 机器 Classes 根中存在时才视为有效；`UserChoice` 只读不写，不修改 hash，也不会为项目创建全局 fake ProgId。

即使扩展名没有出现在用户或机器 Classes 中，创建仍会继续写用户级 `HKU\<SID>\Software\Classes\.ext\ShellNew`。ProgId 是可选元数据：解析到有效 ProgId 时用于 per-user `FriendlyTypeName` 和返回项 metadata；没有 ProgId 时仍创建扩展名级 ShellNew，显示名优先写到 ShellNew 的 `MenuText` 并用于返回项。`UserChoice` 始终只读，不写入、不修改 hash。

排序复杂的原因是 Explorer “新建”菜单不是简单按注册表子键自然顺序显示。`SpecialMenuService` 的 `MoveShellNewAsync` 要求 ShellNew order lock 已启用；移动时会用简单 unlock 临时移除 WorldSid deny 规则，更新 `Classes` 排序值，再按原锁定状态重新加锁。

ShellNew ACL lock / unlock 只针对 ShellNew order key 相关保护。当前实现使用 v2 narrow lock：锁定时读取现有 DACL，移除重复的显式 WorldSid deny 规则和可读取的旧版 broad `WriteKey` deny 规则，再添加一个 WorldSid `Deny SetValue | CreateSubKey | Delete` 规则。v2 故意不使用 `RegistryRights.WriteKey`，因为 .NET 中 `WriteKey` 过宽，可能阻止后续读取 / 修改 ACL，导致本程序无法正常解锁。解锁时会移除旧版 WorldSid `WriteKey` deny 规则和 v2 narrow deny 规则。它只请求 `ReadPermissions` / `ChangePermissions`，不 deny `ReadPermissions`、`ChangePermissions`、`TakeOwnership` 或 `FullControl`，保留现有继承和显式 allow 规则，不合成替换 DACL，不调用 take ownership，也不使用 `SeTakeOwnershipPrivilege` / `SeRestorePrivilege` 修复 ShellNew ACL。这里即使服务是 LocalSystem，也不能把用户级路径写到 SYSTEM 的 `HKCU`；用户级 Classes 和 Explorer order key 必须定位到 `HKEY_USERS\<sid>`。

如果旧版本 broad `WriteKey` lock 或外部工具留下的 broken ACL 无法通过 `ReadPermissions | ChangePermissions` 安全修改，本程序会返回明确错误：需要先用 BluePointLilac / ContextMenuManager 或系统工具解锁 / 修复，然后重试。主后端不再执行 take ownership、replacement DACL 或自动 ACL reset。

## 4. ShellNew ACL Lock 与 Registry Write Protection 的区别

| 项目 | ShellNew ACL Lock | Registry Write Protection |
| --- | --- | --- |
| 保护范围 | 主要针对 Explorer ShellNew order key / 新建菜单排序保护。 | 更广泛的传统右键菜单注册表写入保护。 |
| 代码路径 | `SpecialMenuService.SetShellNewOrderLockAsync`，兼容旧前端命令的 `RepairShellNewOrderAclAsync` 只执行简单 unlock。 | `ContextMenuRegistryCatalog` 的注册表写保护相关逻辑。 |
| 用户提示 | 主要围绕 ShellNew 排序锁定 / 解锁；无法安全修改 ACL 时提示外部修复。 | 编辑菜单项时可能提示去设置页解锁。 |
| 失败形态 | legacy broad `WriteKey` 或 broken ACL 可能导致 `ChangePermissions` 修改失败；主程序不会 take ownership 或替换 DACL。 | 可能阻止第三方安装器或本程序运行时写入受保护路径。 |

不要把两者混用。ShellNew ACL Lock 不是普通菜单禁用，也不是全局右键菜单保护开关。

## 5. SendTo

SendTo 菜单来自用户 profile 下的目录，当前代码通过 `BackendUserContext.GetSendToPath()` 定位到 RoamingAppData 的 `Microsoft\Windows\SendTo`。

| 行为 | 当前实现倾向 |
| --- | --- |
| 枚举 | 读取 SendTo 目录中的文件和快捷方式。 |
| 启用 / 禁用 | 通过 hidden 属性等文件系统状态表达。 |
| 删除 / 恢复 | 使用 `.deleted` 机制进行软删除和恢复。 |
| 编辑 | 更新 `.lnk` 的目标、参数、工作目录、图标等。 |

除了用户 SendTo 目录中的真实文件，快照还会追加 Explorer 动态生成的当前可用可移动磁盘目标，例如 USB 盘。此类项来自 `DriveInfo.GetDrives()` 中 `DriveType.Removable` 且 `IsReady` 的驱动器，使用 `sendto:drive:X` 形式的稳定 id，并通过 metadata 标记为 Explorer-generated dynamic target。它们不对应 SendTo 目录内的文件，因此 `CanEdit=false`、`CanDelete=false`，前端不显示开关，后端也不会对它们执行创建、删除、编辑或 hidden 属性切换。

SendTo 是文件系统菜单，不是 registry-only 功能。没有正确 profile path 时，服务可能改到错误用户目录或找不到目标。

## 6. WinX

WinX 菜单来自用户 `LocalAppData` 下的 `Microsoft\Windows\WinX` 目录。它包含分组、排序、`.lnk` 文件和 hash。

| 行为 | 当前实现倾向 |
| --- | --- |
| 分组 | 以 group 目录组织。 |
| 排序 | 通过文件名前缀和分组位置处理，移动操作只移动/重命名 `.lnk` 和对应的 desktop.ini 本地化名称，不重写 WinX hash。 |
| `.lnk` | 条目是快捷方式，需要写目标、参数和工作目录。 |
| hash | `WinXHasher.HashLnk` 使用硬编码 PKEY 写入 Explorer 接受的 WinX 快捷方式 hash；新增快捷方式、恢复默认组、修改 target/arguments 后必须重新 hash。显示名、图标、RunAs 和移动排序不重新 hash。hasher 不允许使用空 target 计算 hash，并会记录 property-store/fallback/final target 与 arguments。 |
| 删除 | 当前 WinX 删除是直接删除用户 WinX 目录下的 `.lnk` 或分组目录，不使用 `.deleted` 软删除；Undo/Purge 对 WinX 已禁用，直到有持久 manifest-based undo 模型。 |
| 恢复默认 | 禁止恢复整个 WinX 根目录；只允许恢复单个 group，恢复前会把现有 group 移到 `.backup`，复制失败时回滚。`.deleted` 和 `.backup` 不作为普通 WinX group 枚举。 |

WinX 修改需要用户上下文，因为不同用户有不同的 LocalAppData 和 WinX 配置。

WinX 快捷方式的创建、更新和读取由后端 `WinXShortcutFile` 使用原生
`IShellLinkW` / `IPersistFile` 完成。每次操作在短生命周期 STA 线程中显式初始化
COM，不调用 `IShellLink.Resolve`，也不使用 `WScript.Shell`。SendTo 当前仍保留原有
`ShortcutFile` 路径，以限制改动范围。

创建 WinX 条目时，`backend.log` 会记录 `WinXCreateStart`、
`WinXShortcutWriteStart/End`、`WinXDesktopIniWriteStart/End`、
`WinXHashStart/End` 和 `WinXCreateEnd`。若创建失败，`WinXCreateFailed` 会记录失败
阶段；COM 异常同时记录 HRESULT。

## 7. SpecialMenu 的用户上下文要求

| SpecialMenu | 需要 SID | 需要 ProfilePath | 需要 LocalAppData/RoamingAppData | 原因 |
| --- | --- | --- | --- | --- |
| `ShellNew` | 是 | 通常需要 | 需要用户 hive 和 Explorer order key | 用户级 Classes 和 ShellNew order key 必须写 `HKEY_USERS\<SID>`。 |
| `SendTo` | 通常需要 | 是 | 需要 RoamingAppData | SendTo 是用户 profile 下目录。 |
| `WinX` | 通常需要 | 是 | 需要 LocalAppData | WinX 是每用户文件系统配置。 |
| `DragDrop` | 视具体操作而定 | 通常不需要 | 通常不需要 | 当前实现主要按注册表路径处理。 |
| `CommandStore` | 视具体操作而定 | 通常不需要 | 通常不需要 | 属于 Explorer 命令注册表模型。 |
| `GuidBlock` | 视具体操作而定 | 通常不需要 | 通常不需要 | 以 GUID blocked list 为主。 |
| `InternetExplorer` | 视具体操作而定 | 通常不需要 | 通常不需要 | 旧式 IE MenuExt 路径。 |

这里的“通常需要”表示当前实现会通过 `BackendUserContext` 获取 SID 或 profile 目录。不要用 LocalSystem 的环境变量推断前端用户路径。

## 8. 常见坑

| 坑 | 正确处理 |
| --- | --- |
| 用普通 `ContextMenuRegistryCatalog` 处理 ShellNew 排序 | 走 `SpecialMenuService` 的 ShellNew 专用逻辑。 |
| 用服务 `HKCU` 写 ShellNew | 写 `HKEY_USERS\<sid>`。 |
| 把锁定失败简单判断为权限不够 | ACL deny、所有者、继承状态都可能影响 `ReadPermissions` / `ChangePermissions`；证据不足时不要推断根因。 |
| 期望主程序自动修复 legacy broad lock / broken ACL | ShellNew lock/unlock 不再 take ownership、不替换 DACL；旧版 `WriteKey` broad lock 无法安全修改时应使用 BluePointLilac / ContextMenuManager 或系统工具外部解锁 / 修复。 |
| 把 SendTo / WinX 当 registry-only 功能 | 它们主要是用户文件系统目录。 |
| 把 ShellNew ACL Lock 和 Registry Write Protection 混用 | 两者保护范围、代码路径和用户提示都不同。 |
