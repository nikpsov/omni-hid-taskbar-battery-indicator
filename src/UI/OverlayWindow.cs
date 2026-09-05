using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OmniHidTaskbar.Core;
using OmniHid.Core;
using OmniHid.Core.Abstractions;

namespace OmniHidTaskbar.UI
{
    public class OverlayWindow : Window
    {
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
        const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        const uint WINEVENT_OUTOFCONTEXT = 0;

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLong32(hWnd, nIndex, dwNewLong);
        }
        const int GWLP_HWNDPARENT = -8;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private DispatcherTimer _positionTimer;
        private readonly StackPanel _mainStack;
        private readonly Border _containerBorder;

        private WinEventDelegate _winEventProc;
        private IntPtr _hTaskbarHook;
        private IntPtr _hGlobalHook;
        private FlyoutWindow _flyout;

        private System.Windows.Forms.NotifyIcon _notifyIcon;
        private System.Windows.Interop.HwndSource _hwndSource;
        private bool _hideWhenDisconnected = true;
        private bool _shouldHideOverlay = false;
        private bool _runOnStartup = false;
        private bool? _lastIsSystemLight = null;

        private int _displayStyle = 0; // 0 = Percent, 1 = Icon

        private List<TaskbarDeviceState> _latestDevices = new List<TaskbarDeviceState>();
        private readonly HashSet<string> _warnedLowBatteryDeviceIds = new HashSet<string>();

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

        public OverlayWindow()
        {
            LoadSettings();

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            Topmost = true;
            ShowInTaskbar = false;

            Height = 36;
            Width = 90; // Default width, auto-resized by children

            _mainStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = Brushes.Transparent,
                Margin = new Thickness(6, 0, 6, 0)
            };

