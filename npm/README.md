# codex-quota-chip (npm wrapper)

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

`package.json` 的 `version` 必须和 GitHub Release 的 tag（`v<version>`）一致，`sha256`
必须是该 Release 上 zip 的哈希，否则安装会被自己的校验拦下：

```powershell
npm pack                       # 本地打 tarball，可先 --prefix 装到临时目录验证
npm login
npm publish
```
