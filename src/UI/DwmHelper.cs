using System;
using System.Runtime.InteropServices;

namespace OmniHidTaskbar.UI
{
    internal static class DwmHelper
    {
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int pvAttribute, int cbAttribute);

        [DllImport("dcomp.dll", EntryPoint = "DCompositionBoostCompositorClock")]
        private static extern int DCompositionBoostCompositorClockNative([MarshalAs(UnmanagedType.Bool)] bool enable);

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        public static extern uint TimeBeginPeriod(uint periodMilliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        public static extern uint TimeEndPeriod(uint periodMilliseconds);

        [DllImport("user32.dll")]
        public static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [StructLayout(LayoutKind.Sequential)]
        public struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public uint GradientColor;
            public int AnimationId;
        }

        public const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;
        public const int DWMWA_CLOAK = 13;
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWCP_ROUND = 2;
        public const int WCA_ACCENT_POLICY = 19;
        public const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

        public static void EnableWindowTransitions(IntPtr hwnd, bool enable)
        {
            try
            {
                int forceDisabled = enable ? 0 : 1;
                DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref forceDisabled, sizeof(int));
            }
            catch { }
        }

        public static void CloakWindow(IntPtr hwnd, bool cloak)
        {
            try
            {
                int val = cloak ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref val, sizeof(int));
            }
            catch { }
        }

        public static void SetWindowRoundedCorners(IntPtr hwnd)
        {
            try
            {
                int cornerPreference = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
            }
            catch { }
        }

        public static void SetDarkMode(IntPtr hwnd, bool isDark)
        {
            try
            {
                int dark = isDark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            }
            catch { }
        }

        public static void ApplyAcrylicBlur(IntPtr hwnd, bool isDark)
        {
            try
            {
                uint gradientColor = isDark ? 0xCC181818 : 0xCCF5F5F5; // AABBGGRR
                var accent = new AccentPolicy
                {
                    AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
                    AccentFlags = 2,
                    GradientColor = gradientColor,
                    AnimationId = 0
                };

                int accentSize = Marshal.SizeOf(accent);
                IntPtr accentPtr = Marshal.AllocHGlobal(accentSize);
                try
                {
                    Marshal.StructureToPtr(accent, accentPtr, false);
                    var data = new WindowCompositionAttributeData
                    {
                        Attribute = WCA_ACCENT_POLICY,
                        Data = accentPtr,
                        SizeOfData = accentSize
                    };
                    SetWindowCompositionAttribute(hwnd, ref data);
                }
                finally
                {
                    Marshal.FreeHGlobal(accentPtr);
                }
            }
            catch { }
        }

        private static bool _timerBoostActive = false;
        private static bool _dcompBoostFailed = false;

        public static void BoostCompositorClock(bool enable)
        {
            if (enable)
            {
                if (!_dcompBoostFailed)
                {
                    try
                    {
                        int hr = DCompositionBoostCompositorClockNative(true);
                        if (hr < 0) _dcompBoostFailed = true;
                    }
                    catch
                    {
                        _dcompBoostFailed = true;
                    }
                }
                if (!_timerBoostActive)
                {
                    try
                    {
                        TimeBeginPeriod(1);
                        _timerBoostActive = true;
                    }
                    catch { }
                }
            }
            else
            {
                if (!_dcompBoostFailed)
                {
                    try { DCompositionBoostCompositorClockNative(false); } catch { }
                }
                if (_timerBoostActive)
                {
                    try
                    {
                        TimeEndPeriod(1);
                        _timerBoostActive = false;
                    }
                    catch { }
                }
            }
        }
    }
}
