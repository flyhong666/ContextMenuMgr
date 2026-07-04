# Package Manager Publishing

本文档说明 Context Menu Manager Plus 的 Scoop 和 winget 发布自动化。该自动化只处理 GitHub Release 产物和包管理器 manifest，不改变安装器 AppId、服务名、安装目录、注册表逻辑或运行时权限链路。

## 1. 触发方式

包管理器发布 workflow 是 `.github/workflows/publish-package-managers.yml`。

正式路径使用：

```yaml
on:
  release:
    types: [published]
```

原因是当前 `.github/workflows/manual-release.yml` 只创建 draft GitHub Release，最终发布由维护者手动完成。包管理器 manifest 必须指向已经公开可下载的 Release asset，因此不能用 tag push 或 draft release 创建事件触发。

workflow 也支持 `workflow_dispatch`，用于对指定已发布 tag 做 dry-run。发布 workflow 只处理真实 GitHub Release：它会读取 `release.published` 事件中的 Release，或读取手动输入的已发布 tag，然后下载该 Release 的真实 assets、计算真实 SHA256、生成 Scoop 和 winget manifests，运行 `winget validate --ignore-warnings` 验证真实生成的 winget manifests，并上传生成文件为 workflow artifact。

dry-run 仍然使用真实 Release assets。`dry_run=true` 只阻止外部副作用：不会 push 到 Scoop bucket，也不会创建 winget PR。它不会跳过 asset 下载、hash 计算、manifest 生成或真实 winget manifest 验证。

发布流水线顺序是：

```text
real release metadata
-> real asset download/hash
-> Scoop manifest generation
-> winget manifest generation
-> winget validate --ignore-warnings
-> artifact upload
-> optional Scoop bucket push
-> optional winget PR
```

## 2. Stable 与 Beta 渠道

Stable 和 Beta 是同一个应用身份的两个发布渠道，不支持并排安装。

不变的安装器身份：

- AppId: `45156332-3408-47B7-B5D2-2567E5888F64`
- Service name: `ContextMenuManagerPlusService`
- Install dir: `Context Menu Manager Plus`

Scoop 使用不同 app name：

- Stable: `contextmenumgrplus`
- Beta: `contextmenumgrplus-beta`

两个 Scoop manifest 都包含 `pre_install` 互斥检查：

- Stable 安装前检查 `contextmenumgrplus-beta`
- Beta 安装前检查 `contextmenumgrplus`

`persist` 设置为 `Data`。由于 Scoop app name 不同，Stable 和 Beta 的持久化目录不会共享。

winget 使用不同 PackageIdentifier：

- Stable: `PLFJY.ContextMenuMgrPlus`
- Beta: `PLFJY.ContextMenuMgrPlus.Beta`

winget 标识符不同是为了渠道区分；安装器 AppId 仍然相同，因此安装器层面仍会把 Stable 和 Beta 视为同一个应用身份，保持互斥。

## 3. 版本规则

Stable 包管理器版本使用 Release tag 去掉前导 `v` 后的版本：

```text
v1.7.2 -> 1.7.2
```

Beta 包管理器版本使用 Release 发布时间生成稳定可排序版本：

```text
release tag: v1.7.2-Beta+abcdef0
published_at: 2026-07-04T13:58:22Z
package version: 1.7.2-beta.20260704135822
```

asset URL 仍然指向真实 Release tag 和真实 asset filename。包管理器版本不需要等于 asset 文件名中的版本。

## 4. Scoop bucket

默认 bucket 仓库是：

```text
PLFJY/scoop-bucket
```

创建 bucket：

1. 在 GitHub 创建 `PLFJY/scoop-bucket` 仓库。
2. 在仓库中创建 `bucket/` 目录。
3. 不需要手工创建 manifest；workflow 会写入：
   - `bucket/contextmenumgrplus.json`
   - `bucket/contextmenumgrplus-beta.json`

用户安装 Beta：

```powershell
scoop bucket add plfjy https://github.com/PLFJY/scoop-bucket
scoop install plfjy/contextmenumgrplus-beta
```

Scoop manifest 使用 framework-dependent portable zip：

```text
ContextMenuMgrPlus-<assetVersion>-framework-dependent-portable.zip
```

manifest notes 会提示用户需要 .NET 10 Desktop Runtime，应用可能请求安装或修复 Windows Service。Beta manifest 还会提示 Beta 可能包含回归。

## 5. winget

winget 使用 framework-dependent Inno Setup installers：

- `ContextMenuMgrPlus-<assetVersion>-x64-framework-dependent-Setup.exe`
- `ContextMenuMgrPlus-<assetVersion>-x86-framework-dependent-Setup.exe`
- `ContextMenuMgrPlus-<assetVersion>-arm64-framework-dependent-Setup.exe`

workflow 生成 multi-file manifests：

- `<PackageIdentifier>.yaml`
- `<PackageIdentifier>.locale.zh-CN.yaml`
- `<PackageIdentifier>.locale.zh-TW.yaml`
- `<PackageIdentifier>.locale.en-US.yaml`
- `<PackageIdentifier>.installer.yaml`

version manifest 使用 `DefaultLocale: zh-CN`。`zh-CN` locale manifest 是 `ManifestType: defaultLocale`，`zh-TW` 和 `en-US` locale manifests 是 `ManifestType: locale`。Locale tag 使用 BCP 47 casing：`zh-CN`、`zh-TW`、`en-US`。

