using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using CodexQuotaWidget.Models;

namespace CodexQuotaWidget.Services;

internal readonly record struct CodexComposerTarget(
    IntPtr WindowHandle,
    ScreenRectangle WindowBounds,
    ScreenRectangle Placement,
    bool IsLightBackground);

internal sealed class CodexComposerLocator
{
    private const double BottomSearchDepth = 180;
    private const double MinimumModelButtonWidth = 44;
    private const string ComposerButtonClass = "h-token-button-composer";
    private const string CompactComposerButtonClass = "h-token-button-composer-sm";
    public bool TryLocate(
        double desiredWidthDips,
        double desiredHeightDips,
        out CodexComposerTarget target)
    {
        target = default;
        var windowHandle = FindCodexMainWindow();
        if (windowHandle == IntPtr.Zero ||
            !GetWindowRect(windowHandle, out var nativeWindowRect))
        {
            return false;
        }

        var windowRect = ToScreenRectangle(nativeWindowRect);
        var scale = Math.Max(1, GetDpiForWindow(windowHandle)) / 96d;
        var desiredWidth = desiredWidthDips * scale;
        var desiredHeight = desiredHeightDips * scale;

        try
        {
            var root = AutomationElement.FromHandle(windowHandle);
            var cacheRequest = new CacheRequest
            {
                TreeScope = TreeScope.Element,
                AutomationElementMode = AutomationElementMode.Full
            };
            cacheRequest.Add(AutomationElement.BoundingRectangleProperty);
            cacheRequest.Add(AutomationElement.IsOffscreenProperty);
            cacheRequest.Add(AutomationElement.NameProperty);
            cacheRequest.Add(AutomationElement.ClassNameProperty);

            AutomationElementCollection buttons;
            using (cacheRequest.Activate())
            {
                buttons = root.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            }

            var candidates = ReadButtonCandidates(buttons, windowRect);
            var permissions = candidates
                .Where(IsPermissionsButton)
                .OrderByDescending(candidate => candidate.Bounds.Top)
                .ThenBy(candidate => candidate.Bounds.Left)
                .FirstOrDefault();
            if (permissions is null)
            {
                return false;
            }

            var model = candidates
                .Where(candidate =>
                    candidate.Bounds.Left > permissions.Bounds.Right &&
                    candidate.Bounds.Width >= MinimumModelButtonWidth &&
                    Math.Abs(candidate.Bounds.CenterY - permissions.Bounds.CenterY) <= 8 &&
                    HasCssClass(candidate.ClassName, ComposerButtonClass) &&
                    !HasCssClass(candidate.ClassName, CompactComposerButtonClass))
                .OrderBy(candidate => candidate.Bounds.Left)
                .FirstOrDefault();
            if (model is null ||
                !ComposerPlacement.TryCreate(
                    permissions.Bounds,
                    model.Bounds,
                    desiredWidth,
                    desiredHeight,
                    out var placement))
            {
                return false;
            }

            target = new CodexComposerTarget(
                windowHandle,
                windowRect,
                placement,
                IsLightBackground(permissions.Bounds, model.Bounds));
            return true;
        }
        catch (Exception exception) when (
            exception is ElementNotAvailableException or
            COMException or
            InvalidOperationException or
            Win32Exception or
            UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool TryProjectToCurrentWindow(
        CodexComposerTarget target,
        out ScreenRectangle placement)
    {
        placement = default;
        if (target.WindowHandle == IntPtr.Zero ||
            !IsWindowVisible(target.WindowHandle) ||
            IsIconic(target.WindowHandle) ||
            !GetWindowRect(target.WindowHandle, out var nativeWindowRect))
        {
            return false;
        }

        return ComposerPlacement.TryProject(
            target.WindowBounds,
            target.Placement,
            ToScreenRectangle(nativeWindowRect),
            out placement);
    }

    public static bool IsTargetActive(
        CodexComposerTarget target,
        IntPtr overlayHandle,
        bool allowOverlayProcess)
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero || target.WindowHandle == IntPtr.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(target.WindowHandle, out var targetProcessId);
        _ = GetWindowThreadProcessId(foregroundWindow, out var foregroundProcessId);
        if (targetProcessId == 0 || foregroundProcessId == 0)
        {
            return false;
        }

        if (foregroundProcessId == targetProcessId)
        {
            return true;
        }

        if (!allowOverlayProcess || overlayHandle == IntPtr.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(overlayHandle, out var overlayProcessId);
        return overlayProcessId != 0 && foregroundProcessId == overlayProcessId;
    }

    private static IntPtr FindCodexMainWindow()
    {
        var foregroundWindow = GetForegroundWindow();
        var processes = Process.GetProcessesByName("ChatGPT");
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    var handle = process.MainWindowHandle;
                    if (handle == IntPtr.Zero ||
                        !IsWindowVisible(handle) ||
                        IsIconic(handle) ||
                        !CodexProcessMonitor.IsCodexDesktopExecutablePath(process.MainModule?.FileName) ||
                        !GetWindowRect(handle, out var bounds))
                    {
                        continue;
                    }

                    if (handle == foregroundWindow)
                    {
                        return handle;
                    }

                }
                catch (Exception exception) when (
                    exception is Win32Exception or
                    InvalidOperationException or
                    NotSupportedException)
                {
                    // The process can exit or become inaccessible while it is being inspected.
                }
            }

            // Accessibility work is only justified while the Codex main window is
            // active. Background windows are hidden by the caller and must not be
            // polled merely to keep an invisible overlay warm.
            return IntPtr.Zero;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static List<ButtonCandidate> ReadButtonCandidates(
        AutomationElementCollection buttons,
        ScreenRectangle windowRect)
    {
        var candidates = new List<ButtonCandidate>();
        var minimumTop = windowRect.Bottom - Math.Min(BottomSearchDepth, windowRect.Height * 0.3);
        for (var index = 0; index < buttons.Count; index++)
        {
            var button = buttons[index];
            try
            {
                var current = button.Cached;
                var bounds = new ScreenRectangle(
                    current.BoundingRectangle.Left,
                    current.BoundingRectangle.Top,
                    current.BoundingRectangle.Width,
                    current.BoundingRectangle.Height);
                if (current.IsOffscreen ||
                    !bounds.IsFinitePositive ||
                    bounds.Top < minimumTop ||
                    bounds.Left < windowRect.Left ||
                    bounds.Right > windowRect.Right)
                {
                    continue;
                }

                candidates.Add(new ButtonCandidate(button, current.Name, current.ClassName, bounds));
            }
            catch (Exception exception) when (
                exception is ElementNotAvailableException or COMException or InvalidOperationException)
            {
                // A React render can replace an accessibility element during enumeration.
            }
        }

        return candidates;
    }

    private static bool IsPermissionsButton(ButtonCandidate candidate) =>
        HasCssClass(candidate.ClassName, CompactComposerButtonClass) ||
        candidate.Name.Equals("更改权限", StringComparison.OrdinalIgnoreCase) ||
        candidate.Name.Equals("Change permissions", StringComparison.OrdinalIgnoreCase);

    private static bool HasCssClass(string className, string expectedClass) =>
        className.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(expectedClass, StringComparer.Ordinal);

    private static bool IsLightBackground(ScreenRectangle permissions, ScreenRectangle model)
    {
        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return true;
        }

        try
        {
            var sampleX = (int)Math.Round((permissions.Right + model.Left) / 2);
            var sampleY = (int)Math.Round(Math.Min(permissions.Top, model.Top) - 3);
            var color = GetPixel(screenDc, sampleX, sampleY);
            if (color == uint.MaxValue)
            {
                return true;
            }

            var red = color & 0xff;
            var green = color >> 8 & 0xff;
            var blue = color >> 16 & 0xff;
            var luminance = (0.2126 * red + 0.7152 * green + 0.0722 * blue) / 255;
            return luminance >= 0.56;
        }
        finally
        {
            _ = ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static ScreenRectangle ToScreenRectangle(NativeRectangle rectangle) =>
        new(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right - rectangle.Left,
            rectangle.Bottom - rectangle.Top);

    private sealed record ButtonCandidate(
        AutomationElement Element,
        string Name,
        string ClassName,
        ScreenRectangle Bounds);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRectangle rectangle);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr windowHandle, IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr deviceContext, int x, int y);
}
