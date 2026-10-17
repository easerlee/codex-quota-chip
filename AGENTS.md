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
