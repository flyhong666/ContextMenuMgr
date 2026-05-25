<p align="center">
    <img src="./ContextMenuMgr.Frontend/Assets/AppIcon.png" style="height: 100px; width: 100px" />
</p>
<h1 align="center">
  <span>Context Menu Manager Plus</span>
</h1>
<p align="center">
  <span align="center">Context Menu Manager Plus 是一个强大的实用程序，它可帮助您管理 Windows 上的右键菜单，并避免第三方向你的右键菜单里塞屎。</span>
</p>

[English Version](./README.en.md)

> [!WARNING]
> 本项目的相当一部分代码由 AI 辅助生成，并经过持续的人工作业、联调和重构，但它仍然可能存在遗漏、边界情况处理不足或行为与预期不完全一致的问题。
> 如果你在使用过程中发现 Bug、兼容性问题、异常行为或文档缺失，欢迎积极提交 Issue。最好附上复现步骤、日志、截图和系统版本信息，这会非常有帮助。

## 项目简介

`Context Menu Manager Plus` 是一个面向 Windows 的右键菜单管理工具。

它不是一个单纯的“右键菜单开关器”，而是围绕以下目标设计：

- 管理传统右键菜单项
- 管理 Windows 11 新右键菜单项
- 检测第三方软件新增的右键菜单项
- 先拦截，再交给用户审核
- 保护右键菜单相关注册表，减少第三方软件随意添加菜单项
- 提供全局搜索、页面筛选、菜单项深入分析等辅助能力
- 通过后台服务、前端界面、托盘宿主协作，尽量保持长期可用

简单来说，它的目标是：

> 帮你把 Windows 右键菜单从“软件想塞什么就塞什么”，变成“用户决定什么能留下”。

项目基于：

- `.NET 10`
- `WPF`
- `WPF-UI`
- `Named Pipe IPC`
- `Windows Service`
- 原生 Win32 托盘宿主
- 独立 ProbeHost 辅助分析进程

---

## 核心特色

### 先拦截，再审核

这是本项目最重要的设计目标。

新的右键菜单项被检测到后，项目不会默认放任它直接生效，而是尽量执行以下流程：

1. 后端服务检测到新增菜单项
2. 将其标记为待审核
3. 尝试先禁用或拦截该项
4. 在前端“待审核”页面展示
5. 用户手动决定如何处理

用户可以选择：

- `允许`：放行并启用该菜单项
- `保持禁用`：保留该项，但继续禁用
- `移除`：从注册表或审核队列中移除该项

这也是它和普通右键菜单管理器最大的区别。

---

### 后端主导，多进程协作

项目采用后端主导架构。

主要进程包括：

- `ContextMenuManagerPlus.exe`
  - WPF 前端
  - 负责界面展示和用户交互
  - 关闭窗口即退出
- `ContextMenuManagerPlus.Service.exe`
  - 后端 Windows Service
  - 负责扫描、状态管理、注册表操作、审核逻辑、IPC
- `ContextMenuManagerPlus.TrayHost.exe`
  - 每用户托盘宿主
  - 负责托盘图标、通知、托盘菜单、拉起前端
- `ContextMenuMgr.ProbeHost.exe`
  - 独立菜单分析辅助进程
  - 用于隔离加载第三方 Shell Extension
  - 崩溃时不应拖垮主程序或后端服务

前端不常驻后台；托盘由独立 TrayHost 负责；真正的核心逻辑由后端服务负责。

---

### 传统菜单与 Win11 新菜单同时管理

项目同时关注两类菜单：

- 传统右键菜单
  - `shell`
  - `shellex\ContextMenuHandlers`
  - 文件、文件夹、目录背景、桌面背景等经典场景
- Windows 11 新右键菜单
  - Packaged COM
  - AppX / MSIX 相关菜单项
  - Win11 新菜单页面中的禁用 / 恢复

传统菜单和 Win11 新菜单的注册方式不同，因此项目内部会分别处理。

---

### 全局搜索

主窗口标题栏提供全局搜索框。

它不是页面内筛选框，而是一个全局跳转搜索器。

支持搜索：

- 菜单项标题
- 命令文本
- 注册表路径
- 后端注册表路径
- DLL / EXE 文件路径
- CLSID
- Win11 新菜单的包信息与 COM Server 路径

搜索结果会展示：

- 菜单项图标
- 菜单项名称
- 启用 / 禁用状态
- 所属场景或 Win11 新菜单标签
- 跳转提示

