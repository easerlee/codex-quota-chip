# 本项目约定

## 本地副本不要放回 `E:\dsh`

`E:\dsh` 是 DSH 的安装目录，它的自更新会**整目录替换**，2026-10-18 就这样把当时放在
`E:\dsh\CodexQuotaChip` 的工作副本连同 `.tmp` 一起清掉了（源码没丢，因为全都推到 GitHub 了）。
本地副本现在放在 `E:\CodexQuotaChip`。

## 不要从工作区启动它

DSH 给 agent 执行命令用的 shell，cwd **就是工作区**；`Start-Process` 会把这个 cwd 继承给子进程。
Windows 不允许改名一个被任何进程当作工作目录的目录 —— 所以只要本程序的 cwd 落在 `E:\dsh` 下，
DSH 的自更新就必定失败（2026-10-18 连挡了三次）。

正式位置：`D:\Program Files\CodexQuotaChip`。启动一律显式给工作目录：

```powershell
Start-Process -FilePath 'D:\Program Files\CodexQuotaChip\CodexQuotaChip.exe' `
              -WorkingDirectory 'D:\Program Files\CodexQuotaChip'
```

收工前自查（直接读各进程 cwd）：

```powershell
pwsh -File .\tools\cwd-holders.ps1 -Prefix 'E:\dsh'
```

除 `DeepSeek Harness.exe` 和当次 `pwsh` 外，不该有别的进程。

（更完整的说明在 `~/.dsh/AGENTS.md` 的「从工作区启动的进程会挡住 DSH 自更新」一节。）

## 构建流程里不要启动 GUI

改完一版，我该做的到此为止：编译、打包、`install.ps1 -Target 'D:\Program Files\CodexQuotaChip'`、
用 `--check` / `--once`（无窗口、即退）验证，然后**写在回复里**。

**不要**顺手 `Start-Process` 把界面启起来 —— 启动是"用"，由用户决定。他机器上这个程序要不要跑、
什么时候跑，不是构建流程的一部分。要看界面/截图/验证通知，先问。

## 本地装好就要发出去，不要等人问

改完一版、装回本机之后，紧接着把同一版发出去，不等用户催。顺序：

1. `npm/package.json`：`version` 改成新版本，`codexQuotaChip.sha256` 换成新 zip 的哈希（现算，不抄旧的）。
2. `CHANGELOG.md` 顶部加这一版的条目，正文与 Release 说明一致。
3. 提交 `Release X.Y.Z` → 打 tag `vX.Y.Z` → 推送。
4. `gh release create vX.Y.Z <zip> --notes-file …`，确认资产已上传。
5. 补跑 / 重跑 npm workflow，然后**核对到底**：Release 资产在、CI success、`npm view <包> version`
   指向这一版、npm 里钉的 sha256 与本地 zip 一致。

唯一例外：用户明确说"先别发"。发不顺（CI 失败、npm 404 之类）要当场查清并补跑到成功，不能停在
"我以为发了"——上一版 0.2.39 就是 tag 推送时 Release 还没有 zip，第一次 CI 必失败，得重跑。
npm 发布完有两三分钟的 registry 延迟，查不到不等于没发，直连 registry 再确认。