所有 locale 的 `PackageName` 都保持为 `Context Menu Manager Plus`，不翻译成中文。这样做是为了与 Inno Setup installer AppName、Windows “Add or Remove Programs” 项，以及 Stable / Beta 共用的安装器身份保持一致。Stable 和 Beta 在 winget 中使用不同 PackageIdentifier，但安装器 AppId、服务名和安装目录仍然是同一个应用身份。

提交路径遵循 winget-pkgs identifier split convention：

- Stable: `manifests/p/PLFJY/ContextMenuMgrPlus/<PackageVersion>/`
- Beta: `manifests/p/PLFJY/ContextMenuMgrPlus/Beta/<PackageVersion>/`

winget 可用性取决于 PR 合并到 `microsoft/winget-pkgs` 以及源索引刷新。Action 可以自动打开 PR，但不能保证用户立即通过 winget 搜到或安装。

`Scripts/PackageManagers/New-WingetManifest.ps1` 只负责生成 manifest 并做确定性的文件内容检查。`Scripts/PackageManagers/Test-WingetManifest.ps1` 负责调用 `winget validate --ignore-warnings`。发布 workflow 只对真实 Release assets 生成的 manifest 运行 winget CLI 验证。

验证脚本会保留 winget YAML schema header，例如：

```yaml
# Created with ContextMenuMgr package manager automation
# yaml-language-server: $schema=https://aka.ms/winget-manifest.version.1.12.0.schema.json

PackageIdentifier: ...
```

GitHub-hosted runner 上的 winget 版本可能在输出 `Manifest validation succeeded with warnings.` 时仍因 schema-header warning 返回非零退出码。发布 workflow 不应因为 warning-only validation 阻断包管理器 manifest 发布，因此验证脚本使用 `--ignore-warnings`。如果加入该参数后 `winget validate` 仍返回非零退出码，则视为真实 validation error，并让 workflow 失败。脚本同时会把 winget 版本和完整验证输出写入 `winget-validation-output.txt`，该文件会随 `if: always()` 上传的 package manager artifact 一起保留，便于后续排查。

## 6. Secrets and Variables

Repository variables:

| Name | Default | Purpose |
| --- | --- | --- |
| `SCOOP_BUCKET_REPOSITORY` | `PLFJY/scoop-bucket` | Scoop bucket repository full name. |
| `ENABLE_SCOOP_PUSH` | `true` | Set to `false` to skip real Scoop pushes. |
| `WINGET_FORK_REPOSITORY` | none | Fork of `microsoft/winget-pkgs`, for example `PLFJY/winget-pkgs`. |
| `ENABLE_WINGET_PR` | `false` | Set to `true` only after the fork and token are ready. |
| `PACKAGE_MANAGER_DRY_RUN` | `false` | Optional global dry-run switch for release-published runs. |

Repository secrets:

| Name | Purpose |
| --- | --- |
| `SCOOP_BUCKET_TOKEN` | Token with push access to `PLFJY/scoop-bucket`. Required when Scoop push is enabled. |
| `WINGET_PR_TOKEN` | Token with push access to the winget fork and permission to open PRs. Required when `ENABLE_WINGET_PR=true`. |

`GITHUB_TOKEN` is used only to read the published Release and download assets from `PLFJY/ContextMenuMgr`.

## 7. Dry-run

Manual dry-run:

1. Open Actions.
2. Run `Publish Package Managers`.
3. Enter an already published Release tag.
4. Keep `dry_run` enabled.
5. Download the `package-manager-manifests-*` artifact and inspect:
   - `release-metadata.json`
   - `release-assets.json`
   - `scoop/*.json`
   - `winget/*.yaml`

The dry-run artifact is generated from the real GitHub Release selected by tag. `release-assets.json` contains hashes computed from downloaded Release assets, not fixture values.

## 8. Fixture tests

Fixture tests are separate CI/local checks and are not part of the publishing workflow. They validate script behavior with offline sample JSON and fake URLs/hashes, so they must not run before a real release publish.

CI workflow:

```text
.github/workflows/package-manager-script-tests.yml
```

Local script validation:

```powershell
pwsh Scripts/PackageManagers/Test-PackageManagerScripts.ps1
```

This test is offline. It uses fixture JSON and fake asset hashes to validate Stable and Beta versioning, Scoop mutual exclusion, licenses, persisted data, winget identifiers, locale manifest generation, and installer architecture coverage. Fixture tests only check deterministic generation logic and do not run winget CLI validation because they use fake URLs and hashes.

## 9. First Beta validation checklist

1. Create `PLFJY/scoop-bucket`.
2. Add `SCOOP_BUCKET_TOKEN`.
3. Keep `ENABLE_WINGET_PR=false` for the first package-manager dry-run.
4. Publish a Beta GitHub Release from the existing draft release flow.
5. Confirm the workflow generates `contextmenumgrplus-beta.json`.
6. Confirm the generated URL points to the real framework-dependent portable zip.
7. Confirm the generated SHA256 matches the Release asset.
8. Confirm the Scoop bucket commit updates `bucket/contextmenumgrplus-beta.json`.
9. Install through Scoop:

```powershell
scoop bucket add plfjy https://github.com/PLFJY/scoop-bucket
scoop install plfjy/contextmenumgrplus-beta
```

10. Enable `ENABLE_WINGET_PR=true` only after `WINGET_FORK_REPOSITORY` and `WINGET_PR_TOKEN` are ready.