选中搜索结果后会：

1. 跳转到对应页面
2. 自动把该菜单项名称填入目标页面的筛选框
3. 尽可能让目标页面只显示该菜单项

全局搜索基于前端已加载的数据进行，不会在每次输入时访问后端、注册表或文件系统。

---

### 页面内多维度筛选

各菜单页面的筛选框也支持多字段搜索。

可匹配内容包括：

- 显示名称
- 键名
- 命令文本
- 注册表路径
- 后端注册表路径
- Shell Extension CLSID
- DLL / EXE 文件路径
- 备注信息
- Win11 包名、发布者、COM Server 路径等

全局搜索和页面筛选尽量使用一致的匹配逻辑，以减少“全局搜得到，页面里搜不到”的割裂感。

---

### Shell Extension 深入分析

对于 Shell Extension / DLL 型菜单项，项目提供“深入分析”能力。

它会尝试在独立 ProbeHost 进程中加载目标 Shell Extension，并解析它实际向右键菜单插入的菜单文字。

需要注意：

- 这是 best-effort 功能
- 不是所有 Shell Extension 都支持单独初始化
- 某些扩展依赖 Explorer 的完整运行环境
- 某些扩展只在特定文件类型、特定状态或特定用户会话下显示
- 某些菜单项是 owner-draw，可能不会暴露普通文本
- 分析失败通常是正常现象，不代表菜单项开关功能有问题

因此，深入分析失败通常不需要报告为 Bug。  
除非它导致主程序崩溃、普通开关功能失效，或出现明显异常行为。

---

### ProbeHost 隔离设计

项目不会在前端或后端服务里直接加载第三方 Shell Extension DLL。

原因是 Shell Extension 本质上是第三方进程内 COM 组件：

- 可能崩溃
- 可能卡死
- 可能访问异常
- 可能依赖 Explorer 环境
- 可能与当前进程架构不匹配

因此项目使用独立的 `ProbeHost` 进程来执行运行时分析。

ProbeHost 的原则：

- 只在用户点击“深入分析”时启动
- 不参与常规扫描
- 不写注册表
- 不执行菜单命令
- 不调用菜单项的实际操作
- 超时或崩溃时由前端捕获并展示友好提示

在发布包中，ProbeHost 会按架构提供：

- `x86`
- `x64`
- `arm64`

在 ARM64 Windows 上，项目会根据目标 DLL 架构选择对应 ProbeHost，例如：

- ARM64 Shell Extension 使用 ARM64 ProbeHost
- x64 Shell Extension 使用 x64 ProbeHost
- x86 Shell Extension 使用 x86 ProbeHost

---

### 注册表保护增强

设置页提供右键菜单相关注册表保护开关。

它的目标是减少第三方软件随意添加或修改右键菜单项。

需要注意：

- 该功能涉及注册表权限调整
- 开启后，某些软件安装器、驱动程序安装器或安全软件可能无法正常写入右键菜单相关注册表项
- 如果你需要安装显卡驱动、压缩软件、网盘客户端、杀毒软件等会写入右键菜单的程序，建议临时关闭相关保护
- 如果软件自身编辑菜单项时被保护机制阻止，界面会提示用户先到设置中解锁后再操作

这是偏高级的功能，不建议不了解其影响的用户长期无脑开启。

---

### 特殊菜单管理

除了常规右键菜单项，项目还覆盖部分特殊菜单：

- 新建菜单
- 发送到
- Win + X 菜单

其中部分功能涉及用户级注册表、排序、隐藏、恢复或权限状态。  
这些区域的行为和普通 `shell` / `shellex` 菜单项不同，因此项目内部会单独处理。

---

## 功能

### 传统右键菜单管理

支持按场景浏览和管理：

- 文件
- 所有对象
- 文件夹
- 目录
- 目录背景
- 桌面背景
- 磁盘分区
- 库
- 此电脑
- 回收站

支持操作：

- 启用 / 禁用菜单项
- 删除菜单项
- 撤销删除
- 永久删除删除备份
- 编辑部分显示名称
- 查看命令文本
- 打开注册表位置
- 打开 CLSID 位置
- 打开文件位置
- 搜索与筛选
- 图标、显示名、命令文本、CLSID 元数据解析

---

### Windows 11 新菜单管理

支持扫描和管理 Windows 11 新右键菜单项。

功能包括：

