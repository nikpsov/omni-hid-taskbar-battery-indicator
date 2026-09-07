using System;
using System.Runtime.InteropServices;

namespace OmniHidTaskbar.UI
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Win32 Taskbar Shell & Window Geometry Helper
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Encapsulates native Win32 User32 and Shell APIs for querying taskbar geometry,
    /// tracking tray and start button boundaries, detecting fullscreen windows, and managing icon resources.
    /// Operates entirely via ultra-fast Win32 APIs without COM or UIAutomation overhead.
    /// </summary>
    internal static class TaskbarHelper
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Native Win32 P/Invoke Declarations
        // ═══════════════════════════════════════════════════════════════════════

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        /// <summary>
        /// Cross-architecture SetWindowLong wrapper targeting 32-bit and 64-bit systems.
        /// </summary>
        public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLong32(hWnd, nIndex, dwNewLong);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

        // ═══════════════════════════════════════════════════════════════════════
        // Win32 Structures & Constants
        // ═══════════════════════════════════════════════════════════════════════

        public const int GWLP_HWNDPARENT = -8;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint MONITOR_DEFAULTTONEAREST = 2;

        public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        public const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
        public const uint EVENT_OBJECT_SHOW = 0x8002;
        public const uint EVENT_OBJECT_HIDE = 0x8003;
        public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        public const uint WINEVENT_OUTOFCONTEXT = 0;

        /// <summary>
        /// Win32 rectangle structure defining physical screen boundaries in pixels.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            /// <summary>Gets the physical pixel width.</summary>
            public int Width { get { return Right - Left; } }

            /// <summary>Gets the physical pixel height.</summary>
            public int Height { get { return Bottom - Top; } }
        }

        /// <summary>
        /// Contains information about a display monitor, including monitor and work area boundaries.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Taskbar Bounds & Window Geometry Helpers
        // ═══════════════════════════════════════════════════════════════════════

        private static IntPtr _cachedTaskbarHwnd = IntPtr.Zero;
        private static IntPtr _cachedTrayNotifyHwnd = IntPtr.Zero;

        /// <summary>
        /// Queries the primary Windows taskbar window handle (<c>Shell_TrayWnd</c>).
        /// Caches the handle across queries to eliminate P/Invoke string marshaling allocations.
        /// </summary>
        /// <returns>Window handle to the taskbar, or <c>IntPtr.Zero</c> if not found.</returns>
        public static IntPtr GetTaskbarHandle()
        {
            if (!IsWindow(_cachedTaskbarHwnd))
            {
                _cachedTaskbarHwnd = FindWindow("Shell_TrayWnd", null);
            }
            return _cachedTaskbarHwnd;
        }

        /// <summary>
        /// Queries the notification tray container window handle (<c>TrayNotifyWnd</c>).
        /// Caches the handle across queries to eliminate P/Invoke string marshaling allocations.
        /// </summary>
        /// <param name="taskbarHwnd">Parent taskbar window handle.</param>
        /// <returns>Window handle to the tray area, or <c>IntPtr.Zero</c> if not found.</returns>
        public static IntPtr GetTrayNotifyHandle(IntPtr taskbarHwnd)
        {
            if (taskbarHwnd == IntPtr.Zero) return IntPtr.Zero;
            if (!IsWindow(_cachedTrayNotifyHwnd))
            {
                _cachedTrayNotifyHwnd = FindWindowEx(taskbarHwnd, IntPtr.Zero, "TrayNotifyWnd", null);
            }
            return _cachedTrayNotifyHwnd;
        }


        /// <summary>
        /// Queries the physical screen rectangle of the Windows System Tray (<c>TrayNotifyWnd</c>).
        /// </summary>
        /// <param name="taskbarHwnd">Parent taskbar window handle.</param>
        /// <param name="rect">Output rectangle in screen coordinates.</param>
        /// <returns><c>true</c> if successfully queried; otherwise <c>false</c>.</returns>
        public static bool GetTrayRect(IntPtr taskbarHwnd, out RECT rect)
        {
            rect = new RECT();
            IntPtr trayNotify = GetTrayNotifyHandle(taskbarHwnd);
            if (trayNotify != IntPtr.Zero && GetWindowRect(trayNotify, out rect) && rect.Left > 0)
            {
                return true;
            }
            return false;
        }


        private static IntPtr _cachedDesktopHwnd = IntPtr.Zero;
        private static IntPtr _cachedShellHwnd = IntPtr.Zero;

        /// <summary>
        /// Determines whether the active foreground window is running in true or borderless fullscreen mode
        /// (e.g. immersive 3D games, media players, F11 browser), indicating that the taskbar overlay should be concealed.
        /// Caches static desktop/shell window handles to eliminate P/Invoke allocations, with instant zero-allocation geometric testing.
        /// </summary>
        /// <param name="ignoredHwnd1">First window handle to ignore (e.g., overlay window itself).</param>
        /// <param name="ignoredHwnd2">Second window handle to ignore (e.g., flyout window).</param>
        /// <returns><c>true</c> if a fullscreen application occupies the display; otherwise, <c>false</c>.</returns>
        public static bool IsForegroundFullscreen(IntPtr ignoredHwnd1, IntPtr ignoredHwnd2)
        {
            IntPtr fgWnd = GetForegroundWindow();
            if (fgWnd == IntPtr.Zero) return false;

            if (fgWnd == ignoredHwnd1 || (ignoredHwnd2 != IntPtr.Zero && fgWnd == ignoredHwnd2))
                return false;

            if (!IsWindow(_cachedDesktopHwnd)) _cachedDesktopHwnd = FindWindow("Progman", null);
            if (!IsWindow(_cachedShellHwnd)) _cachedShellHwnd = FindWindow("WorkerW", null);
            if (!IsWindow(_cachedTaskbarHwnd)) _cachedTaskbarHwnd = FindWindow("Shell_TrayWnd", null);

            if (fgWnd == _cachedDesktopHwnd || fgWnd == _cachedShellHwnd || fgWnd == _cachedTaskbarHwnd)
                return false;

            // Check if taskbar itself is hidden (e.g. auto-hide or exclusive fullscreen)
            if (_cachedTaskbarHwnd != IntPtr.Zero && !IsWindowVisible(_cachedTaskbarHwnd))
            {
                return true;
            }

            RECT appBounds;
            if (!GetWindowRect(fgWnd, out appBounds))
                return false;

            IntPtr hMonitor = MonitorFromWindow(fgWnd, MONITOR_DEFAULTTONEAREST);
            if (hMonitor == IntPtr.Zero)
                return false;

            MONITORINFO mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(mi);
            if (GetMonitorInfo(hMonitor, ref mi))
            {
                // Fullscreen if foreground window covers or exceeds the monitor physical display (with 2px tolerance for DPI rounding)
                return (appBounds.Left <= mi.rcMonitor.Left + 2 &&
                        appBounds.Top <= mi.rcMonitor.Top + 2 &&
                        appBounds.Right >= mi.rcMonitor.Right - 2 &&
                        appBounds.Bottom >= mi.rcMonitor.Bottom - 2);
            }

            return false;
        }

        /// <summary>
        /// Forces garbage collection and trims unreferenced pages from the application's physical RAM working set.
        /// Flushes one-time JIT-compiled pages and WPF Direct3D startup allocations back to the Windows OS.
        /// </summary>
        public static void TrimProcessMemory()
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Forced);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced);

                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    IntPtr hProc = System.Diagnostics.Process.GetCurrentProcess().Handle;
                    SetProcessWorkingSetSize(hProc, new IntPtr(-1), new IntPtr(-1));
                }
            }
            catch { }
        }
    }
}
