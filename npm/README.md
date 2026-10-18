# codex-quota-chip (npm wrapper)

> 仓库首页有中英文两版说明：[简体中文](https://github.com/easerlee/codex-quota-chip#readme)
> ・ [English](https://github.com/easerlee/codex-quota-chip/blob/main/README.en.md)
> ・ [更新记录](https://github.com/easerlee/codex-quota-chip/blob/main/CHANGELOG.md)

这是 [Codex Quota Chip](https://github.com/easerlee/codex-quota-chip) 的 npm 外壳。
**它不是 JavaScript 库** —— 本体是 C#/WPF 的 Windows 桌面程序，所以这个包做的事只有三件：

1. 安装时从对应的 GitHub Release 下载自包含的 `CodexQuotaChip-<version>-win-x64.zip`；
2. 用 `package.json` 里钉死的 sha256 校验，校验不过就拒绝解压；
3. 解到包内 `vendor/`，然后 `codex-quota-chip` 这个命令把参数原样转给那个 exe。

```sh
npm install -g codex-quota-chip

codex-quota-chip                       # 启动任务栏浮窗
codex-quota-chip --check               # 打印状态目录 / codex 路径 / 停靠位置
codex-quota-chip --once                # 读一次并打印 JSON
```

因为只有 Windows 构建，`package.json` 里限制为 `os: win32`，在别的系统上 npm 直接拒绝安装。
想装真正的安装包/卸载脚本，用 GitHub Release 里的 zip，或者用 winget（见仓库首页）。

## 卸载

```sh
npm uninstall -g codex-quota-chip
```

程序自己的状态目录（`%LOCALAPPDATA%\CodexQuotaChip`）不会被 npm 删除；要一起清就用 Release
zip 里的 `uninstall.ps1 -Purge`，或手动删那个目录。

## 发布（维护者）

**版本号停在 `0.2.x`**：只递增修订号（0.2.1、0.2.2、…），次版本号先不要动（这是维护者的决定，
不是技术限制 —— 哪天要动，这条和下面的示例一起改）。

发布由 [`.github/workflows/publish.yml`](../.github/workflows/publish.yml) 在 CI 里完成，
用 **Trusted Publishing（OIDC）**，仓库和本机都不存 npm token：

1. **先把这次的代码改动提交干净**。产物里会写进打包那一刻的 `HEAD`（就是 `0.2.4+<sha>` 那串），
   先打包后提交的话，它指向的是上一个提交 —— 2026-10-18 的 0.2.4 就是这么来的：功能没错，
   但版本串对不上，事后查证要多花一道功夫。顺序是：改代码 → 提交 → 再打包。
2. `..\build-publish.ps1 -Version 0.2.1` 出新的 zip，记下它的 sha256
3. 把本目录 `package.json` 的 `version` 和 `codexQuotaChip.sha256` 一起改成 `0.2.1` / 新哈希，
   并同步 `..\src\CodexQuotaChip\CodexQuotaChip.csproj` 的 `<Version>`，提交
4. 建 GitHub Release `v0.2.1` 并上传 zip（先建 draft、传完资源再公开，免得 CI 抢跑）
5. Release 一公开，tag 就有了，workflow 自动跑：校验 tag 与 version 一致、校验 Release 上的
   zip 哈希与 `package.json` 一致，然后 `npm publish --provenance`

`version` 与 tag 不一致、或 zip 哈希对不上时，workflow **会拒绝发布** —— 因为安装脚本就是按
自己的 version 去 Release 拼下载地址的，发出去也装不上。

手动补发：Actions → publish → Run workflow，填一个已存在的 tag。
绑定关系只能由维护者在 npmjs.com 的包设置里维护（包页 → Settings → Trusted publishing）。