            _containerBorder = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255)),
                Cursor = Cursors.Hand,
                Margin = new Thickness(2, 0, 2, 0),
                Child = _mainStack
            };

            Content = _containerBorder;

            _containerBorder.MouseEnter += (s, e) =>
            {
                bool isLight = _lastIsSystemLight.GetValueOrDefault(false);
                _containerBorder.Background = isLight ?
                    new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)) :
                    new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
            };
            _containerBorder.MouseLeave += (s, e) =>
            {
                _containerBorder.Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255));
            };
            this.PreviewMouseLeftButtonDown += (s, e) =>
            {
                bool isLight = _lastIsSystemLight.GetValueOrDefault(false);
                _containerBorder.Background = isLight ?
                    new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)) :
                    new SolidColorBrush(Color.FromArgb(35, 255, 255, 255));
            };
            this.PreviewMouseLeftButtonUp += (s, e) =>
            {
                bool isLight = _lastIsSystemLight.GetValueOrDefault(false);
                _containerBorder.Background = isLight ?
                    new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)) :
                    new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
            };
            this.MouseLeftButtonUp += (s, e) => ShowFlyout();
            this.MouseRightButtonUp += (s, e) => ShowContextMenu();

            ApplyDevicesState(_latestDevices);

            InitNotifyIcon();

            // Subscribe to DeviceManager
            _omniManager = new OmniManager();
            _omniManager.DevicesUpdated += OnOmniDevicesUpdated;
            int pollSec = SettingsManager.Instance.Current.PollIntervalSeconds;
            _omniManager.StartMonitoring(pollSec > 0 ? pollSec * 1000 : 15000);

            this.SourceInitialized += (s, e) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
                if (taskbar != IntPtr.Zero)
                {
                    SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, taskbar);
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

                SetupHooks();
                UpdatePosition();

                _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                _positionTimer.Tick += (ts, te) => UpdatePosition();
                _positionTimer.Start();
            };

            this.Closed += (s, e) =>
            {
                if (_omniManager != null) { _omniManager.Dispose(); _omniManager = null; }
                if (_positionTimer != null) _positionTimer.Stop();
                if (_hwndSource != null)
                {
                    _hwndSource.RemoveHook(WndProc);
                    _hwndSource = null;
                }
                if (_hTaskbarHook != IntPtr.Zero)
                {
                    UnhookWinEvent(_hTaskbarHook);
                    _hTaskbarHook = IntPtr.Zero;
                }
                if (_hGlobalHook != IntPtr.Zero)
                {
                    UnhookWinEvent(_hGlobalHook);
                    _hGlobalHook = IntPtr.Zero;
                }
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                }
                if (_flyout != null)
                {
                    _flyout.Close();
                    _flyout = null;
                }
            };
        }

        private OmniManager _omniManager;

        public List<TaskbarDeviceState> LatestDevices { get { return _latestDevices; } }

        private void OnOmniDevicesUpdated(IReadOnlyList<IOmniDevice> devices)
        {
            Logger.Log(string.Format("OmniHID update received: {0} device(s)", devices != null ? devices.Count : 0));
            if (devices != null)
            {
                foreach (var d in devices)
                {
                    var tel = d.Telemetry;
                    Logger.Log(string.Format("  Device: {0} [{1}] Connected={2}, Level={3}%, Charging={4}, State='{5}'",
                        d.Name, d.Id, d.IsConnected, tel != null ? tel.LevelPercent : -1, tel != null && tel.IsCharging, tel != null ? tel.StateDescription : "Offline"));
                }
            }

            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                _latestDevices = devices != null ? devices.Select(TaskbarDeviceState.FromOmniDevice).ToList() : new List<TaskbarDeviceState>();
                ApplyDevicesState(_latestDevices);
            }));
        }

        private void ApplyDevicesState(List<TaskbarDeviceState> devices)
        {
            try
            {
                _mainStack.Children.Clear();
                bool isLight = !IsDarkTheme;
                var themeBrush = isLight ? Brushes.Black : Brushes.White;

                var connectedDevices = devices.Where(d => d.IsConnected && d.BatteryPercent >= 0).ToList();

                if (connectedDevices.Count > 0)
                {
                    _shouldHideOverlay = false;

                    for (int i = 0; i < connectedDevices.Count; i++)
                    {
                        var dev = connectedDevices[i];
                        if (i > 0)
                        {
                            // Spacer between multiple devices
                            var spacer = new Border
                            {
                                Width = 8,
                                Background = Brushes.Transparent
                            };
                            _mainStack.Children.Add(spacer);
                        }

                        var devWidget = BuildTaskbarDeviceWidget(dev, themeBrush, isLight);
                        _mainStack.Children.Add(devWidget);

                        // Low battery toast warning
                        if (dev.BatteryPercent <= 20 && !dev.IsCharging)
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

                    // Adjust overlay width based on count of items
                    this.Width = Math.Max(70, connectedDevices.Count * 68 + 20);
                    this.Visibility = IsForegroundFullscreen() ? Visibility.Hidden : Visibility.Visible;

                    UpdateTrayTooltip(connectedDevices);
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
                        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                        FontSize = 16,
                        Foreground = themeBrush,
                        Opacity = 0.75,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };

                    var crossText = new TextBlock
                    {
                        Text = "\u2715",
                        FontFamily = new FontFamily("Segoe UI, Arial, sans-serif"),
                        FontSize = 8.5,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(225, 45, 45)),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(0, 0, -3, -2)
                    };

                    disconnectedGrid.Children.Add(iconText);
                    disconnectedGrid.Children.Add(crossText);
                    _mainStack.Children.Add(disconnectedGrid);

                    this.Width = 46;
                    this.Visibility = (_hideWhenDisconnected || IsForegroundFullscreen()) ? Visibility.Hidden : Visibility.Visible;

                    if (_notifyIcon != null)
                    {
                        _notifyIcon.Text = "Device Battery: Disconnected";
                    }
                }

                if (_flyout != null && _flyout.IsVisible)
                {
                    _flyout.UpdateData(devices);
                }
            }
            catch { }
        }

        private FrameworkElement BuildTaskbarDeviceWidget(TaskbarDeviceState dev, Brush themeBrush, bool isLight)
        {
            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Device glyph
            var iconBlock = new TextBlock
            {
                Text = !string.IsNullOrEmpty(dev.IconGlyph) ? dev.IconGlyph : dev.GetDefaultIconGlyph(),
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 15,
                Foreground = themeBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 3, 0)
            };
            stack.Children.Add(iconBlock);

            if (_displayStyle == 0)
            {
                // Percent text
                var battText = new TextBlock
                {
                    FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
                    FontSize = 12.5,
                    FontWeight = FontWeights.Normal,
                    Foreground = themeBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    Text = dev.BatteryPercent + "%",
                    Margin = new Thickness(0, 0, 2, 0)
                };
                stack.Children.Add(battText);

                if (dev.IsCharging)
                {
                    var bolt = new TextBlock
                    {
                        Text = "\uE945",
                        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(30, 215, 96)),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 1, 0)
                    };
                    stack.Children.Add(bolt);
                }
            }
            else
            {
                // Battery Icon Glyphs
                var battIconGrid = new Grid
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 2, 0)
                };

                int levelIndex = (int)Math.Round(dev.BatteryPercent / 10.0);
                if (levelIndex < 0) levelIndex = 0;
                if (levelIndex > 10) levelIndex = 10;

                bool isColored = dev.IsCharging || dev.BatteryPercent <= 20;

                if (isColored)
                {
                    var outline = new TextBlock
                    {
                        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                        FontSize = 16,
                        Foreground = themeBrush,
                        Text = dev.IsCharging ? "\uEBAB" : "\uEBA0",
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    battIconGrid.Children.Add(outline);

                    char fillChar = dev.IsCharging ? (char)(0xEBAB + levelIndex) : (char)(0xEBA0 + levelIndex);
                    var fill = new TextBlock
                    {
                        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                        FontSize = 16,
                        Foreground = dev.IsCharging ?
                            new SolidColorBrush(Color.FromRgb(30, 215, 96)) :
                            new SolidColorBrush(Color.FromRgb(225, 40, 40)),
                        Text = fillChar.ToString(),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    battIconGrid.Children.Add(fill);
                }
                else
                {
                    var normalGlyph = new TextBlock
                    {
                        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                        FontSize = 16,
                        Foreground = themeBrush,
                        Text = ((char)(0xEBA0 + levelIndex)).ToString(),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    battIconGrid.Children.Add(normalGlyph);
                }

                stack.Children.Add(battIconGrid);
            }

            return stack;
        }

        private void UpdateTrayTooltip(List<TaskbarDeviceState> connectedDevices)
        {
            if (_notifyIcon == null) return;
            string tip = string.Join(" | ", connectedDevices.Select(d =>
                string.Format("{0}: {1}%{2}", d.Name, d.BatteryPercent, d.IsCharging ? " ⚡" : "")));
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            _notifyIcon.Text = tip;
        }

        private void ShowFlyout()
        {
            if (_flyout == null)
            {
                _flyout = new FlyoutWindow(this, _latestDevices);
            }

            if (_flyout.IsVisible)
            {
                _flyout.HideFlyout();
            }
            else
            {
                _flyout.ShowFlyout(_latestDevices);
            }
        }

        private void ShowLowBatteryToast(TaskbarDeviceState dev)
        {
            var notif = new NotificationWindow("Low Battery Alert", string.Format("{0}% remaining on {1}.", dev.BatteryPercent, dev.Name));
            notif.Show();
        }

        private static Style CreateContextMenuStyle(bool isDark)
        {
            var style = new Style(typeof(ContextMenu));
            var template = new ControlTemplate(typeof(ContextMenu));

            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, isDark ? new SolidColorBrush(Color.FromArgb(242, 32, 32, 32)) : new SolidColorBrush(Color.FromArgb(245, 252, 252, 252)));
            border.SetValue(Border.BorderBrushProperty, isDark ? new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.PaddingProperty, new Thickness(4));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);

            var itemsPresenter = new FrameworkElementFactory(typeof(ItemsPresenter));
            border.AppendChild(itemsPresenter);

            template.VisualTree = border;
            style.Setters.Add(new Setter(Control.TemplateProperty, template));

            var dropShadow = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 3,
                Direction = 270,
                Color = Colors.Black,
                Opacity = isDark ? 0.45 : 0.15
            };
            style.Setters.Add(new Setter(UIElement.EffectProperty, dropShadow));

            return style;
        }

        private static Style CreateMenuItemStyle(bool isDark)
        {
            var style = new Style(typeof(MenuItem));
            var template = new ControlTemplate(typeof(MenuItem));

            var border = new FrameworkElementFactory(typeof(Border), "Bd");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.PaddingProperty, new Thickness(8, 6, 12, 6));
            border.SetValue(Border.MarginProperty, new Thickness(0, 1, 0, 1));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);

            var grid = new FrameworkElementFactory(typeof(Grid));

            var col0 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col0.SetValue(ColumnDefinition.WidthProperty, new GridLength(20));
            grid.AppendChild(col0);

            var col1 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col1.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            grid.AppendChild(col1);

            var checkGlyph = new FrameworkElementFactory(typeof(TextBlock), "CheckGlyph");
            checkGlyph.SetValue(Grid.ColumnProperty, 0);
            checkGlyph.SetValue(TextBlock.TextProperty, "\uE73E");
            checkGlyph.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"));
            checkGlyph.SetValue(TextBlock.FontSizeProperty, 11.0);
            checkGlyph.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            checkGlyph.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            checkGlyph.SetValue(TextBlock.VisibilityProperty, Visibility.Collapsed);
            checkGlyph.SetValue(TextBlock.ForegroundProperty, isDark ? new SolidColorBrush(Color.FromRgb(96, 205, 255)) : new SolidColorBrush(Color.FromRgb(0, 95, 184)));
            grid.AppendChild(checkGlyph);

            var cp = new FrameworkElementFactory(typeof(ContentPresenter), "HeaderHost");
            cp.SetValue(Grid.ColumnProperty, 1);
            cp.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            cp.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            grid.AppendChild(cp);

            border.AppendChild(grid);
            template.VisualTree = border;

            var checkedTrigger = new Trigger { Property = MenuItem.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "CheckGlyph"));
            template.Triggers.Add(checkedTrigger);

            var highlightTrigger = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
            highlightTrigger.Setters.Add(new Setter(
                Border.BackgroundProperty,
                isDark ? new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(16, 0, 0, 0)),
                "Bd"));
            template.Triggers.Add(highlightTrigger);

            var pressedTrigger = new Trigger { Property = MenuItem.IsPressedProperty, Value = true };
            pressedTrigger.Setters.Add(new Setter(
                Border.BackgroundProperty,
                isDark ? new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(26, 0, 0, 0)),
                "Bd"));
            template.Triggers.Add(pressedTrigger);

            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(Control.ForegroundProperty, isDark ? Brushes.White : new SolidColorBrush(Color.FromRgb(26, 26, 26))));
            style.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif")));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));

            return style;
        }

        private static Separator CreateStyledSeparator(bool isDark)
        {
            return new Separator
            {
                Margin = new Thickness(4, 3, 4, 3),
                Height = 1,
                Background = isDark ? new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(20, 0, 0, 0)),
                BorderThickness = new Thickness(0)
            };
        }

        private void ShowContextMenu(bool fromTray = false)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetForegroundWindow(hwnd);
            }

            bool isDark = IsDarkTheme;
            var itemStyle = CreateMenuItemStyle(isDark);

            var menu = new ContextMenu
            {
                Style = CreateContextMenuStyle(isDark)
            };

            if (fromTray)
            {
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                menu.PlacementTarget = this;

                var openPanelItem = CreateStyledMenuItem("Open Panel", itemStyle);
                openPanelItem.Click += (s, e) => ShowFlyout();
                menu.Items.Add(openPanelItem);

                menu.Items.Add(CreateStyledSeparator(isDark));
            }
            else
            {
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
                menu.PlacementTarget = this;
                menu.VerticalOffset = -6;
            }

            var styleItem = CreateStyledMenuItem("Display Style: " + (_displayStyle == 0 ? "Percentage (85%)" : "Battery Icon"), itemStyle);
            styleItem.Click += (s, e) =>
            {
                _displayStyle = _displayStyle == 0 ? 1 : 0;
                SaveSettings();
                ApplyDevicesState(_latestDevices);
                UpdateTheme();
            };
            menu.Items.Add(styleItem);

            var hideItem = CreateStyledMenuItem("Hide when disconnected", itemStyle, true, _hideWhenDisconnected);
            hideItem.Click += (s, e) =>
            {
                _hideWhenDisconnected = hideItem.IsChecked;
                SaveSettings();
                ApplyDevicesState(_latestDevices);
            };
            menu.Items.Add(hideItem);

            var startupItem = CreateStyledMenuItem("Run on startup", itemStyle, true, _runOnStartup);
            startupItem.Click += (s, e) =>
            {
                _runOnStartup = startupItem.IsChecked;
                SetRunOnStartup(_runOnStartup);
            };
            menu.Items.Add(startupItem);

            menu.Items.Add(CreateStyledSeparator(isDark));

            var refreshItem = CreateStyledMenuItem("Refresh Device Info", itemStyle);
            refreshItem.Click += (s, e) => { if (_omniManager != null) _omniManager.ForceRefresh(); };
            menu.Items.Add(refreshItem);

            var exitItem = CreateStyledMenuItem("Exit", itemStyle);
            exitItem.Click += (s, e) => Application.Current.Shutdown();
            menu.Items.Add(exitItem);

            menu.IsOpen = true;
        }

        private MenuItem CreateStyledMenuItem(string text, Style style, bool isCheckable = false, bool isChecked = false)
        {
            return new MenuItem
            {
                Header = text,
                Style = style,
                IsCheckable = isCheckable,
                IsChecked = isChecked
            };
        }

        private void SetupHooks()
        {
            _winEventProc = new WinEventDelegate(WinEventCallback);

            // 1. Taskbar position/size changes
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero)
            {
                uint processId;
                uint threadId = GetWindowThreadProcessId(taskbar, out processId);
                if (threadId != 0)
                {
                    _hTaskbarHook = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _winEventProc, processId, threadId, WINEVENT_OUTOFCONTEXT);
                }
            }

            // 2. Global foreground window switch and resize/fullscreen transitions (fires immediately in 0 ms)
            _hGlobalHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_MOVESIZEEND, IntPtr.Zero, _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);
        }

        private void WinEventCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            UpdatePosition();
        }

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private bool IsForegroundFullscreen()
        {
            IntPtr fgWnd = GetForegroundWindow();
            if (fgWnd == IntPtr.Zero) return false;

            IntPtr desktop = FindWindow("Progman", null);
            IntPtr shell = FindWindow("WorkerW", null);
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (fgWnd == desktop || fgWnd == shell || fgWnd == taskbar) return false;

            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (fgWnd == hwnd) return false;

            if (_flyout != null && _flyout.IsVisible)
            {
                var flyoutHwnd = new System.Windows.Interop.WindowInteropHelper(_flyout).Handle;
                if (fgWnd == flyoutHwnd) return false;
            }

            RECT appBounds;
            if (!GetWindowRect(fgWnd, out appBounds)) return false;

            IntPtr hMonitor = MonitorFromWindow(fgWnd, MONITOR_DEFAULTTONEAREST);
            if (hMonitor == IntPtr.Zero) return false;

            MONITORINFO mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(mi);
            if (GetMonitorInfo(hMonitor, ref mi))
            {
                // Fullscreen if foreground window covers or exceeds the monitor screen
                return appBounds.Left <= mi.rcMonitor.Left &&
                       appBounds.Top <= mi.rcMonitor.Top &&
                       appBounds.Right >= mi.rcMonitor.Right &&
                       appBounds.Bottom >= mi.rcMonitor.Bottom;
            }
            return false;
        }

        private int _lastX = -1;
        private int _lastY = -1;
        private int _lastTrayLeft = -1;
        private int _lastTrayTop = -1;
        private int _lastTrayRight = -1;
        private int _lastTrayBottom = -1;

        public void UpdatePosition()
        {
            if (IsForegroundFullscreen() || (_shouldHideOverlay && _hideWhenDisconnected))
            {
                if (this.Visibility != Visibility.Hidden)
                    this.Visibility = Visibility.Hidden;
                return;
            }
            else
            {
                if (this.Visibility != Visibility.Visible)
                    this.Visibility = Visibility.Visible;
            }

            UpdateTheme();

            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero)
            {
                IntPtr trayNotify = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
                if (trayNotify != IntPtr.Zero)
                {
                    RECT rect;
                    if (GetWindowRect(trayNotify, out rect))
                    {
                        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;

                        var source = PresentationSource.FromVisual(this);
                        double dpiX = source != null ? source.CompositionTarget.TransformToDevice.M11 : 1.0;
                        double dpiY = source != null ? source.CompositionTarget.TransformToDevice.M22 : 1.0;

                        int physicalWidth = (int)(this.Width * dpiX);
                        int physicalHeight = (int)(this.Height * dpiY);

                        int x = rect.Left - physicalWidth;
                        int taskbarHeight = rect.Bottom - rect.Top;
                        int y = rect.Top + (taskbarHeight - physicalHeight) / 2;

                        if (x != _lastX || y != _lastY ||
                            rect.Left != _lastTrayLeft || rect.Top != _lastTrayTop ||
                            rect.Right != _lastTrayRight || rect.Bottom != _lastTrayBottom)
                        {
                            _lastX = x;
                            _lastY = y;
                            _lastTrayLeft = rect.Left;
                            _lastTrayTop = rect.Top;
                            _lastTrayRight = rect.Right;
                            _lastTrayBottom = rect.Bottom;

                            if (hwnd != IntPtr.Zero)
                            {
                                DwmHelper.EnableWindowTransitions(hwnd, false);
                                SetWindowPos(hwnd, HWND_TOPMOST, x, y, physicalWidth, physicalHeight, 0x0010); // NOACTIVATE
                            }
                        }
                        if (this.Visibility == Visibility.Visible && hwnd != IntPtr.Zero)
                        {
                            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, 0x0010 | 0x0001 | 0x0002);
                        }
                    }
                }
            }
        }

        private DateTime _lastThemeCheckTime = DateTime.MinValue;

        private void UpdateTheme()
        {
            try
            {
                if ((DateTime.Now - _lastThemeCheckTime).TotalMilliseconds < 1000)
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

                            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                            if (hwnd != IntPtr.Zero)
                            {
                                DwmHelper.SetDarkMode(hwnd, !isLight);
                            }

                            UpdateTrayIconTheme(isLight);

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

        private void UpdateTrayIconTheme(bool isLight)
        {
            try
            {
                using (var bmp = new System.Drawing.Bitmap(16, 16))
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.Clear(System.Drawing.Color.Transparent);
                    var pen = new System.Drawing.Pen(isLight ? System.Drawing.Color.Black : System.Drawing.Color.White, 1.5f);
                    // Battery outline icon
                    g.DrawRectangle(pen, 2, 4, 10, 8);
                    g.FillRectangle(new System.Drawing.SolidBrush(isLight ? System.Drawing.Color.Black : System.Drawing.Color.White), 12, 6, 2, 4);

                    IntPtr hIcon = bmp.GetHicon();
                    _notifyIcon.Icon = System.Drawing.Icon.FromHandle(hIcon);
                }
            }
            catch { }
        }

        private void InitNotifyIcon()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            _notifyIcon.Text = "OmniHID Battery Indicator";
            _notifyIcon.Visible = true;
            _notifyIcon.ContextMenuStrip = null;

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

            UpdateTrayIconTheme(!IsDarkTheme);
        }

        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVICEARRIVAL = 0x8000;
        private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
        private const int DBT_DEVNODES_CHANGED = 0x0007;

        private DateTime _lastDeviceChangePoll = DateTime.MinValue;

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
                        Logger.Log("System USB/Hardware device change detected (WM_DEVICECHANGE). Forcing refresh...");
                        if (_omniManager != null) _omniManager.ForceRefresh();
                    }
                }
            }
            return IntPtr.Zero;
        }

        private void LoadSettings()
        {
            try
            {
                var s = SettingsManager.Instance.Current;
                _hideWhenDisconnected = s.HideWhenDisconnected;
                _displayStyle = s.DisplayStyle;
                _runOnStartup = s.RunOnStartup;
                Logger.Log(string.Format("Settings loaded: Style={0}, HideDisconnected={1}, RunOnStartup={2}",
                    _displayStyle, _hideWhenDisconnected, _runOnStartup));
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to load settings: " + ex.Message);
            }
        }

        private void SaveSettings()
        {
            try
            {
                var s = SettingsManager.Instance.Current;
                s.HideWhenDisconnected = _hideWhenDisconnected;
                s.DisplayStyle = _displayStyle;
                s.RunOnStartup = _runOnStartup;
                SettingsManager.Instance.Save();
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to save settings: " + ex.Message);
            }
        }

        private void SetRunOnStartup(bool enable)
        {
            _runOnStartup = enable;
            SaveSettings();
        }
    }
}
