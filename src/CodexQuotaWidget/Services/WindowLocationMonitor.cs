using System.Runtime.InteropServices;

namespace CodexQuotaWidget.Services;

internal sealed class WindowLocationMonitor : IDisposable
{
    internal const uint LocationChangeEvent = 0x800B;
    private const uint WineventOutOfContext = 0x0000;
    private const uint WineventSkipOwnProcess = 0x0002;
    private const int ObjectIdWindow = 0;
    private const int ChildIdSelf = 0;

    private readonly WinEventDelegate _callback;
    private IntPtr _hook;
    private bool _disposed;

    public WindowLocationMonitor()
    {
        _callback = HandleWinEvent;
    }

    public event Action<IntPtr>? LocationChanged;

    public bool Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_hook != IntPtr.Zero)
        {
            return true;
        }

        _hook = SetWinEventHook(
            LocationChangeEvent,
            LocationChangeEvent,
            IntPtr.Zero,
            _callback,
            0,
            0,
            WineventOutOfContext | WineventSkipOwnProcess);
        return _hook != IntPtr.Zero;
    }

    internal static bool IsTopLevelWindowLocationChange(
        uint eventType,
        IntPtr windowHandle,
        int objectId,
        int childId) =>
        eventType == LocationChangeEvent &&
        windowHandle != IntPtr.Zero &&
        objectId == ObjectIdWindow &&
        childId == ChildIdSelf;

    private void HandleWinEvent(
        IntPtr hook,
        uint eventType,
        IntPtr windowHandle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (!_disposed &&
            IsTopLevelWindowLocationChange(eventType, windowHandle, objectId, childId))
        {
            LocationChanged?.Invoke(windowHandle);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hook != IntPtr.Zero)
        {
            _ = UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private delegate void WinEventDelegate(
        IntPtr hook,
        uint eventType,
        IntPtr windowHandle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMinimum,
        uint eventMaximum,
        IntPtr eventHookModule,
        WinEventDelegate eventHook,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);
}
