# Codex Quota Widget

一个面向 Windows 10/11 的 Codex 底栏额度条。它只读定位 Codex 输入框底部的“完全访问”和模型按钮，把账号当前实际提供的 5 小时和/或每周额度放在两者正中：单额度宽 72px，双额度宽 148px，高度均为 28px。界面只保留剩余比例，精确到分钟的重置倒计时放在悬停提示中，每 60 秒在线刷新。

本项目是独立开发的非官方社区工具，与 OpenAI 没有隶属、赞助或背书关系。

完整威胁模型和审计结果见 [SECURITY.md](SECURITY.md)。开源项目致谢见 [ACKNOWLEDGEMENTS.md](ACKNOWLEDGEMENTS.md)。

## 安全边界

- 只从 `%CODEX_HOME%\auth.json` 或 `%USERPROFILE%\.codex\auth.json` 读取当前 access token，并且只保存在请求生命周期的内存中。
- 唯一允许的远程地址是固定编译进程序的 `https://chatgpt.com/backend-api/wham/usage`。
- HTTP 客户端禁用 Cookie 和自动重定向，避免凭据被带往其他域名。
- 不复制、刷新或修改 `auth.json`，不读取其中的 refresh token。
- 在线查询失败时，才只读 `%CODEX_HOME%\sessions` 或 `%USERPROFILE%\.codex\sessions` 下的 `*.jsonl` 作为本地降级显示；缺少有效事件时间的记录会被拒绝，已经重置的旧周期不会继续显示。
- 不读取浏览器 Cookie，不接受 API Key，不加载远程页面。
- 不修改 Codex 配置，不安装 hook，不开启 CDP，不注入或改写 Codex 进程和 Windows Store 安装目录，不申请管理员权限。
- 只在 Codex 主窗口位于前台时，通过后台、单飞的 Windows UI Automation 探测读取底栏按钮名称、样式类和屏幕边界；额度条是独立无边框窗口，不与 Codex 建立跨进程 owner/parent 关系，不会点击、输入或发送窗口消息。
- 只在 `%LOCALAPPDATA%\CodexQuotaWidget\settings.json` 保存透明度、穿透、跟随 Codex 和向后兼容的旧版显示设置。
- 设置文件损坏或无法读取时，本次运行使用安全默认值，但不会同步启动项或在退出时自动覆盖原文件；用户明确修改菜单设置后才会写入新的有效配置。
- “跟随 Codex”只在当前用户的 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 写入本程序路径和 `--background` 参数；关闭该选项会删除这一项。

额度接口属于 Codex/ChatGPT 当前使用的内部产品接口，并非稳定的公开 API；若 OpenAI 调整结构，应用会停止解析并显示明确错误，不会改走未知域名或静默降级到第三方服务。

## 使用

开发运行：

```powershell
dotnet run --project .\src\CodexQuotaWidget\CodexQuotaWidget.csproj
```

测试：

```powershell
dotnet test .\tests\CodexQuotaWidget.Tests\CodexQuotaWidget.Tests.csproj -c Release
```

项目目标框架为 `net8.0-windows`，持续集成固定在 Windows runner 和 .NET 8 SDK 上执行格式检查、Release 构建、单元测试，并只编译不运行需要真实账号的 LiveProbe。

生成不访问真实账号、网络、托盘或注册表的确定性视觉预览：

```powershell
dotnet run --project .\tests\CodexQuotaWidget.VisualPreview\CodexQuotaWidget.VisualPreview.csproj -- preview-weekly.png --weekly-only
dotnet run --project .\tests\CodexQuotaWidget.VisualPreview\CodexQuotaWidget.VisualPreview.csproj -- preview-both.png
```

使用当前 Codex 登录做一次实时联通验证（输出中不包含 Token）：

```powershell
dotnet run --project .\tests\CodexQuotaWidget.LiveProbe\CodexQuotaWidget.LiveProbe.csproj -c Release
```

构建自包含便携包：

```powershell
.\scripts\build-release.ps1
```

右键额度条会打开深色圆角菜单，可控制刷新、跟随 Codex、鼠标穿透、透明度、隐藏和退出。开启鼠标穿透后请从托盘菜单关闭穿透；托盘菜单保留完整的恢复入口。

“跟随 Codex”默认开启：程序以当前用户启动项进入轻量托盘后台，每 2 秒检查一次官方 `OpenAI.Codex_*` 包的 `ChatGPT.exe` 主进程。UI Automation 最多每 5 秒在同一 EXE 的无窗口隔离子进程中执行一次，使用单次批量属性缓存读取底栏锚点；同一时间只允许一个探测，1 秒超时即终止该子进程，确认调用结束后才允许重试。已有成功锚点时，短暂空结果或超时最多复用 15 秒，并且每次仍须通过 Codex 窗口可见、未最小化、尺寸未变和同进程前台校验；超过期限即隐藏。两次探测之间以 300ms 的纯 Win32 坐标投影跟随窗口平移，并读取原生 Z 序；窗口尺寸变化时先隐藏再重新探测，小组件落到 Codex 后方时才恢复到 Codex 正上方，坐标和 Z 序均未变化时不调用 `SetWindowPos`。额度条只在 Codex 或自身菜单位于前台时显示。Codex 关闭时停止额度刷新和 session 文件监控。找不到两个锚点或可用空间不足时不会退回桌面悬浮窗；短时间内重复的 session 文件事件会合并为一次刷新。

## 设计取舍

Windows Store 版 Codex 的 `app-server` 不能由普通外部进程稳定启动，因此实时刷新采用和主流开源额度工具一致的只读 usage 请求。与现有工具不同，本项目固定请求域名、禁用重定向、不刷新 token、不回写认证文件，并保留本地 session 日志作为断网降级。

Codex 当前公开的插件、Skill、MCP、Apps SDK 和 Hook 扩展的是工具、工作流、会话内 UI 或 agent 生命周期；没有公开的桌面壳层控件注入接口。Cockpit Tools 的客户端额度显示采用 loopback CDP 和 renderer 脚本注入，但这要求用调试参数启动 Codex，并扩大本机同用户进程可访问的调试面。本项目只借鉴“跟随 composer 几何位置”的产品思路，独立实现为不建立跨进程 owner 的外部 tool window：不开放调试端口，不复制其源码表达，也不影响 Codex 的 React/DOM 树。UIA 探测位于可超时终止的同程序隔离子进程中，并通过单飞、失败退避和有界几何缓存限制可访问性提供程序异常的影响范围。
