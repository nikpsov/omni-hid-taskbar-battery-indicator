using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Microsoft.Win32;
using Color = System.Windows.Media.Color;

namespace OmniHidTaskbar.UI
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Desktop Window Manager (DWM), Compositor & Immersive Theme Interop
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Supported Desktop Window Manager (DWM) hardware backdrop effect types.
    /// </summary>
    public enum BackdropType
    {
        /// <summary>No hardware backdrop effect; standard layered composition.</summary>
        None = 0,

        /// <summary>Standard Mica material suited for application main windows.</summary>
        Mica = 1,

        /// <summary>Acrylic translucent blur material suited for taskbar shell flyouts and popups.</summary>
        Acrylic = 2,

        /// <summary>Mica Alt (tabbed) material with pronounced tint for document/tabbed windows.</summary>
        Tabbed = 3
    }

    /// <summary>
    /// Provides low-level Win32 interop with the Desktop Window Manager (DWM), DirectComposition,
    /// and UXTheme Immersive color palette engines, enabling hardware backdrops (Mica/Acrylic),
    /// dynamic system accent colors, immersive dark mode, and compositor frame-rate boosting.
    /// </summary>
    internal static class DwmHelper
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Native Win32 P/Invoke Declarations
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Sets the value of Desktop Window Manager (DWM) non-client rendering attributes for a window.
        /// </summary>
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int pvAttribute, int cbAttribute);

        /// <summary>
        /// Requests DirectComposition to temporarily boost the compositor clock to match the refresh rate.
        /// </summary>
        [DllImport("dcomp.dll", EntryPoint = "DCompositionBoostCompositorClock")]
        private static extern int DCompositionBoostCompositorClockNative([MarshalAs(UnmanagedType.Bool)] bool enable);

        /// <summary>
        /// Requests a minimum resolution for periodic timers in milliseconds.
        /// </summary>
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        public static extern uint TimeBeginPeriod(uint periodMilliseconds);

        /// <summary>
        /// Clears a previously set minimum timer resolution.
        /// </summary>
        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        public static extern uint TimeEndPeriod(uint periodMilliseconds);

        /// <summary>
        /// Undocumented User32 API used for setting window accent policies such as Acrylic blur.
        /// </summary>
        [DllImport("user32.dll")]
        public static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        /// <summary>
        /// Queries an immersive system theme color from the UXTheme color set by index (UXTheme ordinal #95).
        /// </summary>
        [DllImport("uxtheme.dll", EntryPoint = "#95", CharSet = CharSet.Unicode)]
        public static extern uint GetImmersiveColorFromColorSetEx(uint dwImmersiveColorSet, uint dwImmersiveColorType, bool bIgnoreHighContrast, uint dwHighContrastCacheMode);

        /// <summary>
        /// Retrieves the user preference color set ID for immersive UI elements (UXTheme ordinal #98).
        /// </summary>
        [DllImport("uxtheme.dll", EntryPoint = "#98", CharSet = CharSet.Unicode)]
        public static extern uint GetImmersiveUserColorSetPreference(bool bForceCheckRegistry, bool bSkipSysColors);

        /// <summary>
        /// Resolves an immersive color type numeric identifier from its name string (UXTheme ordinal #96).
        /// </summary>
        [DllImport("uxtheme.dll", EntryPoint = "#96", CharSet = CharSet.Unicode)]
        public static extern uint GetImmersiveColorTypeFromName(IntPtr pName);

        // ═══════════════════════════════════════════════════════════════════════
        // Win32 Structures & DWM Constants
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Wrapper structure for <see cref="SetWindowCompositionAttribute"/>.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        /// <summary>
        /// Defines the composition accent policy (e.g., acrylic blur behind, tint gradient).
        /// </summary>
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
        public const int DWMWA_MICA_EFFECT = 1029;
        public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        public const int DWMWCP_ROUND = 2;
        public const int WCA_ACCENT_POLICY = 19;
        public const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
        public const int ACCENT_ENABLE_BLURBEHIND = 3;

        // DWM System Backdrop Type values (Windows 11 Build 22621+)
        public const int DWMSBT_AUTO = 0;
        public const int DWMSBT_NONE = 1;
        public const int DWMSBT_MAINWINDOW = 2;       // Mica
        public const int DWMSBT_TRANSIENTWINDOW = 3;  // Acrylic
        public const int DWMSBT_TABBEDWINDOW = 4;     // Mica Alt

        // ═══════════════════════════════════════════════════════════════════════
        // Window Attribute Configuration Methods
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Enables or forces disabling of standard DWM window open/close animations.
        /// Disabling transitions prevents flicker when docking to the taskbar.
        /// </summary>
        /// <param name="hwnd">Target window handle.</param>
        /// <param name="enable"><c>true</c> to enable standard animations; <c>false</c> to disable.</param>
        public static void EnableWindowTransitions(IntPtr hwnd, bool enable)
        {
            try
            {
                int forceDisabled = enable ? 0 : 1;
                DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref forceDisabled, sizeof(int));
            }
            catch { }
        }

        /// <summary>
        /// Cloaks or uncloaks a window. A cloaked window remains alive in the composition tree
        /// but is completely invisible to the user without triggering unmapping or layout teardown.
        /// </summary>
        /// <param name="hwnd">Target window handle.</param>
        /// <param name="cloak"><c>true</c> to hide without unmapping; <c>false</c> to uncloak.</param>
        public static void CloakWindow(IntPtr hwnd, bool cloak)
        {
            try
            {
                int val = cloak ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref val, sizeof(int));
            }
            catch { }
        }

        /// <summary>
        /// Configures Windows 11 DWM native hardware rounded corners on the target window.
        /// </summary>
        /// <param name="hwnd">Target window handle.</param>
        public static void SetWindowRoundedCorners(IntPtr hwnd)
        {
            try
            {
                int cornerPreference = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
            }
            catch { }
        }

        /// <summary>
        /// Toggles Windows immersive dark mode for window chrome, titlebars, and system menus.
        /// </summary>
        /// <param name="hwnd">Target window handle.</param>
        /// <param name="isDark"><c>true</c> for dark mode; <c>false</c> for light mode.</param>
        public static void SetDarkMode(IntPtr hwnd, bool isDark)
        {
            try
            {
                int dark = isDark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            }
            catch { }
        }

        /// <summary>
        /// Applies the highest-fidelity hardware backdrop material supported by the host OS (Mica or Acrylic).
        /// Falls back seamlessly to <see cref="SetWindowCompositionAttribute"/> Acrylic blur on older builds.
        /// </summary>
        /// <param name="hwnd">Target window handle.</param>
        /// <param name="type">Requested material backdrop type.</param>
        /// <param name="isDark">Theme state used for fallback tinting.</param>
        /// <returns><c>true</c> if backdrop attribute was successfully applied; otherwise <c>false</c>.</returns>
        public static bool ApplySystemBackdrop(IntPtr hwnd, BackdropType type, bool isDark)
        {
            if (hwnd == IntPtr.Zero) return false;

            // Step 1: Ensure dark/light mode attribute is synchronized with DWM
            SetDarkMode(hwnd, isDark);

            if (type == BackdropType.None)
            {
                int none = DWMSBT_NONE;
                try { DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int)); } catch { }
                return true;
            }

            // Step 2: Attempt Windows 11 22H2+ (Build 22621+) DWMWA_SYSTEMBACKDROP_TYPE
            try
            {
                int dwmType = DWMSBT_AUTO;
                switch (type)
                {
                    case BackdropType.Mica:
                        dwmType = DWMSBT_MAINWINDOW;
                        break;
                    case BackdropType.Acrylic:
                        dwmType = DWMSBT_TRANSIENTWINDOW;
                        break;
                    case BackdropType.Tabbed:
                        dwmType = DWMSBT_TABBEDWINDOW;
                        break;
                }

                int hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref dwmType, sizeof(int));
                if (hr == 0) return true;
            }
            catch { }

            // Step 3: Attempt Windows 11 21H2 (Build 22000) DWMWA_MICA_EFFECT
            if (type == BackdropType.Mica)
            {
                try
                {
                    int trueVal = 1;
                    int hr = DwmSetWindowAttribute(hwnd, DWMWA_MICA_EFFECT, ref trueVal, sizeof(int));
                    if (hr == 0) return true;
                }
                catch { }
            }

            // Step 4: Universal fallback for Acrylic via SetWindowCompositionAttribute
            if (type == BackdropType.Acrylic)
            {
                ApplyAcrylicBlur(hwnd, isDark);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Applies Fluent Acrylic blur-behind backdrop effect to the window via User32 accent policy.
        /// </summary>
        /// <param name="hwnd">Target window handle.</param>
        /// <param name="isDark"><c>true</c> for dark acrylic tint; <c>false</c> for light acrylic tint.</param>
        public static void ApplyAcrylicBlur(IntPtr hwnd, bool isDark)
        {
            try
            {
                uint gradientColor = isDark ? 0xCC181818 : 0xCCF5F5F5; // AABBGGRR format
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

        // ═══════════════════════════════════════════════════════════════════════
        // System Immersive & Theme Color Palette Management
        // ═══════════════════════════════════════════════════════════════════════

        private static bool _colorsCached = false;
        private static bool _cachedIsDark = true;
        private static Color _cachedAccentColor;
        private static Brush _cachedAccentBrush;
        private static Brush _cachedHoverBrush;
        private static Brush _cachedPressedBrush;
        private static Brush _cachedPrimaryTextBrush;
        private static Brush _cachedSecondaryTextBrush;
        private static Brush _cachedSubtleBorderBrush;
        private static Brush _cachedCardBackgroundBrush;

        /// <summary>
        /// Invalidates cached system brushes and colors, forcing a refresh on next query.
        /// </summary>
        public static void InvalidateThemeColorCache()
        {
            _colorsCached = false;
        }

        /// <summary>
        /// Ensures system theme brushes are populated for the current dark/light mode preference.
        /// </summary>
        private static void EnsureThemeColors(bool isDark)
        {
            if (_colorsCached && _cachedIsDark == isDark) return;

            _cachedIsDark = isDark;
            _cachedAccentColor = QuerySystemAccentColor(isDark);

            var accentSolid = new SolidColorBrush(_cachedAccentColor);
            accentSolid.Freeze();
            _cachedAccentBrush = accentSolid;

            // Hover & Pressed tokens derived from Windows 11 Shell SystemTray button standards
            // In dark mode: ~8% white for hover, ~5% white for pressed
            // In light mode: ~75% white for hover, ~50% white for pressed (crisp translucent white highlight over Mica taskbar)
            var hoverColor = isDark ? Color.FromArgb(20, 255, 255, 255) : Color.FromArgb(185, 255, 255, 255);
            var hoverBrush = new SolidColorBrush(hoverColor);
            hoverBrush.Freeze();
            _cachedHoverBrush = hoverBrush;

            var pressedColor = isDark ? Color.FromArgb(12, 255, 255, 255) : Color.FromArgb(125, 255, 255, 255);
            var pressedBrush = new SolidColorBrush(pressedColor);
            pressedBrush.Freeze();
            _cachedPressedBrush = pressedBrush;

            // High-contrast legible text tokens
            var primTextColor = isDark ? Color.FromRgb(255, 255, 255) : Color.FromRgb(26, 26, 26);
            var primBrush = new SolidColorBrush(primTextColor);
            primBrush.Freeze();
            _cachedPrimaryTextBrush = primBrush;

            var secTextColor = isDark ? Color.FromArgb(200, 255, 255, 255) : Color.FromArgb(180, 0, 0, 0);
            var secBrush = new SolidColorBrush(secTextColor);
            secBrush.Freeze();
            _cachedSecondaryTextBrush = secBrush;

            // Subtle border token for Fluent cards and flyout containers
            var borderColor = isDark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(24, 0, 0, 0);
            var borderBrush = new SolidColorBrush(borderColor);
            borderBrush.Freeze();
            _cachedSubtleBorderBrush = borderBrush;

            // Card background token
            var cardBgColor = isDark ? Color.FromArgb(140, 38, 38, 38) : Color.FromArgb(160, 255, 255, 255);
            var cardBrush = new SolidColorBrush(cardBgColor);
            cardBrush.Freeze();
            _cachedCardBackgroundBrush = cardBrush;

            _colorsCached = true;
        }

        /// <summary>
        /// Gets the resolved Windows system accent color.
        /// </summary>
        public static Color GetAccentColor(bool isDark)
        {
            EnsureThemeColors(isDark);
            return _cachedAccentColor;
        }

        /// <summary>
        /// Gets a frozen <see cref="Brush"/> representing the active Windows system accent color.
        /// </summary>
        public static Brush GetAccentBrush(bool isDark)
        {
            EnsureThemeColors(isDark);
            return _cachedAccentBrush;
        }

        /// <summary>
        /// Gets a frozen <see cref="Brush"/> representing the native taskbar button hover highlight.
        /// </summary>
        public static Brush GetTaskbarButtonHoverBrush(bool isDark)
        {
            EnsureThemeColors(isDark);
            return _cachedHoverBrush;
        }

        /// <summary>
        /// Gets a frozen <see cref="Brush"/> representing the native taskbar button pressed highlight.
        /// </summary>
        public static Brush GetTaskbarButtonPressedBrush(bool isDark)
        {
            EnsureThemeColors(isDark);
            return _cachedPressedBrush;
        }

        /// <summary>
        /// Gets the primary text foreground brush for the active system theme.
        /// </summary>
        public static Brush GetPrimaryTextBrush(bool isDark)
        {
            EnsureThemeColors(isDark);
            return _cachedPrimaryTextBrush;
        }

        /// <summary>
        /// Gets the secondary muted text foreground brush for the active system theme.
        /// </summary>
        public static Brush GetSecondaryTextBrush(bool isDark)
        {
            EnsureThemeColors(isDark);
            return _cachedSecondaryTextBrush;
        }

        /// <summary>
        /// Gets the subtle border brush for cards and flyout containers matching Fluent style.
        /// </summary>
        public static Brush GetSubtleBorderBrush(bool isDark)
        {
            EnsureThemeColors(isDark);
            return _cachedSubtleBorderBrush;
        }

        /// <summary>
        /// Gets the subtle background brush for device cards inside the flyout popup.
        /// </summary>
        public static Brush GetCardBackgroundBrush(bool isDark)
        {
            EnsureThemeColors(isDark);
            return _cachedCardBackgroundBrush;
        }

        /// <summary>
        /// Resolves the Windows 10/11 system accent color by querying UXTheme Immersive Color API
        /// and falling back to the DWM registry configuration.
        /// </summary>
        /// <param name="isDark">Indicates whether the system is in dark theme.</param>
        /// <returns>The resolved system accent color or default Fluent blue fallback.</returns>
        private static Color QuerySystemAccentColor(bool isDark)
        {
            // 1. Try UXTheme ordinal #95 (GetImmersiveColorFromColorSetEx)
            try
            {
                uint colorSet = GetImmersiveUserColorSetPreference(false, false);
                // Color type index for SystemAccent is typically 0 or resolved via name
                uint colorRef = GetImmersiveColorFromColorSetEx(colorSet, 0, false, 0);
                if (colorRef != 0)
                {
                    byte r = (byte)(colorRef & 0xFF);
                    byte g = (byte)((colorRef >> 8) & 0xFF);
                    byte b = (byte)((colorRef >> 16) & 0xFF);
                    if (r != 0 || g != 0 || b != 0)
                    {
                        return Color.FromRgb(r, g, b);
                    }
                }
            }
            catch { }

            // 2. Try Windows DWM AccentColor registry key (0xAABBGGRR)
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("AccentColor");
                        if (val is int)
                        {
                            int intVal = (int)val;
                            if (intVal != 0)
                            {
                                uint dwordVal = (uint)intVal;
                                byte r = (byte)(dwordVal & 0xFF);
                                byte g = (byte)((dwordVal >> 8) & 0xFF);
                                byte b = (byte)((dwordVal >> 16) & 0xFF);
                                return Color.FromRgb(r, g, b);
                            }
                        }

                        object colVal = key.GetValue("ColorizationColor");
                        if (colVal is int)
                        {
                            int intCol = (int)colVal;
                            if (intCol != 0)
                            {
                                uint dwordCol = (uint)intCol; // 0xAARRGGBB
                                byte r = (byte)((dwordCol >> 16) & 0xFF);
                                byte g = (byte)((dwordCol >> 8) & 0xFF);
                                byte b = (byte)(dwordCol & 0xFF);
                                return Color.FromRgb(r, g, b);
                            }
                        }
                    }
                }
            }
            catch { }

            // 3. Fallback to default Windows 11 Fluent Blue accent
            return isDark ? Color.FromRgb(0, 120, 212) : Color.FromRgb(0, 103, 192);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Compositor & System Timer Optimization
        // ═══════════════════════════════════════════════════════════════════════

        private static bool _timerBoostActive = false;
        private static bool _dcompBoostFailed = false;

        /// <summary>
        /// Temporarily elevates DirectComposition and system multimedia timer resolution to 1ms
        /// during flyout animations and positioning passes to ensure 60/120+ FPS fluidity without stutter.
        /// </summary>
        /// <param name="enable"><c>true</c> to boost compositor timing; <c>false</c> to restore standard clock.</param>
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
