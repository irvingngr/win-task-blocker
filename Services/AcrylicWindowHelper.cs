using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WinTaskBlocker.Services;

/// <summary>
/// Enables Windows 10/11 Acrylic Glass Blur and rounded corners (DWM) without double-border artifacts.
/// </summary>
public static class AcrylicWindowHelper
{
    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;

    private const uint DWMWCP_ROUND = 2;
    private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

    private enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
        ACCENT_INVALID_STATE = 5
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public uint AccentFlags;
        public uint GradientColor;
        public uint AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    public static void EnableAcrylicBlur(Window window, byte alphaOpacity = 165, uint tintBgr = 0x36192B)
    {
        try
        {
            var helper = new WindowInteropHelper(window);
            if (helper.Handle == IntPtr.Zero) return;

            // Enable dark mode frame and native 8px rounded corners on Windows 11
            uint darkMode = 1;
            DwmSetWindowAttribute(helper.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(uint));

            uint cornerPref = DWMWCP_ROUND;
            DwmSetWindowAttribute(helper.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(uint));

            // Suppress Windows 11's native outer DWM border so only our single rounded WPF border is visible
            uint noBorder = DWMWA_COLOR_NONE;
            DwmSetWindowAttribute(helper.Handle, DWMWA_BORDER_COLOR, ref noBorder, sizeof(uint));

            // Apply Acrylic Blur Behind with AccentFlags = 0 (prevents secondary DWM border frame)
            uint gradientColor = ((uint)alphaOpacity << 24) | (tintBgr & 0xFFFFFF);
            var accent = new AccentPolicy
            {
                AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 0,
                GradientColor = gradientColor
            };

            int accentStructSize = Marshal.SizeOf(accent);
            IntPtr accentPtr = Marshal.AllocHGlobal(accentStructSize);
            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = 19, // WCA_ACCENT_POLICY
                    SizeOfData = accentStructSize,
                    Data = accentPtr
                };
                SetWindowCompositionAttribute(helper.Handle, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }
        catch
        {
            // Fallback gracefully to WPF translucent glass brush if DWM API is unavailable
        }
    }
}
