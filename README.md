<div align="center">

# Codex Quota Chip

Windows 任务栏上的 Codex 额度小浮窗：一眼看到还剩多少，右键看每个账号。

[English](README.en.md) | **简体中文** ・ [更新记录](CHANGELOG.md)

![任务栏浮窗](docs/chip.png)

</div>

## 长什么样

常驻任务栏的一小块，三行：5 小时额度条、周额度条、以及「5H 重置时刻 / 周重置倒计时 + 状态灯」。
条的颜色随剩余量变（绿 / 黄 / 红），右下角那个小圆点是状态灯（正常 / 读取中或数据变旧 / 还没读到 / 读取失败）。

它默认铺一层深色圆角底图，和右键面板同一个颜色；关掉就直接贴在任务栏上。整个配色默认跟随任务栏的
「浅色 / 深色」设置，开着底图时固定用深色那套。

宽度不是写死的：按底行最长可能出现的文本量出来，需要多宽开多宽，剩下的都留给进度条。

鼠标操作：**左键刷新一次，右键打开面板，悬停显示提示**。全屏程序在前台时自动让位，退出后自己回来。

右键面板：

![右键面板](docs/panel.png)

每个用过的账号一块：名字（当前登录的那个标 `【当前】`）、还剩几次手动重置额度的票券、以及 5H / WK
两行数字。名字前面的圆点是**这一路数据还在不在刷新** —— 绿 = 还在刷新，灰 = 停了（比如账号已退出，或者
令牌失效那段时间）。每个账号的重置时刻和倒计时都按服务端给的时间渲染；窗口刚重置、或者重置时刻已过，
那一行显示 `已重置`，进度条画成满绿。

## 有什么用

- 一眼看到**当前登录账号**的 5 小时 / 周额度还剩多少、什么时候恢复。
- 常驻任务栏，不占窗口、不进 Alt+Tab、不抢焦点；单实例运行。
- 右键面板按账号列出这台机器上用过的**每个账号**最后读到的数值，标出当前登录的是哪一个。
- 账号还剩几次**手动重置额度**，用票券图标标出来（含每张的到期日）。
- **额度窗口重置时弹 Windows 通知**：进通知中心、遵守「勿扰 / 专注助手」，5 小时和周窗口各自判断，
  同一次重置不会重复提醒，不想要可以关掉。
- 浮窗可选**深色圆角底图**；配色跟随任务栏明暗，或者固定深色。
- 该有的开关都有：取数间隔、停靠位置（托盘左侧 / 任务栏最左端）、开机自动启动、重置通知、浮窗底图 ——
  都在右键面板里，改完立刻生效；也可以直接改 JSON 设置文件。
- **只读、不存凭据**：见下。

## 额度是怎么读到的

程序启动本机 Codex 自带的命令行：

```
codex.exe app-server --listen stdio://
```

然后走 JSON-RPC 调 `account/rateLimits/read`，按返回里的 `windowDurationMins` 区分窗口
（约 300 分钟 → 5H，约 10080 分钟 → WK），而不是假定 `primary`/`secondary` 谁是谁。
同一份返回里、与 `rateLimits` 平级的 `rateLimitResetCredits` 就是那个账号还剩几次重置额度，
面板上的票券图标来自它。每次读取起一个进程、问完就结束进程（一次读取约 1.5 秒）。

- **只读**：不写、不改 Codex 的任何文件；`auth.json` 只读，用来知道当前登录的是哪个账号。
- **不存凭据**：账号记录里只有账号 ID / 邮箱 / 百分比 / 时间，没有任何 token。
- 应用自己的状态文件在 `%LOCALAPPDATA%\CodexQuotaChip\`。

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

## 设置文件

`%LOCALAPPDATA%\CodexQuotaChip\settings.json`，各项含义和默认值见仓库里的
[`settings.example.json`](settings.example.json)；用环境变量 `CODEX_QUOTA_CHIP_HOME` 或
命令行 `--home` 可以把状态目录换到别处。面板上改的设置也写在这里。

## 命令行

```
CodexQuotaChip.exe --check      # 打印状态目录、codex 路径、将要停靠的位置（含真实宽度），不起界面
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
pwsh -File .\build-publish.ps1 -Version 0.2.12               # 出 publish\ 下的 zip
```

图标不是丢一个二进制在仓库里就算完：`tools/make-icon.ps1` 用深色圆角方块加两条额度条画出来
（含 16/24/32/48/64/128/256 七个尺寸），要改样式就改脚本再跑一遍。

自检覆盖的都是有规则可讲的部分：解析与窗口分类、读数格式化、账号排序、浮窗停靠、
额度条几何、浮窗宽度测量、重置条满绿、链路状态灯的新鲜度、点外关闭规则、设置文件的容错、
codex.exe 路径失效后重解析、账号记录文件。

## 已知边界

- `codex app-server` 不是公开承诺稳定的接口，桌面版 Codex 更新后方法名/字段可能变；
  程序读不到时会在日志里留下原因（`%LOCALAPPDATA%\CodexQuotaChip\logs\app.log`），
  浮窗保留上一次有效读数并把状态灯转黄/红。
- 程序只读**当前登录账号**的额度。已退出账号的数值停在它最后一次被读到的时候，圆点变灰表示
  这一路已经不再刷新；它的重置倒计时是按记录里的重置时间在本地渲染的，时间一过就显示 `已重置` ——
  那是本地推断，不是又读了一次服务端。想让它持续刷新，得把每个账号的凭据都存下来 ——
  本程序刻意不这么干。
- 读数只适合日常参考，不是计费依据。

## 许可证

MIT，见 `LICENSE`。本项目不是 OpenAI 官方项目，与 OpenAI 无关联。