- 枚举 Win11 新菜单项
- 查看包名、发布者、上下文类型、COM Server 路径等信息
- 启用 / 禁用用户级 Win11 新菜单项
- 与审核队列联动
- 页面内搜索与筛选
- 全局搜索跳转

Win11 新菜单项与传统菜单注册结构不同，因此项目会单独构建快照和禁用状态。

---

### 审核队列

新增菜单项会进入待审核页面。

待审核页支持：

- 允许
- 保持禁用
- 移除
- 聚合同一逻辑项的多个分类来源

新增待审核项时：

1. 后端服务广播事件
2. TrayHost 弹出系统通知
3. 用户点击通知
4. 前端被拉起并跳转到审核页

---

### 外部变化检测

项目会尽量检测外部变化，例如：

- 第三方软件新增菜单项
- 守护离线期间发生的开关变化
- 注册表状态与本地状态库不一致
- 已知项被外部恢复或删除

外部变化检测的目标不是完全替代系统审计，而是帮助用户发现“右键菜单又被软件偷偷改了”。

---

### 文件类型与规则页

文件类型页覆盖：

- 快捷方式
- UWP 快捷方式
- 可执行文件
- 自定义扩展名
- 感知类型
- 目录类型
- 未知类型

其他规则页覆盖：

- 增强菜单
- 详细编辑
- 自定义注册表路径
- 其他右键菜单相关规则

这些页面主要用于查看和管理更底层、更细分的注册表规则。

---

### 设置页

设置页提供：

- 语言切换
  - 跟随系统
  - 简体中文
  - English (United States)
- 主题切换
  - 跟随系统
  - 浅色
  - 深色
- 日志等级
- 随 Windows 启动
- 安装 / 修复服务
- 卸载服务
- 重启资源管理器
- 打开日志目录
- 打开状态库目录
- 打开配置目录
- 注册表保护增强开关

主题设置会在启动时读取并应用，避免打开程序后才切换到正确主题。

---

## 架构

### 1. Backend Service

项目：`ContextMenuMgr.Backend`  
对外可执行文件：`ContextMenuManagerPlus.Service.exe`

Backend Service 是真正的主控层。

职责包括：

- 扫描并解析右键菜单相关注册表项
- 构建传统菜单和 Win11 新菜单快照
- 保存和合并本地状态库
- 执行启用 / 禁用 / 删除 / 恢复
- 执行审核决策
- 执行部分需要高权限的注册表操作
- 提供 Named Pipe IPC
- 广播状态变化和待审核通知
- 在合适时机尝试拉起 TrayHost

---

### 2. Frontend

项目：`ContextMenuMgr.Frontend`  
对外可执行文件：`ContextMenuManagerPlus.exe`

Frontend 是 WPF UI 层。

职责包括：

- 展示主界面
- 展示分类页面
- 展示审核页
- 展示设置页
- 展示 Win11 新菜单页面
- 展示特殊菜单页面
- 提供全局搜索
- 提供页面内筛选
- 提供深入分析结果窗口
- 通过 Named Pipe 与 Backend 通信
- 通过控制管道处理单实例激活和页面跳转

前端是 UI-only：

- 关闭窗口即退出前端进程
- 不负责托盘常驻
- 不直接承载第三方 Shell Extension 分析

---

### 3. Tray Host

项目：`ContextMenuMgr.TrayHost`  
对外可执行文件：`ContextMenuManagerPlus.TrayHost.exe`

TrayHost 是每用户会话侧托盘宿主。

职责包括：

- 托盘图标
- 托盘菜单
- 系统通知
- 打开前端主界面
- 打开审核页
- 请求后端退出
- 响应本地化刷新

TrayHost 使用原生 Win32 托盘实现，不依赖前端窗口常驻。

---

### 4. ProbeHost

项目：`ContextMenuMgr.ProbeHost`  
对外可执行文件：`ContextMenuMgr.ProbeHost.exe`

ProbeHost 是深入分析功能的隔离辅助进程。

职责包括：

- 接收前端传入的分析请求
- 根据示例目标初始化 Shell Extension
- 尝试调用 `IContextMenu.QueryContextMenu`
- 枚举生成的菜单文本
- 将结果写回前端
- 在失败时返回结构化错误

ProbeHost 不应：

- 写注册表
- 执行菜单命令
- 常驻后台
- 参与普通扫描
- 被后端服务直接加载为库

---

### 5. Shared Contracts

项目：`ContextMenuMgr.Contracts`

职责包括：

