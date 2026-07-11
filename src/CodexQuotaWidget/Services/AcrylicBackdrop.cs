using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CodexQuotaWidget.Services;

internal static class AcrylicBackdrop
{
    private const int WindowCompositionAttributeAccentPolicy = 19;
    private const int DwmWindowCornerPreference = 33;

    public static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var accent = new AccentPolicy
        {
            AccentState = AccentState.EnableAcrylicBlurBehind,
            AccentFlags = 2,
            // ABGR: 50% opaque neutral charcoal. Text remains fully opaque in WPF.
            GradientColor = unchecked((int)0x801B1816)
        };

        var accentSize = Marshal.SizeOf<AccentPolicy>();
        var accentPointer = Marshal.AllocHGlobal(accentSize);
        try
        {
            Marshal.StructureToPtr(accent, accentPointer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttributeAccentPolicy,
                Data = accentPointer,
                SizeOfData = accentSize
            };
            SetWindowCompositionAttribute(handle, ref data);

            // The native window owns the only corner shape. WPF content remains rectangular
            // and is clipped together with the acrylic backdrop by DWM.
            var cornerPreference = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref cornerPreference, sizeof(int));
        }
        finally
        {
            Marshal.FreeHGlobal(accentPointer);
        }
    }

    private enum AccentState
    {
        EnableAcrylicBlurBehind = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(
        IntPtr windowHandle,
        ref WindowCompositionAttributeData data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
