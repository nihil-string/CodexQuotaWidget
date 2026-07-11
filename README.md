# Codex Quota Widget

一个面向 Windows 10/11 的 Codex 实时额度悬浮窗。按照 Codex 官方界面的口径，显示 5 小时和 7 天窗口的剩余比例与重置倒计时，每 60 秒在线刷新。

完整威胁模型和审计结果见 [SECURITY.md](SECURITY.md)。开源项目致谢见 [ACKNOWLEDGEMENTS.md](ACKNOWLEDGEMENTS.md)。

## 安全边界

- 只从 `%CODEX_HOME%\auth.json` 或 `%USERPROFILE%\.codex\auth.json` 读取当前 access token，并且只保存在请求生命周期的内存中。
- 唯一允许的远程地址是固定编译进程序的 `https://chatgpt.com/backend-api/wham/usage`。
- HTTP 客户端禁用 Cookie 和自动重定向，避免凭据被带往其他域名。
- 不复制、刷新或修改 `auth.json`，不读取其中的 refresh token。
- 在线查询失败时，才只读 `%CODEX_HOME%\sessions` 或 `%USERPROFILE%\.codex\sessions` 下的 `*.jsonl` 作为本地降级显示。
- 不读取浏览器 Cookie，不接受 API Key，不加载远程页面。
- 不修改 Codex 配置，不安装 hook，不申请管理员权限。
- 只在 `%LOCALAPPDATA%\CodexQuotaWidget\settings.json` 保存窗口位置、透明度、置顶、位置锁定和穿透设置。

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

使用当前 Codex 登录做一次实时联通验证（输出中不包含 Token）：

```powershell
dotnet run --project .\tests\CodexQuotaWidget.LiveProbe\CodexQuotaWidget.LiveProbe.csproj -c Release
```

构建自包含便携包：

```powershell
.\scripts\build-release.ps1
```

右键悬浮窗可控制刷新、锁定位置、鼠标穿透、置顶、透明度、隐藏和退出。未锁定时可拖动窗口。开启鼠标穿透后请从托盘菜单关闭穿透；托盘菜单保留完整的恢复入口。

## 设计取舍

Windows Store 版 Codex 的 `app-server` 不能由普通外部进程稳定启动，因此实时刷新采用和主流开源额度工具一致的只读 usage 请求。与现有工具不同，本项目固定请求域名、禁用重定向、不刷新 token、不回写认证文件，并保留本地 session 日志作为断网降级。