- IPC 请求 / 响应模型
- 前端、后端、TrayHost、ProbeHost 共享的数据契约
- 通知类型
- 控制命令
- 枚举与常量定义

---

## IPC 与进程协作

### Backend Pipe

主要通过 Named Pipe 做 JSON 请求 / 响应通信，用于：

- 获取菜单快照
- 修改菜单项状态
- 执行审核决策
- 获取 / 设置保护开关
- 管理特殊菜单
- 请求后端尝试拉起 TrayHost
- 请求后端正常关闭

---

### Frontend Control Pipe

前端控制通道用于：

- 单实例激活
- 打开主窗口
- 跳转到指定页面
- 打开审核页
- 聚焦指定审核项
- 正常关闭前端

---

### TrayHost Control Pipe

TrayHost 控制通道用于：

- 退出 TrayHost
- 刷新托盘本地化文案
- 响应前端或后端的控制请求

---

### ProbeHost 通信

ProbeHost 由前端按需启动。

它通过临时请求 / 结果文件或标准输出等受控方式交换数据，并由前端设置超时。  
如果 ProbeHost 崩溃、超时或返回异常结果，前端会将其转为用户可理解的失败状态。

---

## 主要注册表范围

项目重点处理以下范围：

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
- 文件类型、扩展名、感知类型、目录类型相关分支
- 用户级 `HKCU/HKEY_USERS\<SID>\Software\Classes` 对应范围
- Win11 新菜单相关用户级阻止列表

需要注意，Windows 的右键菜单注册方式非常复杂，不同软件、不同菜单类型、不同系统版本的行为可能并不一致。

---

## 仓库结构

```text
ContextMenuMgr/
├─ .github/                        # GitHub Actions
├─ artifacts/                      # 本地构建中间产物
├─ build/                          # publish 输出与安装包
├─ ContextMenuMgr.Backend/         # Windows Service / 核心后端
├─ ContextMenuMgr.Contracts/       # 共享契约
├─ ContextMenuMgr.Frontend/        # WPF 前端
├─ ContextMenuMgr.ProbeHost/       # 深入分析辅助进程
├─ ContextMenuMgr.TrayHost/        # 每用户 Tray 宿主
├─ Installer/                      # Inno Setup 脚本与相关资源
├─ Scripts/                        # 构建脚本模块
├─ build.ps1                       # 主构建脚本
├─ build.bat                       # build.ps1 批处理入口
├─ ContextMenuMgr.slnx             # 解决方案
├─ README.md                       # 中文 README
└─ README.en.md                    # 英文 README
```

---

## 可执行文件与产物

对外主要程序名称：

- 前端：`ContextMenuManagerPlus.exe`
- 后端服务：`ContextMenuManagerPlus.Service.exe`
- 托盘宿主：`ContextMenuManagerPlus.TrayHost.exe`

内部辅助程序：

- ProbeHost：`ContextMenuMgr.ProbeHost.exe`

ProbeHost 通常位于：

```text
ProbeHost/
├─ x86/
├─ x64/
└─ arm64/
```

不同发布包会按需要携带不同架构的 ProbeHost。

---

## 开发环境要求

- Windows 10 / 11
- .NET SDK 10
- PowerShell 5.1 或更高
- Inno Setup 6
  - 默认优先使用仓库内置：
    - `Installer\Inno Setup 6\ISCC.exe`

---

## 本地开发构建

```powershell
dotnet restore .\ContextMenuMgr.slnx --configfile .\NuGet.Config
dotnet build .\ContextMenuMgr.slnx --no-restore
```

如只开发前端，也可以构建前端项目：

```powershell
dotnet build .\ContextMenuMgr.Frontend\ContextMenuMgr.Frontend.csproj
```

前端构建会尽量复制后端、TrayHost、ProbeHost 等运行所需产物，便于本地调试。

---

## 发布与安装包构建

