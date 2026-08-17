using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using CodexQuotaWidget.Models;

namespace CodexQuotaWidget.Services;

internal readonly record struct CodexComposerTarget(
    IntPtr OwnerHandle,
    ScreenRectangle Placement,
    bool IsLightBackground);

internal sealed class CodexComposerLocator
{
    private const double BottomSearchDepth = 180;
    private const double MinimumModelButtonWidth = 44;
    private const string ComposerButtonClass = "h-token-button-composer";
    private const string CompactComposerButtonClass = "h-token-button-composer-sm";
    private IntPtr _cachedOwnerHandle;
    private AutomationElement? _cachedPermissionsElement;
    private AutomationElement? _cachedModelElement;

    public bool TryLocate(
        double desiredWidthDips,
        double desiredHeightDips,
        out CodexComposerTarget target)
    {
        target = default;
        var ownerHandle = FindCodexMainWindow();
        if (ownerHandle == IntPtr.Zero ||
            !GetWindowRect(ownerHandle, out var nativeWindowRect))
        {
            return false;
        }

        var windowRect = ToScreenRectangle(nativeWindowRect);
        var scale = Math.Max(1, GetDpiForWindow(ownerHandle)) / 96d;
        var desiredWidth = desiredWidthDips * scale;
        var desiredHeight = desiredHeightDips * scale;

        try
        {
            if (ownerHandle == _cachedOwnerHandle &&
                TryReadButtonCandidate(_cachedPermissionsElement, windowRect, out var cachedPermissions) &&
                TryReadButtonCandidate(_cachedModelElement, windowRect, out var cachedModel) &&
                IsPermissionsButton(cachedPermissions) &&
                IsModelButton(cachedModel, cachedPermissions) &&
                ComposerPlacement.TryCreate(
                    cachedPermissions.Bounds,
                    cachedModel.Bounds,
                    desiredWidth,
                    desiredHeight,
                    out var cachedPlacement))
            {
                target = new CodexComposerTarget(
                    ownerHandle,
                    cachedPlacement,
                    IsLightBackground(cachedPermissions.Bounds, cachedModel.Bounds));
                return true;
            }

            ClearCachedAnchors();
            var root = AutomationElement.FromHandle(ownerHandle);
            var buttons = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
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

            _cachedOwnerHandle = ownerHandle;
            _cachedPermissionsElement = permissions.Element;
            _cachedModelElement = model.Element;
            target = new CodexComposerTarget(
                ownerHandle,
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

    private static IntPtr FindCodexMainWindow()
    {
        var foregroundWindow = GetForegroundWindow();
        var processes = Process.GetProcessesByName("ChatGPT");
        try
        {
            var candidates = new List<(IntPtr Handle, long Area)>();
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

                    var width = Math.Max(0, bounds.Right - bounds.Left);
                    var height = Math.Max(0, bounds.Bottom - bounds.Top);
                    candidates.Add((handle, (long)width * height));
                }
                catch (Exception exception) when (
                    exception is Win32Exception or
                    InvalidOperationException or
                    NotSupportedException)
                {
                    // The process can exit or become inaccessible while it is being inspected.
                }
            }

            return candidates
                .OrderByDescending(candidate => candidate.Area)
                .Select(candidate => candidate.Handle)
                .FirstOrDefault();
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
                var current = button.Current;
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

    private static bool TryReadButtonCandidate(
        AutomationElement? element,
        ScreenRectangle windowRect,
        out ButtonCandidate candidate)
    {
        candidate = null!;
        if (element is null)
        {
            return false;
        }

        try
        {
            var current = element.Current;
            var bounds = new ScreenRectangle(
                current.BoundingRectangle.Left,
                current.BoundingRectangle.Top,
                current.BoundingRectangle.Width,
                current.BoundingRectangle.Height);
            if (current.IsOffscreen ||
                !bounds.IsFinitePositive ||
                bounds.Left < windowRect.Left ||
                bounds.Right > windowRect.Right ||
                bounds.Top < windowRect.Bottom - Math.Min(BottomSearchDepth, windowRect.Height * 0.3))
            {
                return false;
            }

            candidate = new ButtonCandidate(element, current.Name, current.ClassName, bounds);
            return true;
        }
        catch (Exception exception) when (
            exception is ElementNotAvailableException or COMException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsPermissionsButton(ButtonCandidate candidate) =>
        HasCssClass(candidate.ClassName, CompactComposerButtonClass) ||
        candidate.Name.Equals("更改权限", StringComparison.OrdinalIgnoreCase) ||
        candidate.Name.Equals("Change permissions", StringComparison.OrdinalIgnoreCase);

    private static bool IsModelButton(ButtonCandidate candidate, ButtonCandidate permissions) =>
        candidate.Bounds.Left > permissions.Bounds.Right &&
        candidate.Bounds.Width >= MinimumModelButtonWidth &&
        Math.Abs(candidate.Bounds.CenterY - permissions.Bounds.CenterY) <= 8 &&
        HasCssClass(candidate.ClassName, ComposerButtonClass) &&
        !HasCssClass(candidate.ClassName, CompactComposerButtonClass);

    private void ClearCachedAnchors()
    {
        _cachedOwnerHandle = IntPtr.Zero;
        _cachedPermissionsElement = null;
        _cachedModelElement = null;
    }

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
