using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OmniHidTaskbar.Core;
using OmniHid.Core;
using OmniHid.Core.Abstractions;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Point = System.Windows.Point;

namespace OmniHidTaskbar.UI
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Taskbar Overlay Widget Window
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Frameless overlay window docked dynamically beside the Windows system tray.
    /// Matches Windows 11 SystemTray native visual styling, tracks tray and taskbar running tab boundaries,
    /// dynamically avoids collisions with expanding application tabs, and hosts the interactive Fluent Flyout.
    /// </summary>
    public class OverlayWindow : Window
    {
        // ═══════════════════════════════════════════════════════════════════════
        // State Fields & UI Elements
        // ═══════════════════════════════════════════════════════════════════════

        private DispatcherTimer _positionTimer;
        private readonly StackPanel _mainStack;
        private readonly Border _containerBorder;

        private TaskbarHelper.WinEventDelegate _winEventProc;
        private IntPtr _hTaskbarHook;
        private IntPtr _hGlobalHook;
        private IntPtr _hGlobalSizeHook;
        private FlyoutWindow _flyout;

        private System.Windows.Forms.NotifyIcon _notifyIcon;
        private System.Windows.Interop.HwndSource _hwndSource;
        private bool _hideWhenDisconnected = true;
        private bool _shouldHideOverlay = false;
        private bool? _lastIsSystemLight = null;

        private int _displayStyle = 0; // 0 = Percent, 1 = Icon
        private bool _isCompactMode = false;

        private List<TaskbarDeviceState> _latestDevices = new List<TaskbarDeviceState>();
        private readonly Dictionary<string, TaskbarDeviceState> _allKnownDevices = new Dictionary<string, TaskbarDeviceState>(StringComparer.OrdinalIgnoreCase);
        private readonly List<TaskbarDeviceWidget> _widgetCache = new List<TaskbarDeviceWidget>();
        private readonly HashSet<string> _warnedLowBatteryDeviceIds = new HashSet<string>();

        private OmniManager _omniManager;
        private string _lastRenderSignature = null;
        private string _lastTrayIconSignature = null;
        private readonly Dictionary<string, System.Drawing.Icon> _trayIconCache = new Dictionary<string, System.Drawing.Icon>(StringComparer.Ordinal);
        private bool _isBackgroundMode = false;
        private bool _isSessionLocked = false;
        private Visibility _lastVisibility = Visibility.Visible;
        private IntPtr _overlayHwnd = IntPtr.Zero;
        private double _dpiX = 1.0;
        private double _dpiY = 1.0;
        private bool _dpiInitialized = false;

        private static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        private static readonly FontFamily TextFont = new FontFamily("Segoe UI Variable Display, Segoe UI");
        private static readonly FontFamily CrossFont = new FontFamily("Segoe UI, Arial, sans-serif");
        private static readonly Brush ChargingGreenBrush = CreateFrozenBrush(Color.FromRgb(30, 215, 96));
        private static readonly Brush CriticalRedBrush = CreateFrozenBrush(Color.FromRgb(225, 40, 40));
        private static readonly Brush DisconnectedCrossBrush = CreateFrozenBrush(Color.FromRgb(225, 45, 45));

        /// <summary>
        /// Gets whether the current Windows system personalization preference is set to dark theme.
        /// </summary>
        public bool IsDarkTheme
        {
            get
            {
                try
                {
                    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    {
                        if (key != null)
                        {
                            object val = key.GetValue("SystemUsesLightTheme");
                            return val == null || (int)val == 0;
                        }
                    }
                }
                catch { }
                return true;
            }
        }

        /// <summary>
        /// Gets the latest raw telemetry state snapshot for all enumerated peripheral devices in user-configured order.
        /// </summary>
        public List<TaskbarDeviceState> LatestDevices { get { return SortDevicesByCustomOrder(_latestDevices); } }

        /// <summary>
        /// Gets the filtered list of peripheral devices that are configured as visible by user settings in custom order.
        /// </summary>
        public List<TaskbarDeviceState> VisibleDevices { get { return GetVisibleDevices(SortDevicesByCustomOrder(_latestDevices)); } }

        // ═══════════════════════════════════════════════════════════════════════
        // Constructor & Initialization
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Initializes a new instance of <see cref="OverlayWindow"/>, docks to taskbar,
        /// and initializes OmniHID telemetry monitoring.
        /// </summary>
        public OverlayWindow()
        {
            LoadSettings();

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = HitTestTransparentBrush;
            Topmost = true;
            ShowInTaskbar = false;

            // Height matches Windows 11 SystemTray.NormalButton (40 DIPs inside a 48px taskbar with 4px vertical margin)
            Height = 40;
            Width = 90;

            _mainStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = HitTestTransparentBrush,
                Margin = new Thickness(6, 0, 6, 0),
                IsHitTestVisible = false
            };

            // Container border styled according to Windows 11 SystemTray button design tokens
            _containerBorder = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = HitTestTransparentBrush,
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(2, 0, 2, 0),
                Child = _mainStack
            };

            Content = _containerBorder;

            // Fluent hover & pressed states matching Windows 11 taskbar native system tray buttons
            this.MouseEnter += (s, e) => ApplyContainerBackground(isHovered: true, isPressed: Mouse.LeftButton == MouseButtonState.Pressed);
            this.MouseLeave += (s, e) =>
            {
                ApplyContainerBackground(isHovered: false, isPressed: false);
                ScheduleHoverExitCleanup();
            };
            this.PreviewMouseLeftButtonDown += (s, e) => ApplyContainerBackground(isHovered: true, isPressed: true);
            this.PreviewMouseLeftButtonUp += (s, e) => ApplyContainerBackground(isHovered: this.IsMouseOver, isPressed: false);

            this.MouseLeftButtonUp += (s, e) => ShowFlyout();
            this.MouseRightButtonUp += (s, e) => ShowContextMenu();

            ApplyDevicesState(_latestDevices);

            InitNotifyIcon();

            // Subscribe to OmniHID core telemetry engine (disable redundant background watcher thread as host window processes WM_DEVICECHANGE)
            _omniManager = new OmniManager(enableInternalWatcher: false);
            _omniManager.RegisteredOnly = true;
            _omniManager.DevicesUpdated += OnOmniDevicesUpdated;
            int pollSec = SettingsManager.Instance.Current.PollIntervalSeconds;
            _omniManager.StartMonitoring(pollSec > 0 ? pollSec * 1000 : 15000);

            this.SourceInitialized += (s, e) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                IntPtr taskbar = TaskbarHelper.GetTaskbarHandle();
                if (taskbar != IntPtr.Zero)
                {
                    TaskbarHelper.SetWindowLongPtr(hwnd, TaskbarHelper.GWLP_HWNDPARENT, taskbar);
                }

                if (hwnd != IntPtr.Zero)
                {
                    DwmHelper.EnableWindowTransitions(hwnd, false);
                    DwmHelper.SetDarkMode(hwnd, IsDarkTheme);
                    _hwndSource = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
                    if (_hwndSource != null)
                    {
                        _hwndSource.AddHook(WndProc);
                    }
                }

                Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;

                _overlayHwnd = hwnd;
                SetupHooks();
                UpdatePosition();

                // Heartbeat timer (1500ms at Background priority) for geometry and fullscreen verification.
                // WinEvents handle real-time movements and active window switches with zero overhead.
                _positionTimer = new DispatcherTimer(DispatcherPriority.Background, this.Dispatcher) { Interval = TimeSpan.FromMilliseconds(1500) };
                _positionTimer.Tick += (ts, te) => UpdatePosition();
                _positionTimer.Start();

                // Post-startup memory optimization timer: flush one-time JIT allocations and WPF DirectX buffers
                var memTimer = new DispatcherTimer(DispatcherPriority.Background, this.Dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(1500)
                };
                memTimer.Tick += (ms, me) =>
                {
                    memTimer.Stop();
                    TaskbarHelper.TrimProcessMemory();
                    Logger.Log("Startup working set trimmed successfully");
                };
                memTimer.Start();
            };

            this.Closed += (s, e) =>
            {
                Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
                if (_hoverExitTrimTimer != null) { _hoverExitTrimTimer.Stop(); _hoverExitTrimTimer = null; }
                if (_fullscreenExitTrimTimer != null) { _fullscreenExitTrimTimer.Stop(); _fullscreenExitTrimTimer = null; }
                if (_omniManager != null) { _omniManager.Dispose(); _omniManager = null; }
                if (_positionTimer != null) _positionTimer.Stop();
                if (_hwndSource != null)
                {
                    _hwndSource.RemoveHook(WndProc);
                    _hwndSource = null;
                }
                if (_hTaskbarHook != IntPtr.Zero)
                {
                    TaskbarHelper.UnhookWinEvent(_hTaskbarHook);
                    _hTaskbarHook = IntPtr.Zero;
                }
                if (_hGlobalHook != IntPtr.Zero)
                {
                    TaskbarHelper.UnhookWinEvent(_hGlobalHook);
                    _hGlobalHook = IntPtr.Zero;
                }
                if (_hGlobalSizeHook != IntPtr.Zero)
                {
                    TaskbarHelper.UnhookWinEvent(_hGlobalSizeHook);
                    _hGlobalSizeHook = IntPtr.Zero;
                }
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }
                foreach (var ic in _trayIconCache.Values)
                {
                    try { ic.Dispose(); } catch { }
                }
                _trayIconCache.Clear();
                if (_flyout != null)
                {
                    _flyout.Close();
                    _flyout = null;
                }
            };
        }

        private static readonly Brush HitTestTransparentBrush = CreateFrozenHitTestBrush();

        private static Brush CreateFrozenHitTestBrush()
        {
            var brush = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            brush.Freeze();
            return brush;
        }

        private static Brush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private int _currentHoverState = -1;
        private DispatcherTimer _hoverExitTrimTimer = null;

        /// <summary>
        /// Applies theme-accurate Windows 11 Fluent hover/pressed translucent pills to the container border.
        /// Guards against redundant brush re-assignments and visual invalidations.
        /// </summary>
        /// <param name="isHovered"><c>true</c> if mouse pointer is over the widget; otherwise <c>false</c>.</param>
        /// <param name="isPressed"><c>true</c> if primary mouse button is pressed over the widget; otherwise <c>false</c>.</param>
        private void ApplyContainerBackground(bool isHovered, bool isPressed)
        {
            int newState = isPressed ? 2 : (isHovered ? 1 : 0);
            if (_currentHoverState == newState) return;
            _currentHoverState = newState;

            bool isDark = !(_lastIsSystemLight.GetValueOrDefault(!IsDarkTheme));
            if (newState == 0)
            {
                _containerBorder.Background = HitTestTransparentBrush;
            }
            else if (newState == 2)
            {
                _containerBorder.Background = DwmHelper.GetTaskbarButtonPressedBrush(isDark);
            }
            else
            {
                _containerBorder.Background = DwmHelper.GetTaskbarButtonHoverBrush(isDark);
            }
        }

        /// <summary>
        /// Schedules a one-off delayed memory trimming pass after the mouse pointer leaves the widget.
        /// Flushes any transient WPF input packets and compositing buffers back to the OS baseline.
        /// </summary>
        private void ScheduleHoverExitCleanup()
        {
            if (_hoverExitTrimTimer == null)
            {
                _hoverExitTrimTimer = new DispatcherTimer(DispatcherPriority.Background, this.Dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(800)
                };
                _hoverExitTrimTimer.Tick += (s, e) =>
                {
                    _hoverExitTrimTimer.Stop();
                    if (!this.IsMouseOver && (_flyout == null || !_flyout.IsVisible) && !TaskbarContextMenu.IsOpen)
                    {
                        TaskbarHelper.TrimProcessMemory();
                    }
                };
            }
            _hoverExitTrimTimer.Stop();
            _hoverExitTrimTimer.Start();
        }

        private DispatcherTimer _fullscreenExitTrimTimer = null;

        /// <summary>
        /// Schedules a delayed memory trim after returning to desktop from fullscreen mode.
        /// Flushes any transient DirectX presentation surfaces and telemetry scan allocations back to the OS baseline.
        /// </summary>
        private void ScheduleFullscreenExitTrim()
        {
            if (_fullscreenExitTrimTimer == null)
            {
                _fullscreenExitTrimTimer = new DispatcherTimer(DispatcherPriority.Background, this.Dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(1500)
                };
                _fullscreenExitTrimTimer.Tick += (s, e) =>
                {
                    _fullscreenExitTrimTimer.Stop();
                    TaskbarHelper.TrimProcessMemory();
                };
            }
            _fullscreenExitTrimTimer.Stop();
            _fullscreenExitTrimTimer.Start();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // OmniHID Telemetry Event Subscription & State Handling
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Compares newly acquired peripheral telemetry with current device state snapshots
        /// to determine whether any visual or telemetry property actually changed.
        /// Performs a zero-allocation check before dispatching work to the WPF UI thread.
        /// </summary>
        /// <param name="devices">Newly acquired peripheral device list.</param>
        /// <returns><c>true</c> if any device was added, removed, or changed telemetry; otherwise <c>false</c>.</returns>
        private bool HasDevicesStateChanged(IReadOnlyList<IOmniDevice> devices)
        {
            var prev = _latestDevices;
            if (devices == null && (prev == null || prev.Count == 0)) return false;
            if (devices == null || prev == null) return true;
            if (devices.Count != prev.Count) return true;

            for (int i = 0; i < devices.Count; i++)
            {
                var d = devices[i];
                var p = prev[i];
                if (d == null && p == null) continue;
                if (d == null || p == null) return true;

                if (!string.Equals(d.Id, p.Id, StringComparison.Ordinal)) return true;
                if (d.IsConnected != p.IsConnected) return true;

                var tel = d.Telemetry;
                int level = tel != null ? tel.LevelPercent : -1;
                bool charging = tel != null && tel.IsCharging;
                bool wired = d.IsWired;
                if (level != p.BatteryPercent || charging != p.IsCharging || wired != p.IsWired)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Callback executed when the OmniHID engine broadcasts updated peripheral battery telemetry.
        /// Dispatches snapshot mapping and UI rendering to the WPF UI thread only if device states changed.
        /// </summary>
        /// <param name="devices">Read-only list of active OmniHID peripheral device abstractions.</param>
        private void OnOmniDevicesUpdated(IReadOnlyList<IOmniDevice> devices)
        {
            if (!HasDevicesStateChanged(devices))
            {
                return;
            }

            Logger.Log(string.Format("OmniHID update received: {0} device(s)", devices != null ? devices.Count : 0));
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                _latestDevices = devices != null ? devices.Select(TaskbarDeviceState.FromOmniDevice).ToList() : new List<TaskbarDeviceState>();
                if (_latestDevices != null)
                {
                    foreach (var d in _latestDevices)
                    {
                        if (d != null)
                        {
                            string key = !string.IsNullOrEmpty(d.Id) ? d.Id : d.Name;
                            if (!string.IsNullOrEmpty(key))
                            {
                                _allKnownDevices[key] = d;
                            }
                        }
                    }
                }
                ApplyDevicesState(_latestDevices);
            }));
        }

        /// <summary>
        /// Forces a re-render of current peripheral devices and refreshes the tray tooltip.
        /// </summary>
        public void RefreshWidgetState()
        {
            LoadSettings();
            ApplyDevicesState(_latestDevices);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Device Ordering & Visibility Filtering Subsystem
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Sorts an input list of peripheral device snapshots according to the user-defined device order.
        /// </summary>
        /// <param name="devices">Source peripheral devices.</param>
        /// <returns>Devices ordered according to settings.</returns>
        public static List<TaskbarDeviceState> SortDevicesByCustomOrder(List<TaskbarDeviceState> devices)
        {
            if (devices == null || devices.Count <= 1) return devices ?? new List<TaskbarDeviceState>();
            var order = SettingsManager.Instance.Current.DeviceOrder;
            if (order == null || order.Count == 0) return devices;

            return devices.OrderBy(d =>
            {
                if (d == null) return int.MaxValue;
                int idx = -1;
                if (!string.IsNullOrEmpty(d.Id))
                {
                    idx = order.FindIndex(o => string.Equals(o, d.Id, StringComparison.OrdinalIgnoreCase));
                }
                if (idx < 0 && !string.IsNullOrEmpty(d.Name))
                {
                    idx = order.FindIndex(o => string.Equals(o, d.Name, StringComparison.OrdinalIgnoreCase));
                }
                return idx >= 0 ? idx : int.MaxValue;
            }).ToList();
        }

        /// <summary>
        /// Updates the persistent user-defined device display ordering and refreshes the taskbar widgets.
        /// </summary>
        /// <param name="newOrder">Updated collection of device identifiers.</param>
        public void UpdateDeviceOrder(List<string> newOrder)
        {
            if (newOrder != null)
            {
                SettingsManager.Instance.Current.DeviceOrder = new List<string>(newOrder);
                SettingsManager.Instance.Save();
                ApplyDevicesState(_latestDevices);
            }
        }

        /// <summary>
        /// Updates or clears a persistent custom alias name for a peripheral device.
        /// </summary>
        /// <param name="deviceId">Target device identifier.</param>
        /// <param name="customName">Custom alias string or null/empty to revert to factory model name.</param>
        public void SetDeviceCustomName(string deviceId, string customName)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            var names = SettingsManager.Instance.Current.CustomDeviceNames;
            if (names == null)
            {
                names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                SettingsManager.Instance.Current.CustomDeviceNames = names;
            }

            if (string.IsNullOrWhiteSpace(customName))
            {
                names.Remove(deviceId);
            }
            else
            {
                names[deviceId] = customName.Trim();
            }

            if (_latestDevices != null)
            {
                foreach (var d in _latestDevices)
                {
                    if (d != null && string.Equals(d.Id, deviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        d.CustomName = string.IsNullOrWhiteSpace(customName) ? null : customName.Trim();
                    }
                }
            }

            TaskbarDeviceState known;
            if (_allKnownDevices.TryGetValue(deviceId, out known))
            {
                known.CustomName = string.IsNullOrWhiteSpace(customName) ? null : customName.Trim();
            }

            SettingsManager.Instance.Save();
            ApplyDevicesState(_latestDevices);
        }

        /// <summary>
        /// Determines whether a device is currently marked as hidden in user settings.
        /// </summary>
        /// <param name="nameOrId">Device model name or identifier.</param>
        /// <returns><c>true</c> if hidden; otherwise <c>false</c>.</returns>
        public bool IsDeviceHidden(string nameOrId)
        {
            if (string.IsNullOrEmpty(nameOrId)) return false;
            var hidden = SettingsManager.Instance.Current.HiddenDevices;
            if (hidden == null || hidden.Count == 0) return false;
            foreach (var h in hidden)
            {
                if (string.Equals(h.Trim(), nameOrId.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Evaluates whether a peripheral snapshot should be displayed in the taskbar and flyout.
        /// </summary>
        /// <param name="dev">Peripheral device state model.</param>
        /// <returns><c>true</c> if device should be visible; otherwise <c>false</c>.</returns>
        public bool IsDeviceVisible(TaskbarDeviceState dev)
        {
            if (dev == null) return false;
            var hidden = SettingsManager.Instance.Current.HiddenDevices;
            if (hidden == null || hidden.Count == 0) return true;
            foreach (var h in hidden)
            {
                if (!string.IsNullOrEmpty(dev.Id) && string.Equals(h.Trim(), dev.Id.Trim(), StringComparison.OrdinalIgnoreCase))
                    return false;
                if (!string.IsNullOrEmpty(dev.Name) && string.Equals(h.Trim(), dev.Name.Trim(), StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Filters an input list of peripheral states to include only user-visible devices.
        /// </summary>
        /// <param name="devices">Source peripheral state list.</param>
        /// <returns>Filtered list of visible peripheral devices.</returns>
        public List<TaskbarDeviceState> GetVisibleDevices(List<TaskbarDeviceState> devices)
        {
            if (devices == null) return new List<TaskbarDeviceState>();
            return devices.Where(IsDeviceVisible).ToList();
        }

        /// <summary>
        /// Toggles visibility for a specific device identifier or model name and persists the update to settings.
        /// </summary>
        /// <param name="idOrName">Peripheral unique identifier or model name.</param>
        /// <param name="isVisible"><c>true</c> to show; <c>false</c> to hide.</param>
        public void SetDeviceVisibility(string idOrName, bool isVisible)
        {
            if (string.IsNullOrEmpty(idOrName)) return;
            var hidden = SettingsManager.Instance.Current.HiddenDevices;
            if (hidden == null)
            {
                hidden = new List<string>();
                SettingsManager.Instance.Current.HiddenDevices = hidden;
            }

            if (isVisible)
            {
                hidden.RemoveAll(x => string.Equals(x.Trim(), idOrName.Trim(), StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                bool alreadyExists = hidden.Any(x => string.Equals(x.Trim(), idOrName.Trim(), StringComparison.OrdinalIgnoreCase));
                if (!alreadyExists)
                {
                    hidden.Add(idOrName.Trim());
                }
            }

            SettingsManager.Instance.Save();
            ApplyDevicesState(_latestDevices);

            if (_flyout != null && _flyout.IsVisible)
            {
                _flyout.UpdateData(GetVisibleDevices(SortDevicesByCustomOrder(_latestDevices)));
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Widget Rendering & State Application
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Rebuilds the visual layout of the overlay based on the provided list of device states,
        /// handling multi-device spacing, offline placeholders, low-battery toast alerts, and flyout sync.
        /// </summary>
        /// <param name="devices">The latest snapshot of device states received from the telemetry manager.</param>
        private void ApplyDevicesState(List<TaskbarDeviceState> devices)
        {
            try
            {
                bool isLight = !IsDarkTheme;
                var themeBrush = DwmHelper.GetPrimaryTextBrush(!isLight);
                bool isTrayOnly = SettingsManager.Instance.Current.DisplayMode == 1;

                var orderedDevices = SortDevicesByCustomOrder(devices);
                var visibleDevices = GetVisibleDevices(orderedDevices);
                var targetDevices = _hideWhenDisconnected
                    ? visibleDevices.Where(d => d.IsConnected && d.BatteryPercent >= 0).ToList()
                    : visibleDevices;

                var sb = new System.Text.StringBuilder();
                sb.AppendFormat("{0}:{1}:{2}:{3}:", isLight, isTrayOnly, _displayStyle, _isCompactMode);
                foreach (var d in targetDevices)
                {
                    sb.AppendFormat("{0}_{1}_{2}_{3}_{4}_{5};", d.Id, d.IsConnected, d.BatteryPercent, d.IsCharging, d.DisplayName, d.IconGlyph);
                }
                string currentSignature = sb.ToString();

                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                IntPtr flyoutHwnd = _flyout != null ? new System.Windows.Interop.WindowInteropHelper(_flyout).Handle : IntPtr.Zero;
                bool isFullscreen = TaskbarHelper.IsForegroundFullscreen(hwnd, flyoutHwnd);

                // If device state and visual configuration have not changed, skip rebuilding the visual tree
                if (string.Equals(currentSignature, _lastRenderSignature, StringComparison.Ordinal) && _mainStack.Children.Count > 0)
                {
                    this.Visibility = (isTrayOnly || isFullscreen || (_shouldHideOverlay && _hideWhenDisconnected))
                        ? Visibility.Hidden : Visibility.Visible;
                    UpdateTrayTooltip(targetDevices);
                    UpdateDynamicTrayIcon(targetDevices, isLight);
                    if (_flyout != null && _flyout.IsVisible)
                    {
                        _flyout.UpdateData(visibleDevices);
                    }
                    return;
                }

                // Check if existing widgets can be updated in-place without rebuilding WPF visual tree
                _widgetCache.Clear();
                for (int i = 0; i < _mainStack.Children.Count; i++)
                {
                    var w = _mainStack.Children[i] as TaskbarDeviceWidget;
                    if (w != null) _widgetCache.Add(w);
                }

                bool canUpdateInPlace = (targetDevices.Count > 0 &&
                                         _widgetCache.Count == targetDevices.Count &&
                                         !_shouldHideOverlay);

                if (canUpdateInPlace)
                {
                    for (int i = 0; i < targetDevices.Count; i++)
                    {
                        if (!string.Equals(_widgetCache[i].DeviceId, targetDevices[i].Id, StringComparison.Ordinal) ||
                            _widgetCache[i].CurrentDisplayStyle != _displayStyle ||
                            _widgetCache[i].CurrentIsCompactMode != _isCompactMode)
                        {
                            canUpdateInPlace = false;
                            break;
                        }
                    }
                }

                if (canUpdateInPlace)
                {
                    _lastRenderSignature = currentSignature;
                    for (int i = 0; i < targetDevices.Count; i++)
                    {
                        var dev = targetDevices[i];
                        _widgetCache[i].Update(dev, themeBrush, isLight, _displayStyle, _isCompactMode);

                        // Low battery toast warning (only for connected devices)
                        if (dev.IsConnected && dev.BatteryPercent >= 0 && dev.BatteryPercent <= 20 && !dev.IsCharging)
                        {
                            if (!_warnedLowBatteryDeviceIds.Contains(dev.Id))
                            {
                                ShowLowBatteryToast(dev);
                                _warnedLowBatteryDeviceIds.Add(dev.Id);
                            }
                        }
                        else
                        {
                            _warnedLowBatteryDeviceIds.Remove(dev.Id);
                        }
                    }

                    // Compute dynamic overlay width based on item count and compact/full state
                    int itemSlotWidth = _isCompactMode ? 44 : (_displayStyle == 0 ? 58 : 46);
                    this.Width = Math.Max(50, targetDevices.Count * itemSlotWidth + 16);
                    this.Visibility = (isTrayOnly || isFullscreen)
                        ? Visibility.Hidden : Visibility.Visible;

                    UpdateTrayTooltip(targetDevices);
                    UpdateDynamicTrayIcon(targetDevices, isLight);
                    if (_flyout != null && _flyout.IsVisible)
                    {
                        _flyout.UpdateData(visibleDevices);
                    }
                    UpdatePosition();
                    return;
                }

                _lastRenderSignature = currentSignature;
                _mainStack.Children.Clear();

                if (targetDevices.Count > 0)
                {
                    _shouldHideOverlay = false;

                    for (int i = 0; i < targetDevices.Count; i++)
                    {
                        var dev = targetDevices[i];
                        if (i > 0)
                        {
                            // Spacing between multiple devices: tighter in compact mode
                            var spacer = new Border
                            {
                                Width = _isCompactMode ? 5 : 8,
                                Background = Brushes.Transparent
                            };
                            _mainStack.Children.Add(spacer);
                        }

                        var devWidget = new TaskbarDeviceWidget(dev, themeBrush, isLight, _displayStyle, _isCompactMode);
                        _mainStack.Children.Add(devWidget);

                        // Low battery toast warning (only for connected devices)
                        if (dev.IsConnected && dev.BatteryPercent >= 0 && dev.BatteryPercent <= 20 && !dev.IsCharging)
                        {
                            if (!_warnedLowBatteryDeviceIds.Contains(dev.Id))
                            {
                                ShowLowBatteryToast(dev);
                                _warnedLowBatteryDeviceIds.Add(dev.Id);
                            }
                        }
                        else
                        {
                            _warnedLowBatteryDeviceIds.Remove(dev.Id);
                        }
                    }

                    // Compute dynamic overlay width based on item count and compact/full state
                    int itemSlotWidth = _isCompactMode ? 44 : (_displayStyle == 0 ? 58 : 46);
                    this.Width = Math.Max(50, targetDevices.Count * itemSlotWidth + 16);
                    this.Visibility = (isTrayOnly || isFullscreen)
                        ? Visibility.Hidden : Visibility.Visible;

                    UpdateTrayTooltip(targetDevices);
                    UpdateDynamicTrayIcon(targetDevices, isLight);
                }
                else
                {
                    _shouldHideOverlay = true;

                    // When no devices are online, display disconnected placeholder
                    var disconnectedGrid = new Grid
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(4, 0, 4, 0)
                    };

                    var iconText = new TextBlock
                    {
                        Text = "\uE772", // Generic Hardware icon
                        FontFamily = IconFont,
                        FontSize = 16,
                        Foreground = themeBrush,
                        Opacity = 0.75,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };

                    var crossText = new TextBlock
                    {
                        Text = "\u2715",
                        FontFamily = CrossFont,
                        FontSize = 8.5,
                        FontWeight = FontWeights.Bold,
                        Foreground = DisconnectedCrossBrush,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(0, 0, -3, -2)
                    };

                    disconnectedGrid.Children.Add(iconText);
                    disconnectedGrid.Children.Add(crossText);
                    _mainStack.Children.Add(disconnectedGrid);

                    this.Width = 44;
                    this.Visibility = (isTrayOnly || _hideWhenDisconnected || isFullscreen) ? Visibility.Hidden : Visibility.Visible;

                    if (_notifyIcon != null)
                    {
                        _notifyIcon.Text = "Device Battery: Disconnected";
                    }
                    UpdateDynamicTrayIcon(null, isLight);
                }

                if (_flyout != null && _flyout.IsVisible)
                {
                    _flyout.UpdateData(visibleDevices);
                }

                UpdatePosition();
            }
            catch (Exception ex)
            {
                Logger.Log("Error in ApplyDevicesState: " + ex.Message);
            }
        }

        /// <summary>
        /// Stateful reusable WPF widget representing a single device on the taskbar.
        /// Supports in-place updates to avoid rebuilding visual elements on every telemetry tick.
        /// </summary>
        private class TaskbarDeviceWidget : StackPanel
        {
            public string DeviceId { get; private set; }
            public int CurrentDisplayStyle { get; private set; }
            public bool CurrentIsCompactMode { get; private set; }

            private readonly TextBlock _iconBlock;
            private TextBlock _battText;
            private TextBlock _boltBlock;
            private Grid _battIconGrid;

            public TaskbarDeviceWidget(TaskbarDeviceState dev, Brush themeBrush, bool isLight, int displayStyle, bool isCompact)
            {
                Orientation = Orientation.Horizontal;
                VerticalAlignment = VerticalAlignment.Center;
                DeviceId = dev.Id;
                CurrentDisplayStyle = displayStyle;
                CurrentIsCompactMode = isCompact;

                _iconBlock = new TextBlock
                {
                    Text = !string.IsNullOrEmpty(dev.IconGlyph) ? dev.IconGlyph : dev.GetDefaultIconGlyph(),
                    FontFamily = IconFont,
                    FontSize = isCompact ? 13.5 : 15,
                    Foreground = themeBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, isCompact ? 1 : 3, 0)
                };
                Children.Add(_iconBlock);

                if (displayStyle == 0)
                {
                    string percentText = (dev.IsConnected && dev.BatteryPercent >= 0) ? (dev.BatteryPercent + "%") : "--%";
                    _battText = new TextBlock
                    {
                        FontFamily = TextFont,
                        FontSize = isCompact ? 11.5 : 12.5,
                        FontWeight = FontWeights.Normal,
                        Foreground = themeBrush,
                        VerticalAlignment = VerticalAlignment.Center,
                        Text = percentText,
                        Margin = new Thickness(0, 0, 2, 0)
                    };
                    Children.Add(_battText);

                    _boltBlock = new TextBlock
                    {
                        Text = "\uE945",
                        FontFamily = IconFont,
                        FontSize = isCompact ? 10 : 11,
                        FontWeight = FontWeights.Bold,
                        Foreground = ChargingGreenBrush,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 1, 0),
                        Visibility = (dev.IsConnected && dev.IsCharging) ? Visibility.Visible : Visibility.Collapsed
                    };
                    Children.Add(_boltBlock);
                }
                else
                {
                    _battIconGrid = new Grid
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 2, 0)
                    };
                    Children.Add(_battIconGrid);
                    UpdateBatteryIconGlyphs(dev, themeBrush);
                }

                Opacity = (dev.IsConnected && dev.BatteryPercent >= 0) ? 1.0 : 0.65;
            }

            public void Update(TaskbarDeviceState dev, Brush themeBrush, bool isLight, int displayStyle, bool isCompact)
            {
                DeviceId = dev.Id;
                Opacity = (dev.IsConnected && dev.BatteryPercent >= 0) ? 1.0 : 0.65;

                string glyph = !string.IsNullOrEmpty(dev.IconGlyph) ? dev.IconGlyph : dev.GetDefaultIconGlyph();
                if (!string.Equals(_iconBlock.Text, glyph, StringComparison.Ordinal))
                {
                    _iconBlock.Text = glyph;
                }
                _iconBlock.Foreground = themeBrush;

                if (displayStyle == 0 && _battText != null)
                {
                    string percentText = (dev.IsConnected && dev.BatteryPercent >= 0) ? (dev.BatteryPercent + "%") : "--%";
                    if (!string.Equals(_battText.Text, percentText, StringComparison.Ordinal))
                    {
                        _battText.Text = percentText;
                    }
                    _battText.Foreground = themeBrush;

                    if (_boltBlock != null)
                    {
                        var boltVis = (dev.IsConnected && dev.IsCharging) ? Visibility.Visible : Visibility.Collapsed;
                        if (_boltBlock.Visibility != boltVis)
                        {
                            _boltBlock.Visibility = boltVis;
                        }
                    }
                }
                else if (displayStyle != 0 && _battIconGrid != null)
                {
                    UpdateBatteryIconGlyphs(dev, themeBrush);
                }
            }

            private void UpdateBatteryIconGlyphs(TaskbarDeviceState dev, Brush themeBrush)
            {
                _battIconGrid.Children.Clear();
                if (!dev.IsConnected || dev.BatteryPercent < 0)
                {
                    var disconnectedGlyph = new TextBlock
                    {
                        FontFamily = IconFont,
                        FontSize = 16,
                        Foreground = themeBrush,
                        Text = "\uEBA0", // Empty battery frame
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    _battIconGrid.Children.Add(disconnectedGlyph);
                }
                else
                {
                    int levelIndex = (int)Math.Round(dev.BatteryPercent / 10.0);
                    if (levelIndex < 0) levelIndex = 0;
                    if (levelIndex > 10) levelIndex = 10;

                    bool isColored = dev.IsCharging || dev.BatteryPercent <= 20;

                    if (isColored)
                    {
                        char fillChar = dev.IsCharging ? (char)(0xEBAB + levelIndex) : (char)(0xEBA0 + levelIndex);
                        var fill = new TextBlock
                        {
                            FontFamily = IconFont,
                            FontSize = 16,
                            Foreground = dev.IsCharging ? ChargingGreenBrush : CriticalRedBrush,
                            Text = fillChar.ToString(),
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        _battIconGrid.Children.Add(fill);

                        var outline = new TextBlock
                        {
                            FontFamily = IconFont,
                            FontSize = 16,
                            Foreground = themeBrush,
                            Text = dev.IsCharging ? "\uEBAB" : "\uEBA0",
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        _battIconGrid.Children.Add(outline);
                    }
                    else
                    {
                        var normalGlyph = new TextBlock
                        {
                            FontFamily = IconFont,
                            FontSize = 16,
                            Foreground = themeBrush,
                            Text = ((char)(0xEBA0 + levelIndex)).ToString(),
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        _battIconGrid.Children.Add(normalGlyph);
                    }
                }
            }
        }

        /// <summary>
        /// Updates the hover tooltip text of the system tray notification icon, truncating to 63 characters.
        /// </summary>
        /// <param name="devices">The list of active devices whose summaries should be displayed.</param>
        private void UpdateTrayTooltip(List<TaskbarDeviceState> devices)
        {
            if (_notifyIcon == null) return;
            if (devices == null || devices.Count == 0)
            {
                _notifyIcon.Text = "Device Battery: Disconnected";
                return;
            }

            string tip = string.Join(" | ", devices.Select(d =>
                (d.IsConnected && d.BatteryPercent >= 0)
                    ? string.Format("{0}: {1}%{2}", d.DisplayName, d.BatteryPercent, d.IsCharging ? " \u26A1" : "")
                    : string.Format("{0}: Disconnected", d.DisplayName)));
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            _notifyIcon.Text = tip;
        }

        /// <summary>
        /// Toggles the visibility of the Fluent flyout details popup window anchored above the taskbar widget.
        /// </summary>
        public void ShowFlyout()
        {
            var visibleDevices = GetVisibleDevices(SortDevicesByCustomOrder(_latestDevices));
            if (_flyout == null)
            {
                _flyout = new FlyoutWindow(this, visibleDevices);
                _flyout.IsVisibleChanged += (s, e) =>
                {
                    if (!_flyout.IsVisible)
                    {
                        TaskbarHelper.TrimProcessMemory();
                    }
                };
            }

            if (_flyout.IsVisible)
            {
                _flyout.HideFlyout();
            }
            else
            {
                _flyout.ShowFlyout(visibleDevices);
            }
        }

        /// <summary>
        /// Displays an interactive toast alert notifying the user of critically low battery level on a device.
        /// </summary>
        /// <param name="dev">The device whose battery has fallen below the critical threshold.</param>
        private void ShowLowBatteryToast(TaskbarDeviceState dev)
        {
            var notif = new NotificationWindow("Low Battery Alert", string.Format("{0}% remaining on {1}.", dev.BatteryPercent, dev.DisplayName));
            notif.Show();
        }

        /// <summary>
        /// Displays the settings and configuration context menu either anchored above the taskbar widget
        /// or floating at cursor coordinates when triggered from the system tray icon.
        /// </summary>
        /// <param name="fromTray">Flag indicating whether the menu was opened from the system tray notification icon.</param>
        public void ShowContextMenu(bool fromTray = false)
        {
            TaskbarContextMenu.Show(
                owner: this,
                fromTray: fromTray,
                isDark: IsDarkTheme,
                devicesList: _allKnownDevices.Values.ToList(),
                onOpenFlyout: ShowFlyout,
                onRefresh: () => { if (_omniManager != null) _omniManager.ForceRefresh(); }
            );
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // WinEvent Hooks & Geometry Updates
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Registers Win32 accessibility event hooks to track taskbar geometry updates, foreground transitions, and window resizes.
        /// </summary>
        private void SetupHooks()
        {
            _winEventProc = new TaskbarHelper.WinEventDelegate(WinEventCallback);

            IntPtr taskbar = TaskbarHelper.GetTaskbarHandle();
            if (taskbar != IntPtr.Zero)
            {
                uint processId;
                uint threadId = TaskbarHelper.GetWindowThreadProcessId(taskbar, out processId);
                if (threadId != 0)
                {
                    _hTaskbarHook = TaskbarHelper.SetWinEventHook(
                        TaskbarHelper.EVENT_OBJECT_SHOW,
                        TaskbarHelper.EVENT_OBJECT_LOCATIONCHANGE,
                        IntPtr.Zero,
                        _winEventProc,
                        processId,
                        threadId,
                        TaskbarHelper.WINEVENT_OUTOFCONTEXT);
                }
            }

            // Global hook for immediate foreground window transitions (games, fullscreen apps, Alt+Tab)
            _hGlobalHook = TaskbarHelper.SetWinEventHook(
                TaskbarHelper.EVENT_SYSTEM_FOREGROUND,
                TaskbarHelper.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _winEventProc,
                0,
                0,
                TaskbarHelper.WINEVENT_OUTOFCONTEXT);

            // Global hook for in-place window resize / F11 fullscreen events
            _hGlobalSizeHook = TaskbarHelper.SetWinEventHook(
                TaskbarHelper.EVENT_SYSTEM_MOVESIZEEND,
                TaskbarHelper.EVENT_SYSTEM_MOVESIZEEND,
                IntPtr.Zero,
                _winEventProc,
                0,
                0,
                TaskbarHelper.WINEVENT_OUTOFCONTEXT);
        }

        /// <summary>
        /// Handles Windows session state changes (lock / unlock) to throttle or resume hardware telemetry polling.
        /// </summary>
        private void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
        {
            if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock)
            {
                _isSessionLocked = true;
                UpdateBackgroundModeState();
            }
            else if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock)
            {
                _isSessionLocked = false;
                UpdateBackgroundModeState();
            }
        }

        /// <summary>
        /// Evaluates active background condition (fullscreen game or locked session) and dynamically adjusts telemetry polling rate.
        /// Throttles to <see cref="AppSettings.BackgroundPollIntervalSeconds"/> in background, and restores
        /// <see cref="AppSettings.PollIntervalSeconds"/> with an immediate telemetry refresh upon returning to desktop.
        /// </summary>
        private void UpdateBackgroundModeState()
        {
            bool shouldBeBackground = _lastFullscreen || _isSessionLocked;
            if (shouldBeBackground == _isBackgroundMode) return;
            _isBackgroundMode = shouldBeBackground;

            if (_isBackgroundMode)
            {
                int bgSec = Math.Max(30, SettingsManager.Instance.Current.BackgroundPollIntervalSeconds);
                Logger.Log(string.Format("Entering background mode (fullscreen/games/lock). Throttling polling interval to {0}s.", bgSec));
                if (_omniManager != null)
                {
                    _omniManager.SetPollInterval(bgSec * 1000);
                }
                TaskbarHelper.TrimProcessMemory();
            }
            else
            {
                int normalSec = Math.Max(5, SettingsManager.Instance.Current.PollIntervalSeconds);
                Logger.Log(string.Format("Exiting background mode. Restoring polling interval to {0}s and requesting immediate refresh.", normalSec));
                if (_omniManager != null)
                {
                    _omniManager.SetPollInterval(normalSec * 1000);
                    _omniManager.RefreshTelemetry();
                }
                UpdatePosition();
                ScheduleFullscreenExitTrim();
            }
        }

        /// <summary>
        /// Native hook callback invoked when foreground window, taskbar or window bounds shift.
        /// </summary>
        private void WinEventCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            UpdatePosition();
            if (eventType == TaskbarHelper.EVENT_SYSTEM_FOREGROUND && this.Visibility == Visibility.Visible)
            {
                var myHwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (myHwnd != IntPtr.Zero)
                {
                    TaskbarHelper.SetWindowPos(myHwnd, TaskbarHelper.HWND_TOPMOST, 0, 0, 0, 0,
                        TaskbarHelper.SWP_NOACTIVATE | TaskbarHelper.SWP_NOSIZE | TaskbarHelper.SWP_NOMOVE);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Window Positioning & Pure Win32 Docking Math
        // ═══════════════════════════════════════════════════════════════════════════

        private bool _isUpdatingPosition = false;
        private int _lastX = -1;
        private int _lastY = -1;
        private bool _lastFullscreen = false;

        /// <summary>
        /// Recalculates the screen position of the overlay to dock at the configured taskbar location.
        /// Evaluates fullscreen state and user-selected position (Right beside Tray vs Left beside Start/Widgets).
        /// </summary>
        public void UpdatePosition()
        {
            if (_isUpdatingPosition) return;
            _isUpdatingPosition = true;
            try
            {
                var hwnd = _overlayHwnd != IntPtr.Zero ? _overlayHwnd : new System.Windows.Interop.WindowInteropHelper(this).Handle;
                IntPtr flyoutHwnd = (_flyout != null && _flyout.IsVisible) ? new System.Windows.Interop.WindowInteropHelper(_flyout).Handle : IntPtr.Zero;
                bool isFullscreen = TaskbarHelper.IsForegroundFullscreen(hwnd, flyoutHwnd);
                if (isFullscreen != _lastFullscreen)
                {
                    _lastFullscreen = isFullscreen;
                    Logger.Log(string.Format("Fullscreen state changed: isFullscreen={0}", isFullscreen));
                    UpdateBackgroundModeState();
                }
                bool isTrayOnly = SettingsManager.Instance.Current.DisplayMode == 1;

                if (isTrayOnly || isFullscreen || (_shouldHideOverlay && _hideWhenDisconnected))
                {
                    if (this.Visibility != Visibility.Hidden)
                    {
                        this.Visibility = Visibility.Hidden;
                        _lastVisibility = Visibility.Hidden;
                    }
                    return;
                }
                else
                {
                    if (this.Visibility != Visibility.Visible)
                        this.Visibility = Visibility.Visible;
                }

                UpdateTheme();

                IntPtr taskbar = TaskbarHelper.GetTaskbarHandle();
                if (taskbar == IntPtr.Zero) return;

                TaskbarHelper.RECT tbRect;
                if (!TaskbarHelper.GetWindowRect(taskbar, out tbRect)) return;

                if (!_dpiInitialized)
                {
                    var source = PresentationSource.FromVisual(this);
                    if (source != null && source.CompositionTarget != null)
                    {
                        _dpiX = source.CompositionTarget.TransformToDevice.M11;
                        _dpiY = source.CompositionTarget.TransformToDevice.M22;
                        _dpiInitialized = true;
                    }
                }

                double dpiX = _dpiX > 0 ? _dpiX : 1.0;
                double dpiY = _dpiY > 0 ? _dpiY : 1.0;

                int physicalWidth = (int)(this.Width * dpiX);
                int physicalHeight = (int)(this.Height * dpiY);
                int taskbarHeight = tbRect.Bottom - tbRect.Top;
                int y = tbRect.Top + (taskbarHeight - physicalHeight) / 2;

                // Standard placement: docked directly beside the Windows System Tray (TrayNotifyWnd)
                int x;
                TaskbarHelper.RECT trayRect;
                if (TaskbarHelper.GetTrayRect(taskbar, out trayRect))
                {
                    x = trayRect.Left - physicalWidth - 4;
                }
                else
                {
                    x = tbRect.Right - physicalWidth - 10;
                }

                bool positionChanged = (x != _lastX || y != _lastY);
                bool visibilityBecameVisible = (_lastVisibility != Visibility.Visible && this.Visibility == Visibility.Visible);

                if (positionChanged)
                {
                    _lastX = x;
                    _lastY = y;

                    this.Left = x / dpiX;
                    this.Top = y / dpiY;

                    if (hwnd != IntPtr.Zero)
                    {
                        DwmHelper.EnableWindowTransitions(hwnd, false);
                        TaskbarHelper.SetWindowPos(hwnd, TaskbarHelper.HWND_TOPMOST, x, y, physicalWidth, physicalHeight, TaskbarHelper.SWP_NOACTIVATE);
                    }
                }
                else if (visibilityBecameVisible && hwnd != IntPtr.Zero)
                {
                    TaskbarHelper.SetWindowPos(hwnd, TaskbarHelper.HWND_TOPMOST, 0, 0, 0, 0,
                        TaskbarHelper.SWP_NOACTIVATE | TaskbarHelper.SWP_NOSIZE | TaskbarHelper.SWP_NOMOVE);
                }

                _lastVisibility = this.Visibility;
            }
            finally
            {
                _isUpdatingPosition = false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // System Theme Synchronization
        // ═══════════════════════════════════════════════════════════════════════════

        private DateTime _lastThemeCheckTime = DateTime.MinValue;

        /// <summary>
        /// Queries the Windows registry to inspect the system theme preference (<c>SystemUsesLightTheme</c>),
        /// dynamically applying dark mode DWM styling, tray icon colors, flyout styling, and widget rendering.
        /// </summary>
        /// <param name="force"><c>true</c> to bypass the 10-second throttle interval (e.g. on WM_SETTINGCHANGE).</param>
        private void UpdateTheme(bool force = false)
        {
            try
            {
                if (!force && (DateTime.Now - _lastThemeCheckTime).TotalMilliseconds < 10000)
                    return;
                _lastThemeCheckTime = DateTime.Now;

                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("SystemUsesLightTheme");
                        bool isLight = val != null && (int)val == 1;

                        if (_lastIsSystemLight != isLight)
                        {
                            _lastIsSystemLight = isLight;
                            DwmHelper.InvalidateThemeColorCache();

                            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                            if (hwnd != IntPtr.Zero)
                            {
                                DwmHelper.SetDarkMode(hwnd, !isLight);
                            }

                            UpdateDynamicTrayIcon(_latestDevices, isLight);

                            if (_flyout != null && _flyout.IsVisible)
                            {
                                _flyout.UpdateTheme(!isLight);
                            }

                            ApplyDevicesState(_latestDevices);
                        }
                    }
                }
            }
            catch { }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // System Tray (NotifyIcon) Dynamic Icon Rendering & Cleanup
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Renders an in-memory 16x16 GDI+ bitmap depicting live peripheral telemetry or minimal battery glyphs,
        /// converting it to a native Win32 icon, caching it to avoid GDI churn, and destroying intermediate unmanaged handles.
        /// </summary>
        /// <param name="devices">Current snapshot of target devices.</param>
        /// <param name="isLight">True if Windows taskbar uses a light theme; false for dark theme.</param>
        private void UpdateDynamicTrayIcon(List<TaskbarDeviceState> devices, bool isLight)
        {
            if (_notifyIcon == null) return;

            var activeDev = devices != null ? devices.FirstOrDefault(d => d.IsConnected && d.BatteryPercent >= 0) : null;
            string traySig;
            if (activeDev != null)
            {
                int fillWidth = Math.Max(1, (int)Math.Round((activeDev.BatteryPercent / 100.0) * 8.0));
                bool isLow = activeDev.BatteryPercent <= 20;
                traySig = string.Format("b_{0}_{1}_{2}_{3}", fillWidth, activeDev.IsCharging, isLow, isLight);
            }
            else
            {
                traySig = "offline_" + isLight;
            }

            // Fast-path: if tray icon visual state has not changed and an icon is already set, skip redrawing
            if (string.Equals(traySig, _lastTrayIconSignature, StringComparison.Ordinal) && _notifyIcon.Icon != null)
            {
                return;
            }
            _lastTrayIconSignature = traySig;

            // Check cache first to avoid GDI/Bitmap churn
            System.Drawing.Icon cachedIcon;
            if (_trayIconCache.TryGetValue(traySig, out cachedIcon))
            {
                _notifyIcon.Icon = cachedIcon;
                return;
            }

            try
            {
                using (var bmp = new Bitmap(16, 16))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(System.Drawing.Color.Transparent);
                    var strokeColor = isLight ? System.Drawing.Color.Black : System.Drawing.Color.White;

                    if (activeDev != null)
                    {
                        // Battery frame
                        using (var pen = new System.Drawing.Pen(strokeColor, 1.2f))
                        {
                            g.DrawRectangle(pen, 1, 3, 11, 9);
                        }
                        // Terminal nub
                        using (var brush = new SolidBrush(strokeColor))
                        {
                            g.FillRectangle(brush, 12, 6, 2, 3);
                        }

                        // Interior fill bar based on percentage
                        int fillWidth = Math.Max(1, (int)Math.Round((activeDev.BatteryPercent / 100.0) * 8.0));
                        var fillBrushColor = activeDev.IsCharging
                            ? System.Drawing.Color.FromArgb(40, 200, 90)
                            : (activeDev.BatteryPercent <= 20
                                ? System.Drawing.Color.FromArgb(225, 45, 45)
                                : strokeColor);

                        using (var fillBrush = new SolidBrush(fillBrushColor))
                        {
                            g.FillRectangle(fillBrush, 3, 5, fillWidth, 5);
                        }
                    }
                    else
                    {
                        // Default offline outline icon
                        using (var pen = new System.Drawing.Pen(strokeColor, 1.2f))
                        {
                            g.DrawRectangle(pen, 1, 3, 11, 9);
                        }
                        using (var brush = new SolidBrush(strokeColor))
                        {
                            g.FillRectangle(brush, 12, 6, 2, 3);
                        }
                    }

                    IntPtr hIcon = bmp.GetHicon();
                    try
                    {
                        using (var tempIcon = System.Drawing.Icon.FromHandle(hIcon))
                        {
                            var persistentIcon = (System.Drawing.Icon)tempIcon.Clone();
                            _trayIconCache[traySig] = persistentIcon;
                            _notifyIcon.Icon = persistentIcon;
                        }
                    }
                    finally
                    {
                        // Crucial: Destroy unmanaged Win32 GDI icon handle to prevent GDI resource leaks
                        TaskbarHelper.DestroyIcon(hIcon);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to render dynamic tray icon: " + ex.Message);
            }
        }

        /// <summary>
        /// Initializes the WinForms <see cref="System.Windows.Forms.NotifyIcon"/> instance and wires mouse click events.
        /// </summary>
        private void InitNotifyIcon()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Text = "OmniHID Battery Indicator",
                Visible = true,
                ContextMenuStrip = null
            };

            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    this.Dispatcher.Invoke(() => ShowFlyout());
                }
                else if (e.Button == System.Windows.Forms.MouseButtons.Right)
                {
                    this.Dispatcher.Invoke(() => ShowContextMenu(true));
                }
            };

            UpdateDynamicTrayIcon(null, !IsDarkTheme);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Hardware Change Events & Settings Persistence
        // ═══════════════════════════════════════════════════════════════════════════

        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVICEARRIVAL = 0x8000;
        private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
        private const int DBT_DEVNODES_CHANGED = 0x0007;
        private const int WM_SETTINGCHANGE = 0x001A;
        private const int WM_THEMECHANGED = 0x031A;

        private DateTime _lastDeviceChangePoll = DateTime.MinValue;

        /// <summary>
        /// Native window procedure hook intercepting broadcast Windows messages.
        /// Detects USB/HID arrival/removal events (<c>WM_DEVICECHANGE</c>) and system theme updates (<c>WM_SETTINGCHANGE</c>).
        /// </summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DEVICECHANGE)
            {
                int wp = wParam.ToInt32();
                if (wp == DBT_DEVICEARRIVAL || wp == DBT_DEVICEREMOVECOMPLETE || wp == DBT_DEVNODES_CHANGED)
                {
                    if ((DateTime.Now - _lastDeviceChangePoll).TotalMilliseconds > 1000)
                    {
                        _lastDeviceChangePoll = DateTime.Now;
                        Logger.Log("System USB/Hardware device change detected (WM_DEVICECHANGE). Forwarding to OmniHID...");
                        if (_omniManager != null) _omniManager.ProcessDeviceChangeNotification();
                    }
                }
            }
            else if (msg == WM_SETTINGCHANGE || msg == WM_THEMECHANGED)
            {
                UpdateTheme(force: true);
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// Loads user settings from the singleton <see cref="SettingsManager"/> into local instance fields.
        /// </summary>
        private void LoadSettings()
        {
            try
            {
                var s = SettingsManager.Instance.Current;
                _hideWhenDisconnected = s.HideWhenDisconnected;
                _displayStyle = s.DisplayStyle;
                Logger.Log(string.Format("Settings loaded: Style={0}, HideDisconnected={1}",
                    _displayStyle, _hideWhenDisconnected));
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to load settings: " + ex.Message);
            }
        }
    }
}
