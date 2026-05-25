# 构建与发布说明

## 1. 构建入口

当前仓库主要构建入口如下：

| 入口 | 作用 |
| --- | --- |
| `build.ps1` | 本地完整发布入口，默认构建 installer 和 portable 产物。 |
| `build-release.bat` | 调用 `build.ps1 -Configuration Release` 的批处理入口。 |
| `build-beta.bat` | 调用 `build.ps1 -Configuration Beta` 的批处理入口。 |
| `Scripts/Build-Target.ps1` | GitHub Actions 和单目标构建使用的入口。 |
| `Scripts/Build.Common.psm1` | restore、publish、打包、ProbeHost 多架构映射、Inno Setup 调用等公共函数。 |
| `NuGet.Config` | restore 使用的 NuGet 配置。 |

当前代码库没有发现名为 `build.bat` 的入口；不要在文档或自动化中假设它存在。

## 2. 项目产物

| 项目 | 输出 | 说明 |
| --- | --- | --- |
| Frontend | `ContextMenuManagerPlus.exe` | WPF UI，最终发布目录的主程序。 |
| Backend Service | `ContextMenuManagerPlus.Service.exe`、`ContextMenuManagerPlus.Service.dll` | Windows Service / bootstrapper 相关后端程序。 |
| TrayHost | `ContextMenuManagerPlus.TrayHost.exe` | 每用户托盘进程。 |
| ProbeHost | `ProbeHost\<arch>\ContextMenuMgr.ProbeHost.exe` | Deep Analysis 多架构隔离进程。 |
| Contracts | `ContextMenuMgr.Contracts.dll` | pipe 契约、模型、共享路径常量，ProbeHost framework-dependent 输出也需要它。 |

## 3. Debug 本地构建

`ContextMenuMgr.Frontend.csproj` 在普通 framework-dependent build 中会负责准备运行所需辅助产物：

```text
Frontend build
-> 构建 framework-dependent ProbeHost x86 / x64 / arm64
-> 复制 Backend artifacts
-> 复制 TrayHost artifacts
-> 复制 ProbeHost\x86 / x64 / arm64
-> 验证 ProbeHost framework-dependent 依赖文件
-> 执行 Verify-ProbeHostArchitecture.ps1
```

前端项目会检查各架构 ProbeHost 目录中的 `dll`、`deps.json`、`runtimeconfig.json` 和 `ContextMenuMgr.Contracts.dll`。如果只复制 exe，Deep Analysis 会在运行时出现 `ProbeHostDependencyMissing` 或无效输出。

## 4. Release 发布

`build.ps1` 负责组合多个平台和分发模式。`Scripts/Build.Common.psm1` 中的关键步骤包括：

| 步骤 | 说明 |
| --- | --- |
| restore | 对 Frontend、Backend、TrayHost、ProbeHost 分别运行 `dotnet restore`，使用 `NuGet.Config`。 |
| publish | 对项目运行 `dotnet publish`，按平台和分发模式输出到 publish workspace。 |
| self-contained | 发布时携带 .NET runtime，安装包不依赖本机已安装 runtime。 |
| framework-dependent | 依赖本机 .NET runtime，安装包可启用 .NET dependency installer。 |
| installer | 调用 Inno Setup 生成安装包。 |
| portable | 当前支持 `framework-dependent` + `anycpu` 的 portable zip。 |
| `artifacts.txt` | `build.ps1` 在 `build/dist` 下写入产物清单。 |

`Build-Target.ps1` 对 installer 目标要求平台是 `win-x64`、`win-x86` 或 `win-arm64`；portable 当前只支持 `framework-dependent anycpu`。

## 5. ProbeHost 多架构

`Get-ProbeHostArchitectureMap` 定义发布包应携带的 ProbeHost 架构：

| 发布平台 | ProbeHost 架构 |
| --- | --- |
| `win-x86` | x86 |
| `win-x64` | x64、x86 |
| `win-arm64` | arm64、x64、x86 |

这样做是因为目标 Shell Extension DLL 的架构可能与主程序平台不同。例如 x64 系统仍可能安装 x86 handler，arm64 系统也可能需要 x64 或 x86 ProbeHost。

常见错误包括：目录标签和 PE 架构不一致、只发布主架构缺少 x86 fallback、framework-dependent 目录缺少 `ContextMenuMgr.Contracts.dll`、遗漏 `deps.json` 或 `runtimeconfig.json`。

当前 Release 脚本发布 ProbeHost 时传入 `--self-contained true` 和 `-p:PublishSingleFile=false`，所以 ProbeHost 是 self-contained 多文件输出。不要把 Release ProbeHost 文档或校验逻辑写成 single-file。

## 6. Inno Setup

安装包脚本位于 `Installer/build_Installer.iss`。构建脚本会定位 `ISCC.exe`，优先检查仓库内 `Installer\Inno Setup 6\ISCC.exe`，然后检查 Program Files 和 PATH。

`Invoke-InstallerBuildTarget` 会向 Inno Setup 传入应用版本、AppId、发布目录、输出目录、架构选项和是否启用 .NET dependency installer。framework-dependent 安装包会把 `MyUseDotNetDependencyInstaller` 设为 `1`。

## 7. GitHub Actions

`.github/workflows/manual-release.yml` 是当前手动发布 workflow。它通过 `workflow_dispatch` 接收 `configuration` 和 `app_id`，主要流程是：

```text
resolve-metadata
-> Resolve-Version.ps1
-> New-ReleaseNotes.ps1
-> build matrix
-> Scripts/Build-Target.ps1
-> 上传 installer / portable artifacts
-> 汇总 artifacts
-> New-ChecksumTable.ps1
-> 创建 draft release
```

构建矩阵覆盖 `win-x64`、`win-x86`、`win-arm64` 的 self-contained 和 framework-dependent installer，以及 framework-dependent portable。

## 8. 构建排错

| 问题 | 优先检查 |
| --- | --- |
| 缺 .NET SDK | `dotnet --info`，workflow 使用 `10.0.x`。 |
| Inno Setup 找不到 | `Scripts/Build.Common.psm1` 的 `Get-InnoSetupCompilerPath` 搜索路径。 |
| ProbeHost 缺依赖 | `ContextMenuMgr.Frontend.csproj` 的 framework-dependent 检查和发布目录内容。 |
| ProbeHost 架构错 | `Scripts/Verify-ProbeHostArchitecture.ps1` 输出和 `ProbeHost\<arch>` 目录。 |
| artifacts 目录污染 | 清理 `build\publish-runs`、`build\publish`、`build\dist` 后重试。 |
| framework-dependent 包运行时缺失 | 检查目标机器 .NET runtime 或安装包 dependency installer 设置。 |
| portable 目标参数错误 | 当前只支持 `-Kind portable -Platform anycpu -DistributionMode framework-dependent`。 |
