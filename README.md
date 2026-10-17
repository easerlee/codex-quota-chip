<div align="center">

# Codex Quota Chip

Windows 任务栏上的 Codex 额度小浮窗：一眼看到还剩多少，右键看每个账号。

</div>

一个常驻 Windows 任务栏的自用小工具。它贴在通知区域左边（或任务栏最左端，见下），
显示当前登录账号的 **5 小时窗口**和**周窗口**还剩多少、什么时候恢复，右键弹出一个面板
列出这台机器上用过的每个账号最后读到的数值。

这是**独立实现**：取数用自己的 JSON-RPC 客户端调本机 Codex 的命令行接口，界面自己画，
不依赖任何第三方同类项目。

## 它长什么样

```
5H  ▬▬▬▬▬▬▬▬▬▬             ← 5 小时额度条，颜色随阈值变（绿/黄/红）
WK  ▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬       ← 周额度条
09:16 / 6d 18h        ●   ← 5H 的重置时刻 / 周的重置倒计时 + 状态灯
```

- 宽度固定 116px，高度直接用任务栏高度，三行等分，字色跟随任务栏「浅色/深色」设置。
- 状态灯（右下角小方块）：绿 = 正常，黄 = 正在读取或数据变旧，灰 = 还没读到，红 = 读取失败。
- **左键点击**：立刻刷新一次。**右键点击**：打开面板。
- 全屏程序在前台时自动隐藏，退出后自己回来。
- 全屏程序在前台时自动隐藏，退出后自己回来。
- **额度窗口重置时弹一条 Windows 通知**（进通知中心，5 小时和周窗口各自判断）；不想要就在面板里关掉。
- 单实例运行；不会出现在任务栏应用列表和 Alt+Tab 里。

右键面板：

```
● easerlee              ← 绿点 = 当前登录的账号，永远排最上面
  5H   20%   09:16
  WK   72%   6d 18h
────────────────────
刷新间隔        [1][5][15]
刷新（或点状态栏数据）
────────────────────
显示位置           [左][右]
开机自动启动         ●──
额度重置时通知       ●──
────────────────────
退出
```

- 账号按「当前登录的 → 谁先恢复可用」排序；已经重置或还没开始计时的窗口当作「现在就能用」，
  排在还在跑的窗口前面；没有重置时间的记录排最后。
- `已重置` 表示这个窗口满额但还没开始计时，或者重置时刻已经过了 —— 这时服务端给的百分比和
  重置时间都不可信（5 小时窗口是从**第一次使用**开始算的），所以不显示旧数字。
- **账号名右侧的金色票券**就是这个账号还剩几次**手动重置额度**（服务端给的 `availableCount`），
  右对齐着账号信息的右边缘；有多张就往左排，鼠标停在上面会显示最近一张的到期日。已过期或已被
  服务端扣掉的票券不会画出来。
- 刷新间隔、显示位置、开机自启、重置通知都是滑动控件：底槽用额度条的同一种底槽色，选中的一块/滑钮用
  额度条的绿色；改这几项**不会关面板**，因为滑过去本身就是反馈。
- **显示位置**：`右`（默认）贴在通知区域左边；`左` 贴到任务栏最左端 —— Windows 11 任务栏
  居中时那一端是空的，用 `左` 更合适；任务栏左对齐就用 `右`，免得压住开始按钮。
- 点面板外面任意位置关闭（别的程序、任务栏、桌面都算）。

## 额度是怎么读到的

程序启动本机 Codex 自带的命令行：

```
codex.exe app-server --listen stdio://
```

然后走 JSON-RPC 调 `account/rateLimits/read`，按返回里的 `windowDurationMins` 区分窗口
（约 300 分钟 → 5H，约 10080 分钟 → WK），而不是假定 `primary`/`secondary` 谁是谁。
同一份返回里、与 `rateLimits` 平级的 `rateLimitResetCredits` 就是那个账号还剩几次重置额度
（`availableCount` 与每张票券的 `status`/`expiresAt`），面板上的票券图标来自它。
每次读取起一个进程、问完就结束进程。

