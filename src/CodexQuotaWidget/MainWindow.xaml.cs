using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CodexQuotaWidget.Controls;
using CodexQuotaWidget.Models;
using CodexQuotaWidget.Services;
using Forms = System.Windows.Forms;

namespace CodexQuotaWidget;

public partial class MainWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x20;
    private const long WsExToolWindow = 0x80;
    private const long WsExAppWindow = 0x40000;
    private const long WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpShowWindow = 0x0040;
    private static readonly TimeSpan ComposerProbeInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ComposerProbeFailureInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ComposerProbeTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ComposerCachedTargetGrace = TimeSpan.FromSeconds(15);
    private static readonly System.Drawing.Color TrayMenuForeground = System.Drawing.Color.FromArgb(242, 241, 236);

    private readonly SessionRateLimitReader _reader = new();
    private readonly CodexUsageClient _usageClient = new();
    private readonly QuotaRefreshCoordinator _refreshCoordinator;
    private readonly WidgetSettingsStore _settingsStore = new();
    private readonly CodexProcessMonitor _codexProcessMonitor = new();
    private readonly ComposerProbeController _composerProbeController;
    private readonly StartupRegistration _startupRegistration = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _countdownTimer;
    private readonly DispatcherTimer _lifecycleTimer;
    private readonly DispatcherTimer _placementTimer;
    private readonly DispatcherTimer _sessionChangeTimer;
    private readonly WidgetSettings _settings;
    private readonly Forms.NotifyIcon? _trayIcon;
    private readonly bool _enableSystemIntegration;
    private readonly RateLimitSnapshot? _previewSnapshot;
    private Forms.ToolStripMenuItem? _trayClickThroughItem;
    private Forms.ToolStripMenuItem? _trayFollowCodexItem;
    private System.Windows.Controls.MenuItem? _windowClickThroughItem;
    private System.Windows.Controls.MenuItem? _windowFollowCodexItem;
    private System.Windows.Controls.MenuItem? _windowOpacityItem;
    private FileSystemWatcher? _watcher;
    private RateLimitSnapshot? _snapshot;
    private bool _settingsWriteEnabled;
    private bool _settingsLoadFailed;
    private bool _quotaUpdatesActive;
    private bool _lastCodexRunning;
    private bool _userHidden;
    private bool? _lastLightBackground;
    private (int X, int Y, int Width, int Height)? _lastNativePlacement;
    private IntPtr _lastPlacedCodexHandle;
    private bool _isExiting;

    public MainWindow(bool backgroundStart = false, bool enableSystemIntegration = true)
        : this(backgroundStart, enableSystemIntegration, previewSnapshot: null)
    {
    }

    internal MainWindow(RateLimitSnapshot previewSnapshot)
        : this(backgroundStart: false, enableSystemIntegration: false, previewSnapshot: previewSnapshot)
    {
    }

    private MainWindow(
        bool backgroundStart,
        bool enableSystemIntegration,
        RateLimitSnapshot? previewSnapshot)
    {
        InitializeComponent();
        _refreshCoordinator = new QuotaRefreshCoordinator(_usageClient.FetchAsync, _reader.ReadLatestAsync);
        _composerProbeController = new ComposerProbeController(
            StartComposerProbeAsync,
            ComposerProbeInterval,
            ComposerProbeFailureInterval,
            ComposerProbeTimeout,
            ComposerCachedTargetGrace);
        _enableSystemIntegration = enableSystemIntegration;
        _previewSnapshot = previewSnapshot;
        var settingsLoad = previewSnapshot is null
            ? _settingsStore.LoadWithStatus()
            : new WidgetSettingsLoadResult(new WidgetSettings(), WidgetSettingsLoadStatus.Missing);
        _settings = settingsLoad.Settings;
        _settingsLoadFailed = settingsLoad.Status == WidgetSettingsLoadStatus.Invalid;
        _settingsWriteEnabled = !_settingsLoadFailed;
        if (_settingsLoadFailed)
        {
            StatusText.ToolTip = "设置文件无法读取；本次未同步启动项，也不会自动覆盖原文件。";
        }
        Topmost = false;
        Opacity = previewSnapshot is null ? 0 : Math.Clamp(_settings.Opacity, 0.6, 1.0);
        ShowActivated = false;
        ShellBorder.ContextMenu = CreateWindowContextMenu();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) => UpdateResetTimes();
        _lifecycleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _lifecycleTimer.Tick += (_, _) => MonitorCodexState();
        _placementTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _placementTimer.Tick += (_, _) => UpdateComposerPlacement();
        _sessionChangeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _sessionChangeTimer.Tick += async (_, _) =>
        {
            _sessionChangeTimer.Stop();
            await RefreshAsync();
        };

        _trayIcon = previewSnapshot is null ? CreateTrayIcon() : null;
        SyncMenuChecks();
        Loaded += MainWindow_Loaded;
        SourceInitialized += (_, _) =>
        {
            ApplyToolWindowStyle();
            ApplyClickThrough();
        };
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_previewSnapshot is not null)
        {
            Opacity = _settings.Opacity;
            StatusText.Text = "预览";
            _snapshot = _previewSnapshot;
            UpdateQuotaLayout(_previewSnapshot);
            UpdateResetTimes();
            return;
        }

        ConfigureWatcher();
        if (_enableSystemIntegration && !_settingsLoadFailed)
        {
            TryApplyStartupRegistration(_settings.FollowCodex);
        }
        _lastCodexRunning = _codexProcessMonitor.IsDesktopAppRunning();
        Opacity = 0;

        _lifecycleTimer.Start();
        _placementTimer.Start();

        if (!_lastCodexRunning)
        {
            Hide();
            return;
        }

        StartQuotaUpdates();
        UpdateComposerPlacement();
        await RefreshAsync();
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip
        {
            BackColor = System.Drawing.Color.FromArgb(26, 32, 38),
            ForeColor = TrayMenuForeground,
            Font = new Font("Microsoft YaHei UI", 9F),
            Padding = new Forms.Padding(5),
            ShowImageMargin = false,
            ShowCheckMargin = true,
            Renderer = TrayMenuAppearance.CreateRenderer()
        };
        menu.Opening += (_, _) => TrayMenuAppearance.ApplyRoundedRegion(menu, 10);
        menu.Items.Add("显示 / 隐藏", null, (_, _) => Dispatcher.Invoke(ToggleVisibility));
        menu.Items.Add("立即刷新", null, (_, _) => Dispatcher.Invoke(async () => await RefreshAsync()));
        menu.Items.Add(new Forms.ToolStripSeparator());

        _trayFollowCodexItem = new Forms.ToolStripMenuItem("跟随 Codex 开启 / 关闭")
        {
            Checked = _settings.FollowCodex
        };
        _trayFollowCodexItem.Click += (_, _) => Dispatcher.Invoke(ToggleFollowCodex);
        menu.Items.Add(_trayFollowCodexItem);

        _trayClickThroughItem = new Forms.ToolStripMenuItem("鼠标穿透") { Checked = _settings.ClickThrough };
        _trayClickThroughItem.Click += (_, _) => Dispatcher.Invoke(ToggleClickThrough);
        menu.Items.Add(_trayClickThroughItem);

        var opacityMenu = new Forms.ToolStripMenuItem("透明度");
        AddOpacityItem(opacityMenu, "70%", 0.70);
        AddOpacityItem(opacityMenu, "85%", 0.85);
        AddOpacityItem(opacityMenu, "96%", 0.96);
        AddOpacityItem(opacityMenu, "100%", 1.00);
        menu.Items.Add(opacityMenu);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        var icon = new Forms.NotifyIcon
        {
            Text = "Codex Quota Widget · 本地只读",
            Icon = CreateTrayGlyph(),
            Visible = true,
            ContextMenuStrip = menu
        };
        icon.DoubleClick += (_, _) => Dispatcher.Invoke(ToggleVisibility);
        return icon;
    }

    private System.Windows.Controls.ContextMenu CreateWindowContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu
        {
            Style = (Style)FindResource(typeof(System.Windows.Controls.ContextMenu))
        };

        var refreshItem = new System.Windows.Controls.MenuItem { Header = "立即刷新" };
        refreshItem.Click += async (_, _) => await RefreshAsync();
        menu.Items.Add(refreshItem);
        menu.Items.Add(new System.Windows.Controls.Separator());

        _windowFollowCodexItem = new System.Windows.Controls.MenuItem
        {
            Header = "跟随 Codex 开启 / 关闭",
            IsCheckable = true
        };
        _windowFollowCodexItem.Click += (_, _) => ToggleFollowCodex();
        menu.Items.Add(_windowFollowCodexItem);

        _windowClickThroughItem = new System.Windows.Controls.MenuItem
        {
            Header = "鼠标穿透",
            IsCheckable = true
        };
        _windowClickThroughItem.Click += (_, _) => ToggleClickThrough();
        menu.Items.Add(_windowClickThroughItem);

        _windowOpacityItem = new System.Windows.Controls.MenuItem();
        _windowOpacityItem.Click += (_, _) => CycleOpacity();
        menu.Items.Add(_windowOpacityItem);
        menu.Items.Add(new System.Windows.Controls.Separator());

        var hideItem = new System.Windows.Controls.MenuItem { Header = "隐藏到托盘" };
        hideItem.Click += (_, _) => HideToTray();
        menu.Items.Add(hideItem);

        var exitItem = new System.Windows.Controls.MenuItem { Header = "退出" };
        exitItem.Click += (_, _) => ExitApplication();
        menu.Items.Add(exitItem);

        menu.Opened += (_, _) => SyncMenuChecks();
        return menu;
    }

    private void AddOpacityItem(Forms.ToolStripMenuItem parent, string label, double opacity)
    {
        var item = new Forms.ToolStripMenuItem(label)
        {
            ForeColor = TrayMenuForeground
        };
        item.Click += (_, _) => Dispatcher.Invoke(() => SetOpacity(opacity));
        parent.DropDownItems.Add(item);
    }

    private void CycleOpacity()
    {
        var next = Opacity < 0.78 ? 0.85 : Opacity < 0.91 ? 0.96 : Opacity < 0.98 ? 1.0 : 0.70;
        SetOpacity(next);
    }

    private void SetOpacity(double opacity)
    {
        EnableSettingsWrite();
        _settings.Opacity = opacity;
        Opacity = opacity;
        SyncMenuChecks();
        SaveSettings();
    }

    private void ToggleFollowCodex()
    {
        var previousValue = _settings.FollowCodex;
        var nextValue = !previousValue;
        if (_enableSystemIntegration && !TryApplyStartupRegistration(nextValue))
        {
            SyncMenuChecks();
            return;
        }

        EnableSettingsWrite();
        _settings.FollowCodex = nextValue;
        SyncMenuChecks();
        if (!SaveSettings())
        {
            _settings.FollowCodex = previousValue;
            if (_enableSystemIntegration && !TryApplyStartupRegistration(previousValue))
            {
                _settings.FollowCodex = nextValue;
            }
            SyncMenuChecks();
            return;
        }

        if (_settings.FollowCodex)
        {
            _lastCodexRunning = _codexProcessMonitor.IsDesktopAppRunning();
            if (_lastCodexRunning)
            {
                ActivateForCodex();
            }
            else
            {
                DeactivateForCodex();
            }
        }
        else
        {
            StartQuotaUpdates();
        }
    }

    private void MonitorCodexState()
    {
        var isRunning = _codexProcessMonitor.IsDesktopAppRunning();
        if (isRunning == _lastCodexRunning)
        {
            return;
        }

        _lastCodexRunning = isRunning;
        if (isRunning)
        {
            ActivateForCodex();
        }
        else
        {
            DeactivateForCodex();
        }
    }

    private void ActivateForCodex()
    {
        StartQuotaUpdates();
        UpdateComposerPlacement();
        _ = RefreshAsync();
    }

    private void DeactivateForCodex()
    {
        StopQuotaUpdates();
        _composerProbeController.Reset(DateTimeOffset.UtcNow);
        _lastNativePlacement = null;
        _lastPlacedCodexHandle = IntPtr.Zero;
        Hide();
    }

    private void StartQuotaUpdates()
    {
        if (_isExiting)
        {
            return;
        }

        _quotaUpdatesActive = true;
        ConfigureWatcher();
        _refreshTimer.Start();
        _countdownTimer.Start();
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = true;
        }
    }

    private void StopQuotaUpdates()
    {
        _quotaUpdatesActive = false;
        _refreshTimer.Stop();
        _countdownTimer.Stop();
        _sessionChangeTimer.Stop();
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
        }

        _refreshCoordinator.Cancel();
    }

    private void ShowWithoutActivation()
    {
        if (IsVisible)
        {
            return;
        }

        ShowActivated = false;
        Show();
    }

    private bool TryApplyStartupRegistration(bool enabled)
    {
        try
        {
            _startupRegistration.SetEnabled(enabled);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException)
        {
            StatusText.Text = "跟随设置失败";
            StatusText.ToolTip = "无法更新当前用户启动项；跟随设置未提交。";
            StatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 180, 90));
            return false;
        }
    }

    private void ToggleClickThrough()
    {
        EnableSettingsWrite();
        _settings.ClickThrough = !_settings.ClickThrough;
        ApplyClickThrough();
        SyncMenuChecks();
        SaveSettings();
    }

    private void SyncMenuChecks()
    {
        if (_trayFollowCodexItem is not null) _trayFollowCodexItem.Checked = _settings.FollowCodex;
        if (_trayClickThroughItem is not null) _trayClickThroughItem.Checked = _settings.ClickThrough;
        if (_windowFollowCodexItem is not null) _windowFollowCodexItem.IsChecked = _settings.FollowCodex;
        if (_windowClickThroughItem is not null) _windowClickThroughItem.IsChecked = _settings.ClickThrough;
        if (_windowOpacityItem is not null) _windowOpacityItem.Header = $"透明度  {Opacity:P0}";
    }

    private static Icon CreateTrayGlyph()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(System.Drawing.Color.Transparent);
        using var outerPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(231, 229, 222), 3.2f);
        using var accentPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(118, 217, 192), 3.2f);
        graphics.DrawArc(outerPen, 5, 5, 22, 22, -75, 245);
        graphics.DrawArc(accentPen, 9, 9, 14, 14, 110, 125);
        var iconHandle = bitmap.GetHicon();
        try
        {
            using var borrowedIcon = System.Drawing.Icon.FromHandle(iconHandle);
            return (Icon)borrowedIcon.Clone();
        }
        finally
        {
            DestroyIcon(iconHandle);
        }
    }

    private void ConfigureWatcher()
    {
        if (_watcher is not null || !Directory.Exists(_reader.SessionsPath))
        {
            return;
        }

        _watcher = new FileSystemWatcher(_reader.SessionsPath, "*.jsonl")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = _quotaUpdatesActive
        };
        _watcher.Changed += SessionFileChanged;
        _watcher.Created += SessionFileChanged;
        _watcher.Renamed += SessionFileChanged;
    }

    private void SessionFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (_isExiting || _watcher?.EnableRaisingEvents is not true)
            {
                return;
            }

            _sessionChangeTimer.Stop();
            _sessionChangeTimer.Start();
        });
    }

    private async Task RefreshAsync()
    {
        if (_isExiting)
        {
            return;
        }

        if (_quotaUpdatesActive && _watcher is null)
        {
            ConfigureWatcher();
        }

        var attempt = await _refreshCoordinator.RefreshAsync();
        if (attempt is null || _isExiting || !_refreshCoordinator.IsLatest(attempt))
        {
            return;
        }

        var snapshot = attempt.Snapshot;
        var onlineFailure = attempt.OnlineFailure;
        if (snapshot is null)
        {
            ShowUnavailable(onlineFailure!.Message);
            return;
        }

        if (onlineFailure is null)
        {
            StatusText.Text = $"在线  {DateTime.Now:HH:mm}";
            StatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(85, 230, 193));
        }
        else
        {
            StatusText.Text = onlineFailure.Kind is UsageFailureKind.Authentication or UsageFailureKind.Credentials
                ? "登录异常 · 本地"
                : "离线 · 本地";
            StatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 180, 90));
        }

        _snapshot = snapshot;
        UpdateQuotaLayout(snapshot);
        UpdateResetTimes();
    }

    private static void UpdateQuota(
        System.Windows.Controls.TextBlock label,
        CircularProgress ring,
        RateLimitWindow? window)
    {
        var display = QuotaDisplayValue.From(window);
        label.Text = display.Text;
        ring.Value = display.RingValue;
    }

    private void UpdateQuotaLayout(RateLimitSnapshot snapshot)
    {
        var hasFiveHour = snapshot.FiveHour is not null;
        var hasWeekly = snapshot.Weekly is not null;
        var layout = QuotaLayout.Create(hasFiveHour, hasWeekly);

        FiveHourPanel.Visibility = layout.ShowFiveHour ? Visibility.Visible : Visibility.Collapsed;
        WeeklyPanel.Visibility = layout.ShowWeekly ? Visibility.Visible : Visibility.Collapsed;
        QuotaSeparator.Visibility = layout.ShowSeparator ? Visibility.Visible : Visibility.Collapsed;
        FiveHourColumn.Width = layout.ShowFiveHour ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        WeeklyColumn.Width = layout.ShowWeekly ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        QuotaSeparatorColumn.Width = layout.ShowSeparator ? new GridLength(1) : new GridLength(0);
        SetWindowWidth(layout.WindowWidth);

        if (snapshot.FiveHour is { } fiveHour)
        {
            UpdateQuota(FiveHourPercent, FiveHourRing, fiveHour);
        }

        if (snapshot.Weekly is { } weekly)
        {
            UpdateQuota(WeeklyPercent, WeeklyRing, weekly);
        }
    }

    private void SetWindowWidth(double targetWidth)
    {
        if (Math.Abs(Width - targetWidth) < 0.5)
        {
            return;
        }

        Width = targetWidth;
        if (_previewSnapshot is null)
        {
            UpdateComposerPlacement();
        }
    }

    private void UpdateResetTimes()
    {
        if (_snapshot is null)
        {
            return;
        }

        var now = _previewSnapshot?.ObservedAt ?? DateTimeOffset.Now;

        if (_snapshot.FiveHour is { } fiveHour)
        {
            FiveHourReset.Text = QuotaResetDisplay.FormatCountdown(fiveHour.ResetsAt, now);
            FiveHourReset.ToolTip = FormatFullReset(fiveHour.ResetsAt);
        }

        if (_snapshot.Weekly is { } weekly)
        {
            WeeklyReset.Text = QuotaResetDisplay.FormatCountdown(weekly.ResetsAt, now);
            WeeklyReset.ToolTip = FormatFullReset(weekly.ResetsAt);
        }

        var tooltipLines = new List<string> { StatusText.Text };
        if (_snapshot.FiveHour is not null)
        {
            tooltipLines.Add($"5 小时：{FiveHourPercent.Text}，{FiveHourReset.Text}");
        }
        if (_snapshot.Weekly is not null)
        {
            tooltipLines.Add($"周额度：{WeeklyPercent.Text}，{WeeklyReset.Text}");
        }
        ShellBorder.ToolTip = string.Join(Environment.NewLine, tooltipLines);
    }

    private static string FormatFullReset(DateTimeOffset resetAt) =>
        $"本地时间 {resetAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} 重置";

    private void ShowUnavailable(string message)
    {
        _snapshot = null;
        UpdateQuota(FiveHourPercent, FiveHourRing, window: null);
        UpdateQuota(WeeklyPercent, WeeklyRing, window: null);
        FiveHourReset.Text = "暂无数据";
        FiveHourReset.ToolTip = message;
        WeeklyReset.Text = "暂无数据";
        WeeklyReset.ToolTip = "在线查询失败且没有本地记录";
        StatusText.Text = "额度不可用";
        StatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 180, 90));
        ShellBorder.ToolTip = $"额度不可用{Environment.NewLine}{message}";
    }

    private void UpdateComposerPlacement()
    {
        if (_previewSnapshot is not null || _isExiting || _userHidden)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var observation = _composerProbeController.Poll(Width, Height, now);
        if (observation.Target is not { } target ||
            !CodexComposerLocator.TryProjectToCurrentWindow(target, out var placement))
        {
            if (observation.Target is not null)
            {
                _composerProbeController.Invalidate(now);
            }
            Hide();
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var wasVisible = IsVisible;
        if (!CodexComposerLocator.IsTargetActive(target, handle, allowOverlayProcess: wasVisible))
        {
            Hide();
            return;
        }

        ApplyComposerTheme(target.IsLightBackground);
        if (!wasVisible)
        {
            Opacity = 0;
            ShowWithoutActivation();
        }

        var nativePlacement = (
            X: (int)Math.Round(placement.Left),
            Y: (int)Math.Round(placement.Top),
            Width: Math.Max(1, (int)Math.Round(placement.Width)),
            Height: Math.Max(1, (int)Math.Round(placement.Height)));
        var placementChanged = _lastNativePlacement != nativePlacement ||
            _lastPlacedCodexHandle != target.WindowHandle;
        var needsZOrderRepair = CodexComposerLocator.TryGetOverlayZOrderRepair(
            target.WindowHandle,
            handle,
            out var zOrderInsertAfter);
        if (!wasVisible || placementChanged || needsZOrderRepair)
        {
            var updateZOrder = needsZOrderRepair;
            if (!SetWindowPos(
                    handle,
                    updateZOrder ? zOrderInsertAfter : IntPtr.Zero,
                    nativePlacement.X,
                    nativePlacement.Y,
                    nativePlacement.Width,
                    nativePlacement.Height,
                    SwpNoActivate | SwpShowWindow | (updateZOrder ? 0 : SwpNoZOrder)))
            {
                _lastNativePlacement = null;
                _lastPlacedCodexHandle = IntPtr.Zero;
                Hide();
                return;
            }

            _lastNativePlacement = nativePlacement;
            _lastPlacedCodexHandle = target.WindowHandle;
        }
        Opacity = Math.Clamp(_settings.Opacity, 0.6, 1.0);
    }

    private static async Task<CodexComposerTarget?> StartComposerProbeAsync(
        double width,
        double height,
        CancellationToken cancellationToken)
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("The executable path is unavailable.");
        }

        var startInfo = new ProcessStartInfo(executablePath)
        {
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("--composer-probe");
        startInfo.ArgumentList.Add(width.ToString("R", CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(height.ToString("R", CultureInfo.InvariantCulture));

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("The isolated composer probe did not start.");
        }

        using var cancellationRegistration = cancellationToken.Register(
            static state => TryTerminateProbeProcess((Process)state!),
            process);
        var outputTask = process.StandardOutput.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryTerminateProbeProcess(process);
            await process.WaitForExitAsync();
            _ = await outputTask;
            throw;
        }

        var output = await outputTask;
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        return JsonSerializer.Deserialize<ComposerProbePayload>(output)?.ToTarget();
    }

    private static void TryTerminateProbeProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            Win32Exception or
            NotSupportedException)
        {
            // A concurrently exiting probe needs no further cleanup.
        }
    }

    private void ApplyComposerTheme(bool isLightBackground)
    {
        if (_lastLightBackground == isLightBackground)
        {
            return;
        }

        _lastLightBackground = isLightBackground;
        var primary = new SolidColorBrush(isLightBackground
            ? System.Windows.Media.Color.FromRgb(78, 82, 87)
            : System.Windows.Media.Color.FromRgb(214, 217, 221));
        var secondary = new SolidColorBrush(isLightBackground
            ? System.Windows.Media.Color.FromRgb(112, 117, 123)
            : System.Windows.Media.Color.FromRgb(163, 168, 174));
        FiveHourPercent.Foreground = primary;
        WeeklyPercent.Foreground = primary;
        FiveHourLabel.Foreground = secondary;
        WeeklyLabel.Foreground = secondary;
        QuotaSeparator.Background = new SolidColorBrush(isLightBackground
            ? System.Windows.Media.Color.FromArgb(42, 0, 0, 0)
            : System.Windows.Media.Color.FromArgb(54, 255, 255, 255));
        WeeklyRing.TrackBrush = new SolidColorBrush(isLightBackground
            ? System.Windows.Media.Color.FromArgb(48, 0, 0, 0)
            : System.Windows.Media.Color.FromArgb(70, 255, 255, 255));
    }

    private void ToggleVisibility()
    {
        if (IsVisible)
        {
            HideToTray();
        }
        else
        {
            _userHidden = false;
            StartQuotaUpdates();
            UpdateComposerPlacement();
            _ = RefreshAsync();
        }
    }

    private void HideToTray()
    {
        _userHidden = true;
        Hide();
    }

    public void ShowFromExternalLaunch()
    {
        _userHidden = false;
        StartQuotaUpdates();
        UpdateComposerPlacement();
        _ = RefreshAsync();
    }

    private void ApplyClickThrough()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        style = _settings.ClickThrough ? style | WsExTransparent : style & ~WsExTransparent;
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style));
    }

    private void ApplyToolWindowStyle()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        style = style | WsExToolWindow | WsExNoActivate;
        style = style & ~WsExAppWindow;
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style));
    }

    private void EnableSettingsWrite()
    {
        if (!_settingsWriteEnabled)
        {
            _settingsWriteEnabled = true;
            _settingsLoadFailed = false;
            StatusText.ToolTip = null;
        }
    }

    private bool SaveSettings()
    {
        if (!_settingsWriteEnabled)
        {
            return false;
        }

        try
        {
            _settingsStore.Save(_settings);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "设置保存失败";
            StatusText.ToolTip = "无法写入本地设置文件；本次设置不会在重启后保留。";
            return false;
        }
    }

    private void ExitApplication()
    {
        _isExiting = true;
        SaveSettings();
        StopQuotaUpdates();
        _lifecycleTimer.Stop();
        _placementTimer.Stop();
        _composerProbeController.Dispose();
        _watcher?.Dispose();
        _refreshCoordinator.Dispose();
        _usageClient.Dispose();
        if (_trayIcon is not null)
        {
            var trayIcon = _trayIcon.Icon;
            var trayMenu = _trayIcon.ContextMenuStrip;
            _trayIcon.Visible = false;
            _trayIcon.Icon = null;
            _trayIcon.ContextMenuStrip = null;
            _trayIcon.Dispose();
            trayIcon?.Dispose();
            trayMenu?.Dispose();
        }
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_previewSnapshot is null && !_isExiting)
        {
            e.Cancel = true;
            HideToTray();
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern IntPtr GetWindowLong32(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr windowHandle, int index, IntPtr newValue);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern IntPtr SetWindowLong32(IntPtr windowHandle, int index, IntPtr newValue);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr iconHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    private static IntPtr GetWindowLongPtr(IntPtr windowHandle, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(windowHandle, index) : GetWindowLong32(windowHandle, index);

    private static IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr newValue) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(windowHandle, index, newValue)
            : SetWindowLong32(windowHandle, index, newValue);
}
