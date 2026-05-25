# Windows 11 新右键菜单实现说明

## 1. Windows 11 新菜单和传统菜单的区别

Windows 11 新右键菜单不等同于传统 `shell` / `shellex\ContextMenuHandlers`。当前实现重点处理 packaged context menu：菜单声明来自 AppX / MSIX package 的 manifest，COM 信息来自 `PackagedCom` 和 package repository，禁用状态通过 Shell Extensions blocked list 表达。

传统菜单可以在 `ContextMenuRegistryCatalog` 的 `MonitoredRoots` 中扫描；Windows 11 新菜单由 `Windows11ContextMenuCatalog` 单独枚举，并通过 `IsWindows11ContextMenu = true` 标记。不要用普通 `LegacyDisable` 或 disabled mirror 策略处理 Win11 新菜单。

## 2. 扫描来源

当前后端扫描主要基于以下来源：

| 来源 | 作用 |
| --- | --- |
| `PackagedCom\Package` | 从 `HKCR` 合并视图读取 packaged COM package 名称。 |
| `PackageRepository\Packages` | 读取 package 安装路径和版本等信息。 |
| `AppxManifest.xml` | package 主 manifest。 |
| `AppxMetadata\AppxBundleManifest.xml` | 主 manifest 不存在时的 fallback。 |
| `FileExplorerContextMenus` | manifest 中声明 Explorer context menu verb 的位置。 |
| `ComServer` | manifest 中声明 COM server 和 class 的位置。 |
| `ContextTypes` | 由 manifest 的 item type 映射到项目的 `ContextMenuCategory`。 |

manifest 解析是 best-effort。缺少命名空间、manifest 不存在、XML 结构变化或包数据异常时，当前代码会跳过或记录 warning，而不是让整个 snapshot 失败。

## 3. Windows11ContextMenuCatalog

`Windows11ContextMenuCatalog` 位于 `ContextMenuMgr.Backend/Services/Windows11ContextMenuCatalog.cs`。当前流程如下：

```text
检查 Windows 版本 >= 10.0.22000
-> 获取 frontend user SID
-> 枚举 PackagedCom package
-> 从 PackageRepository 解析安装路径
-> 读取 AppxManifest.xml 或 AppxBundleManifest.xml
-> 解析 FileExplorerContextMenus
-> 解析 ComServer
-> 匹配 CLSID
-> 根据 ContextTypes 映射 ContextMenuCategory
-> 根据 blocked list 判断 IsEnabled
-> 创建 ContextMenuEntry
```

生成的 `ContextMenuEntry` 使用 `win11|{clsid}|{category}` 形态的 `Id`，`RegistryPath` 指向 `PackagedCom\Package\...\Class\{CLSID}`，`BackendRegistryPath` 指向当前用户的 blocked list。`FilePath` 会尽量使用 COM server 路径，解析不到时回退到 package 安装目录。

## 4. 启用 / 禁用模型

Win11 新菜单通过 blocked list 启用或禁用：

| 范围 | 路径 | 说明 |
| --- | --- | --- |
| 机器级 | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked` | 对所有用户生效，当前 `Windows11BlocksService` 支持读写。 |
| 用户级 | `HKEY_USERS\<sid>\Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked` | 对指定用户生效，必须带正确用户 SID。 |

`SetEnabled` 的本质是写入或删除 CLSID value：禁用时写入 CLSID value，启用时删除该 value。机器级 blocked 优先导致禁用；即使用户级没有 blocked value，只要 HKLM blocked list 存在该 CLSID，snapshot 仍应显示禁用。

## 5. 为什么 userContext 必须正确

Win11 新菜单的 snapshot 和开关都依赖用户上下文：

| 场景 | userContext 错误的后果 |
| --- | --- |
| snapshot | `IsEnabled` 可能读取错误用户 hive，刷新后显示状态错误。 |
| 禁用 | blocked value 写到错误 SID 下，前端看似成功但 Explorer 对当前用户不生效。 |
| 恢复 | 从错误 SID 删除 value，当前用户仍被禁用。 |
| 审核 | 待审核项和逻辑 identity 可能与真实用户状态不一致。 |

`Windows11ContextMenuCatalog` 在缺少 userContext 时有交互式 SID fallback，但代码注释已经表明这不应是主路径。正常 runtime 请求应由 `NamedPipeBackendServer` 解析前端用户上下文并传入。

## 6. 前端服务和页面

前端由 `ContextMenuMgr.Frontend/Services/Windows11ContextMenuService.cs` 和 `Windows11ContextMenuPageViewModel.cs` 负责展示与操作：

| 组件 | 职责 |
| --- | --- |
| `Windows11ContextMenuService` | 通过后端 snapshot 构建 `Windows11ContextMenuItemDefinition`，维护 `CurrentItems`，调用 `SetWin11BlockedItemAsync` 和 `RemoveWin11BlockedItemAsync`。 |
| `Windows11ContextMenuPageViewModel` | 展示条目、分组、筛选、刷新、处理全局搜索跳转。 |
| `CurrentItems` | 前端缓存的 Win11 条目池，避免页面每次筛选都访问后端。 |
| `ContextMenuSearchMatcher` | 全局搜索和页面筛选共用匹配逻辑。 |
| `GlobalSearchNavigationFilterService` | 搜索结果跳转到 Win11 页面后设置页面筛选文本和目标项。 |

Win11 页面筛选是前端内存筛选。只有刷新 snapshot 或执行开关操作时才需要访问后端。

## 7. 常见坑

| 坑 | 正确处理 |
| --- | --- |
| 用传统 `shell` / `shellex` 策略禁用 Win11 新菜单 | 使用 blocked list。 |
| 在 snapshot 中丢掉 userContext | `IsEnabled` 会读错用户 hive。 |
| 禁用写到服务 `HKCU` | 写 `HKEY_USERS\<sid>\...\Blocked`。 |
| 假设一个 CLSID 只对应一个 context type | manifest 中一个 CLSID 可能声明多个类型，项目会映射到多个 `ContextMenuCategory`。 |
| 假设 Win11 项一定有普通 DLL 图标 | packaged COM 的图标和 server 路径解析都是 best-effort。 |
| 把 manifest 解析失败当严重错误 | AppxManifest 解析是 best-effort，应记录并跳过异常 package。 |