- **只读**：不写、不改 Codex 的任何文件；`auth.json` 只读，用来知道当前登录的是哪个账号。
- **不存凭据**：账号记录里只有账号 ID / 邮箱 / 百分比 / 时间，没有任何 token。
- 应用自己的状态文件在 `%LOCALAPPDATA%\CodexQuotaChip\`（可用环境变量
  `CODEX_QUOTA_CHIP_HOME` 或 `--home` 换位置）。

## 安装

绿色包，不需要管理员权限：

```powershell
# 解压 zip 后，在同目录运行
pwsh -File .\install.ps1            # 装到 %LOCALAPPDATA%\Programs\CodexQuotaChip
pwsh -File .\install.ps1 -Autostart # 顺便写开机自启
```

也可以直接双击 zip 里的 `CodexQuotaChip.exe` 运行（自包含，不需要预装 .NET）。
开机自启也可以在面板里开关；`uninstall.ps1` 会去掉自启项和安装目录（加 `-Purge` 连状态文件一起删）。

用 npm 装也行 —— 那个包只是个外壳：安装时去对应版本的 Release 下载 zip，校验 sha256 通过后才解压：

```sh
npm install -g codex-quota-chip
codex-quota-chip --check
```

需要：Windows 10/11 x64；已安装并登录 Windows 桌面版 Codex（程序会从
`%LOCALAPPDATA%\OpenAI\Codex\bin\*\codex.exe` 或 `PATH` 里找 `codex.exe`）。

## 设置

`%LOCALAPPDATA%\CodexQuotaChip\settings.json`（见 `settings.example.json`）：

| 键 | 默认 | 说明 |
|---|---|---|
| `refresh_seconds` | 60 | 取数间隔，最小 30 |
| `display_side` | `right` | `right` = 托盘左侧，`left` = 任务栏最左端 |
| `warn_below` | 30 | 剩余低于这个百分比转黄 |
| `critical_below` | 15 | 剩余低于这个百分比转红 |
| `notify_on_reset` | true | 额度窗口重置时弹通知 |

### 重置通知是怎么判断的

不靠本地时钟，也不额外记状态：窗口重置时服务端会把 `resetsAt` **往后推将近一整个窗口**
（5 小时窗口约 5 小时、周窗口约 7 天），所以「重置时间往后跳了至少 70% 的窗口长度」才算是翻篇。
这样重启、休眠、程序没开都算得准，也不会对同一次重置重复提醒。上次记录里**没有**重置时间的窗口
属于「刚开始用」，不算重置，不提醒。

**为什么阈值必须这么高**：服务端会随使用把周窗口的末尾一点点往后滑（几分钟量级）。0.2.2 一开始
用的是「只要往后移就算重置」，结果每读一次都成立 —— 每两三分钟弹一条假通知。0.2.3 改成按窗口
长度判定，漂移再怎么滑也不够 70%。

通知走系统 toast（会进通知中心，并遵守「勿扰 / 专注助手」），程序首次运行时会给自己登记一个
`HKCU\Software\Classes\AppUserModelId\easerlee.CodexQuotaChip`（不需要开始菜单快捷方式、不需要
安装包）。万一系统没有 toast 接口，退回通知区域气泡。

## 命令行

```
CodexQuotaChip.exe --check      # 打印状态目录、codex 路径、将要停靠的位置，不起界面
CodexQuotaChip.exe --once       # 读一次并打印 JSON，不起界面
CodexQuotaChip.exe --notify-test # 立刻弹一条测试通知（不用等真的重置）
  --codex-home <path>           # 换 Codex home（默认 %CODEX_HOME% 或 ~/.codex）
  --codex-exe <path>            # 显式指定 codex.exe
  --refresh-seconds <n>         # 覆盖取数间隔
  --home <path>                 # 换状态目录
```

## 从源码构建

需要 .NET 8 SDK：

```powershell
dotnet build .\src\CodexQuotaChip\CodexQuotaChip.csproj -c Release
dotnet run   --project .\tests\CodexQuotaChip.Tests          # 自检
pwsh -File .\tools\make-icon.ps1                             # 重新生成 app.ico
pwsh -File .\build-publish.ps1                               # 出 publish\ 下的 zip
```

图标不是丢一个二进制在仓库里就算完：`tools/make-icon.ps1` 用深色圆角方块加两条额度条画出来
（含 16/24/32/48/64/128/256 七个尺寸），要改样式就改脚本再跑一遍。

自检覆盖的都是有规则可讲的部分：解析与窗口分类、读数格式化、账号排序、浮窗停靠、
额度条几何、点外关闭规则、设置文件的容错、codex.exe 路径失效后重解析、账号记录文件。

## 已知边界

- `codex app-server` 不是公开承诺稳定的接口，桌面版 Codex 更新后方法名/字段可能变；
  程序读不到时会在日志里留下原因（`%LOCALAPPDATA%\CodexQuotaChip\logs\app.log`），
  浮窗保留上一次有效读数并把状态灯转黄/红。
- 已退出账号的数值停在它最后一次被读到的时候（那之后它没在用，数值本来也不变）；
  到重置时间后会显示 `已重置`。想让它持续刷新，得把每个账号的凭据都存下来 —— 本程序
  刻意不这么干。
- 读数只适合日常参考，不是计费依据。

## 许可证

MIT，见 `LICENSE`。本项目不是 OpenAI 官方项目，与 OpenAI 无关联。