直接执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Configuration Release
```

### `build.ps1` 的行为

构建脚本会：

1. `restore` 解决方案
2. 分别 `publish`：
   - Frontend
   - Backend
   - TrayHost
   - ProbeHost
3. 针对不同架构生成产物：
   - `win-x64`
   - `win-x86`
   - `win-arm64`
4. 针对不同分发模式生成产物：
   - `self-contained`
   - `framework-dependent`
5. 调用 Inno Setup 生成安装包

ProbeHost 会按目标发布包携带对应架构：

- `win-x86`
  - `x86`
- `win-x64`
  - `x64`
  - `x86`
- `win-arm64`
  - `arm64`
  - `x64`
  - `x86`

其中 Release 包中的 ProbeHost 倾向使用 self-contained single-file 发布，以避免用户缺少对应架构 .NET Runtime 导致深入分析无法启动。

默认输出目录：

- publish 输出：`build\publish\`
- 安装包输出：`build\dist\`

`build\dist\` 中还会生成：

- `artifacts.txt`

用于列出本次生成的安装包列表。

---

## GitHub Actions

仓库中包含：

- `.github/workflows/manual-release.yml`

工作流行为：

- 支持手动触发
- 支持 tag 发布
- 调用 `build.ps1`
- 上传构建产物
- 自动创建 Draft Release
- Release 的版本号和标题会从项目版本信息中解析

---

## 本地数据、日志与状态库

### Frontend

配置：

```text
%LocalAppData%\ContextMenuMgr\frontend-settings.json
```

日志：

```text
%LocalAppData%\ContextMenuMgr\Logs\frontend-debug.log
%LocalAppData%\ContextMenuMgr\Logs\frontend-crash.log
```

---

### TrayHost

日志：

```text
%LocalAppData%\ContextMenuMgr\Logs\trayhost.log
```

---

### Backend

日志：

```text
%ProgramData%\ContextMenuMgr\Logs\backend.log
```

状态库：

```text
%ProgramData%\ContextMenuMgr\Data\context-menu-state.json
```

说明：

- 产品对外名称为 `Context Menu Manager Plus`
- 本地数据目录保留 `ContextMenuMgr` 历史命名，以兼容旧数据

---

## 运行与恢复说明

### 正常链路

1. 前端启动
2. 前端连接 Backend Service
3. 前端请求后端确保 TrayHost 存在
4. 后端按当前用户会话拉起 TrayHost
5. 前端加载菜单快照并展示

---

### 审核通知链路

1. Backend 检测到新增菜单项
2. Backend 将其加入待审核队列
3. Backend 广播通知
4. TrayHost 收到事件后弹出系统通知
5. 用户点击通知
6. TrayHost 拉起 Frontend
7. Frontend 跳转到审核页

---

### 异常恢复

如果 Backend Service 异常退出：

- TrayHost 可能无法继续提供完整能力
- Frontend 不应被强制退出
- 用户可以在设置页使用：
  - 安装 / 修复服务

如果注册表保护导致某些操作失败：

- 可以先在设置页关闭对应保护
- 完成编辑或安装后再重新开启

如果 ProbeHost 深入分析失败：

- 通常不影响菜单项开关功能
- 可查看日志或诊断信息
- 不建议仅因为分析不出菜单文字就提交 Bug

---

## 注意事项

- 本项目涉及注册表修改，请谨慎使用
- 删除、禁用、ACL 保护等操作可能受系统权限、安全软件或 Windows 保护机制影响
- 某些系统保护的注册表根键无法被普通方式修改 ACL，这是 Windows 本身限制
- 某些安全软件可能会拦截删除、恢复、注册表写入或 Shell Extension 探测
- 图标、显示名、命令文本、CLSID 元数据解析均为 best-effort，不保证覆盖所有第三方菜单项
- Shell Extension 深入分析是 best-effort，失败通常是正常现象
- 用户级右键菜单项、系统级右键菜单项、PackagedCom、ShellEx / CLSID 项的行为并不完全一致
- 开启注册表保护后，部分软件安装器或驱动安装器可能无法写入右键菜单相关项
- 遇到边界情况时，日志通常比界面提示更有价值

## 反馈建议

如果你要提 Issue，建议至少附上：

- Windows 版本
- 出问题的菜单项名称 / 注册表路径
- 前端日志
- backend 日志
- trayhost 日志（如果问题涉及托盘）
- 复现步骤
- 截图或录屏

## 贡献指南

本项目中内置有开发文档（位于 `docs/` 目录下），并附带有 `AGENTS.md`、`CLAUDE.md`，可以直接利用主流的 Agent 工具为本项目做出贡献

## License

本项目遵循 GPL V3.0 协议开源，See [LICENSE](./LICENSE).

## 参考与致谢

本项目代码大量参考自以下项目：

- https://github.com/BluePointLilac/ContextMenuManager
- https://github.com/branhill/windows-11-context-menu-manager


## Stargazers over time
[![Stargazers over time](https://starchart.cc/PLFJY/ContextMenuMgr.svg?variant=adaptive)](https://starchart.cc/PLFJY/ContextMenuMgr)

​                    
