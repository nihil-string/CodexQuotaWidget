using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CodexQuotaWidget.Models;
using CodexQuotaWidget.Services;
using Forms = System.Windows.Forms;

namespace CodexQuotaWidget;

public partial class MainWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x20;

    private readonly SessionRateLimitReader _reader = new();
    private readonly CodexUsageClient _usageClient = new();
    private readonly WidgetSettingsStore _settingsStore = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _countdownTimer;
    private readonly WidgetSettings _settings;
    private readonly Forms.NotifyIcon _trayIcon;
    private Forms.ToolStripMenuItem? _trayTopmostItem;
    private Forms.ToolStripMenuItem? _trayClickThroughItem;
    private Forms.ToolStripMenuItem? _trayLockItem;
    private System.Windows.Controls.MenuItem? _windowTopmostItem;
    private System.Windows.Controls.MenuItem? _windowClickThroughItem;
    private System.Windows.Controls.MenuItem? _windowLockItem;
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _refreshCancellation;
    private RateLimitSnapshot? _snapshot;
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsStore.Load();
        Topmost = _settings.AlwaysOnTop;
        Opacity = Math.Clamp(_settings.Opacity, 0.6, 1.0);
        ShellBorder.ContextMenu = CreateWindowContextMenu();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) => UpdateCountdowns();

        _trayIcon = CreateTrayIcon();
        SyncMenuChecks();
        Loaded += MainWindow_Loaded;
        SourceInitialized += (_, _) =>
        {
            AcrylicBackdrop.Apply(this);
            ApplyClickThrough();
        };
        LocationChanged += (_, _) => RememberPosition();
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RestorePosition();
        ConfigureWatcher();
        _refreshTimer.Start();
        _countdownTimer.Start();
        await RefreshAsync();
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示 / 隐藏", null, (_, _) => Dispatcher.Invoke(ToggleVisibility));
        menu.Items.Add("立即刷新", null, (_, _) => Dispatcher.Invoke(async () => await RefreshAsync()));
        menu.Items.Add(new Forms.ToolStripSeparator());

        _trayLockItem = new Forms.ToolStripMenuItem("锁定悬浮窗位置") { Checked = _settings.IsPositionLocked };
        _trayLockItem.Click += (_, _) => Dispatcher.Invoke(TogglePositionLock);
        menu.Items.Add(_trayLockItem);

        _trayClickThroughItem = new Forms.ToolStripMenuItem("鼠标穿透") { Checked = _settings.ClickThrough };
        _trayClickThroughItem.Click += (_, _) => Dispatcher.Invoke(ToggleClickThrough);
        menu.Items.Add(_trayClickThroughItem);

        _trayTopmostItem = new Forms.ToolStripMenuItem("始终置顶") { Checked = _settings.AlwaysOnTop };
        _trayTopmostItem.Click += (_, _) => Dispatcher.Invoke(ToggleTopmost);
        menu.Items.Add(_trayTopmostItem);

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
        var menu = new System.Windows.Controls.ContextMenu();

        var refreshItem = new System.Windows.Controls.MenuItem { Header = "立即刷新" };
        refreshItem.Click += async (_, _) => await RefreshAsync();
        menu.Items.Add(refreshItem);
        menu.Items.Add(new System.Windows.Controls.Separator());

        _windowLockItem = new System.Windows.Controls.MenuItem
        {
            Header = "锁定悬浮窗位置",
            IsCheckable = true
        };
        _windowLockItem.Click += (_, _) => TogglePositionLock();
        menu.Items.Add(_windowLockItem);

        _windowClickThroughItem = new System.Windows.Controls.MenuItem
        {
            Header = "鼠标穿透",
            IsCheckable = true
        };
        _windowClickThroughItem.Click += (_, _) => ToggleClickThrough();
        menu.Items.Add(_windowClickThroughItem);

        _windowTopmostItem = new System.Windows.Controls.MenuItem
        {
            Header = "始终置顶",
            IsCheckable = true
        };
        _windowTopmostItem.Click += (_, _) => ToggleTopmost();
        menu.Items.Add(_windowTopmostItem);

        var opacityMenu = new System.Windows.Controls.MenuItem { Header = "透明度" };
        AddWindowOpacityItem(opacityMenu, "70%", 0.70);
        AddWindowOpacityItem(opacityMenu, "85%", 0.85);
        AddWindowOpacityItem(opacityMenu, "96%", 0.96);
        AddWindowOpacityItem(opacityMenu, "100%", 1.00);
        menu.Items.Add(opacityMenu);
        menu.Items.Add(new System.Windows.Controls.Separator());

        var hideItem = new System.Windows.Controls.MenuItem { Header = "隐藏到托盘" };
        hideItem.Click += (_, _) => Hide();
        menu.Items.Add(hideItem);

        var exitItem = new System.Windows.Controls.MenuItem { Header = "退出" };
        exitItem.Click += (_, _) => ExitApplication();
        menu.Items.Add(exitItem);

        menu.Opened += (_, _) => SyncMenuChecks();
        return menu;
    }

    private void AddOpacityItem(Forms.ToolStripMenuItem parent, string label, double opacity)
    {
        parent.DropDownItems.Add(label, null, (_, _) => Dispatcher.Invoke(() =>
        {
            _settings.Opacity = opacity;
            Opacity = opacity;
            SaveSettings();
        }));
    }

    private void AddWindowOpacityItem(System.Windows.Controls.MenuItem parent, string label, double opacity)
    {
        var item = new System.Windows.Controls.MenuItem { Header = label };
        item.Click += (_, _) =>
        {
            _settings.Opacity = opacity;
            Opacity = opacity;
            SaveSettings();
        };
        parent.Items.Add(item);
    }

    private void TogglePositionLock()
    {
        _settings.IsPositionLocked = !_settings.IsPositionLocked;
        SyncMenuChecks();
        SaveSettings();
    }

    private void ToggleClickThrough()
    {
        _settings.ClickThrough = !_settings.ClickThrough;
        ApplyClickThrough();
        SyncMenuChecks();
        SaveSettings();
    }

    private void ToggleTopmost()
    {
        _settings.AlwaysOnTop = !_settings.AlwaysOnTop;
        Topmost = _settings.AlwaysOnTop;
        SyncMenuChecks();
        SaveSettings();
    }

    private void SyncMenuChecks()
    {
        if (_trayLockItem is not null) _trayLockItem.Checked = _settings.IsPositionLocked;
        if (_trayClickThroughItem is not null) _trayClickThroughItem.Checked = _settings.ClickThrough;
        if (_trayTopmostItem is not null) _trayTopmostItem.Checked = _settings.AlwaysOnTop;
        if (_windowLockItem is not null) _windowLockItem.IsChecked = _settings.IsPositionLocked;
        if (_windowClickThroughItem is not null) _windowClickThroughItem.IsChecked = _settings.ClickThrough;
        if (_windowTopmostItem is not null) _windowTopmostItem.IsChecked = _settings.AlwaysOnTop;
    }

    private static Icon CreateTrayGlyph()
    {
        var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(System.Drawing.Color.Transparent);
        using var outerPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(85, 230, 193), 3.2f);
        using var accentPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(255, 180, 90), 3.2f);
        graphics.DrawArc(outerPen, 5, 5, 22, 22, -75, 245);
        graphics.DrawArc(accentPen, 9, 9, 14, 14, 110, 125);
        return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
    }

    private void ConfigureWatcher()
    {
        if (!Directory.Exists(_reader.SessionsPath))
        {
            return;
        }

        _watcher = new FileSystemWatcher(_reader.SessionsPath, "*.jsonl")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };
        _watcher.Changed += SessionFileChanged;
        _watcher.Created += SessionFileChanged;
        _watcher.Renamed += SessionFileChanged;
    }

    private void SessionFileChanged(object sender, FileSystemEventArgs e)
    {
        Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(350);
            await RefreshAsync();
        });
    }

    private async Task RefreshAsync()
    {
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = new CancellationTokenSource();

        try
        {
            RateLimitSnapshot? snapshot;
            try
            {
                snapshot = await _usageClient.FetchAsync(_refreshCancellation.Token);
                StatusText.Text = $"在线 · {DateTime.Now:HH:mm}";
                StatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(85, 230, 193));
            }
            catch (UsageFetchException exception)
            {
                snapshot = await _reader.ReadLatestAsync(_refreshCancellation.Token);
                if (snapshot is null)
                {
                    ShowUnavailable(exception.Message);
                    return;
                }

                StatusText.Text = exception.Kind is UsageFailureKind.Authentication or UsageFailureKind.Credentials
                    ? "登录异常 · 显示本地记录"
                    : "离线 · 显示本地记录";
                StatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 180, 90));
            }

            _snapshot = snapshot;
            UpdateQuota(FiveHourPercent, FiveHourBar, snapshot.FiveHour);
            UpdateQuota(WeeklyPercent, WeeklyBar, snapshot.Weekly);
            UpdateCountdowns();
        }
        catch (OperationCanceledException)
        {
            // A newer file event superseded this refresh.
        }
    }

    private static void UpdateQuota(
        System.Windows.Controls.TextBlock label,
        FrameworkElement bar,
        RateLimitWindow window)
    {
        var remainingPercent = window.RemainingPercent;
        label.Text = $"{Math.Round(remainingPercent):0}%";
        bar.Width = Math.Clamp(remainingPercent / 100d * 290d, 0, 290);
    }

    private void UpdateCountdowns()
    {
        if (_snapshot is null)
        {
            return;
        }

        FiveHourReset.Text = FormatReset(_snapshot.FiveHour.ResetsAt);
        WeeklyReset.Text = FormatReset(_snapshot.Weekly.ResetsAt);
    }

    private static string FormatReset(DateTimeOffset resetAt)
    {
        var remaining = resetAt - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero)
        {
            return "已重置 · 等待 Codex 更新";
        }

        if (remaining.TotalDays >= 1)
        {
            return $"{(int)remaining.TotalDays}天 {remaining.Hours}小时后重置";
        }

        if (remaining.TotalHours >= 1)
        {
            return $"{(int)remaining.TotalHours}小时 {remaining.Minutes}分后重置";
        }

        return $"{Math.Max(0, remaining.Minutes)}分 {Math.Max(0, remaining.Seconds)}秒后重置";
    }

    private void ShowUnavailable(string message)
    {
        _snapshot = null;
        FiveHourPercent.Text = "--";
        WeeklyPercent.Text = "--";
        FiveHourBar.Width = 0;
        WeeklyBar.Width = 0;
        FiveHourReset.Text = message;
        WeeklyReset.Text = "在线查询失败且没有本地记录";
        StatusText.Text = "暂无额度事件";
        StatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 180, 90));
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_settings.IsPositionLocked && e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            Show();
            Activate();
        }
    }

    private void RestorePosition()
    {
        if (_settings.Left is { } left && _settings.Top is { } top &&
            left >= SystemParameters.VirtualScreenLeft - Width + 40 &&
            left <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 40 &&
            top >= SystemParameters.VirtualScreenTop - Height + 40 &&
            top <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40)
        {
            Left = left;
            Top = top;
        }
        else
        {
            Left = SystemParameters.WorkArea.Right - Width - 24;
            Top = SystemParameters.WorkArea.Bottom - Height - 24;
        }

    }

    private void RememberPosition()
    {
        if (WindowState == WindowState.Normal)
        {
            _settings.Left = Left;
            _settings.Top = Top;
        }
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

    private void SaveSettings()
    {
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "设置保存失败";
        }
    }

    private void ExitApplication()
    {
        _isExiting = true;
        SaveSettings();
        _watcher?.Dispose();
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _usageClient.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExiting)
        {
            e.Cancel = true;
            Hide();
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

    private static IntPtr GetWindowLongPtr(IntPtr windowHandle, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(windowHandle, index) : GetWindowLong32(windowHandle, index);

    private static IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr newValue) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(windowHandle, index, newValue)
            : SetWindowLong32(windowHandle, index, newValue);
}
