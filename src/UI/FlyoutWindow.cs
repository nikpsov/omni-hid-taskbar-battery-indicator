using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OmniHidTaskbar.Core;

namespace OmniHidTaskbar.UI
{
    public class FlyoutWindow : Window
    {
        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        static extern bool GetMonitorInfo(IntPtr hMonitor, ref OverlayWindow.MONITORINFO lpmi);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        private readonly OverlayWindow _owner;
        private readonly Border _rootBorder;
        private readonly StackPanel _cardsStack;

        // Settings view state
        private readonly Grid _containerGrid;
        private readonly StackPanel _settingsView;
        private TextBlock _statusFeedbackText;
        private TextBlock _settingsTitle;
        private Border _backBtn;
        private TextBlock _backIcon;
        private string _activeSettingsDeviceId;
        private readonly List<Border> _sleepButtonBorders = new List<Border>();
        private readonly List<TextBlock> _sleepButtonTexts = new List<TextBlock>();

        public FlyoutWindow(OverlayWindow owner, List<TaskbarDeviceState> devices)
        {
            _owner = owner;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            Topmost = true;
            ShowInTaskbar = false;
            Width = 310;
            SizeToContent = SizeToContent.Height;

            bool isDark = _owner != null ? _owner.IsDarkTheme : true;

            _rootBorder = new Border
            {
                Background = new SolidColorBrush(isDark ? Color.FromArgb(245, 28, 28, 28) : Color.FromArgb(248, 250, 250, 250)),
                BorderBrush = new SolidColorBrush(isDark ? Color.FromArgb(80, 255, 255, 255) : Color.FromArgb(60, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                Margin = new Thickness(6)
            };

            var dropShadow = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 3,
                Direction = 270,
                Color = Colors.Black,
                Opacity = isDark ? 0.45 : 0.2
            };
            _rootBorder.Effect = dropShadow;

            _containerGrid = new Grid { MinHeight = 78 };

            _cardsStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Center
            };

            // -------------------------------------------------------------
            // SETTINGS VIEW (For devices supporting sleep timer / options)
            // -------------------------------------------------------------
            _settingsView = new StackPanel
            {
                MinHeight = 78,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };

            BuildSettingsView(isDark);

            _containerGrid.Children.Add(_cardsStack);
            _containerGrid.Children.Add(_settingsView);

            _rootBorder.Child = _containerGrid;
            Content = _rootBorder;

            if (devices != null)
            {
                UpdateData(devices);
            }

            this.Deactivated += (s, e) => HideFlyout();
        }

        private void BuildSettingsView(bool isDark)
        {
            var settingsHeader = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            settingsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            settingsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _backBtn = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.Hand,
                ToolTip = "Back",
                Background = Brushes.Transparent
            };
            _backIcon = new TextBlock
            {
                Text = "\uE72B",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 12,
                Foreground = isDark ? Brushes.White : Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _backBtn.Child = _backIcon;

            _backBtn.MouseEnter += (s, e) =>
            {
                bool currentDark = _owner != null ? _owner.IsDarkTheme : true;
                _backBtn.Background = currentDark ? new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
            };
            _backBtn.MouseLeave += (s, e) =>
            {
                _backBtn.Background = Brushes.Transparent;
            };

            _backBtn.MouseLeftButtonUp += (s, e) =>
            {
                _settingsView.Visibility = Visibility.Collapsed;
                _statusFeedbackText.Visibility = Visibility.Collapsed;
                _cardsStack.Visibility = Visibility.Visible;
            };

            Grid.SetColumn(_backBtn, 0);
            settingsHeader.Children.Add(_backBtn);

            _settingsTitle = new TextBlock
            {
                Text = "Inactive Sleep Timer",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = isDark ? Brushes.White : Brushes.Black,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            Grid.SetColumn(_settingsTitle, 1);
            settingsHeader.Children.Add(_settingsTitle);

            _settingsView.Children.Add(settingsHeader);

            var sleepButtons = new System.Windows.Controls.Primitives.UniformGrid { Columns = 5, Margin = new Thickness(0, 2, 0, 0) };
            int[] timeouts = new int[] { 0, 5, 15, 30, 60 };
            string[] timeoutLabels = new string[] { "Off", "5m", "15m", "30m", "1h" };

            for (int i = 0; i < timeouts.Length; i++)
            {
                byte mins = (byte)timeouts[i];
                string label = timeoutLabels[i];

                var btnBorder = new Border
                {
                    Height = 26,
                    Margin = new Thickness(2, 0, 2, 0),
                    CornerRadius = new CornerRadius(4),
                    Background = isDark ? new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(20, 0, 0, 0)),
                    BorderBrush = isDark ? new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand
                };

                var btnText = new TextBlock
                {
                    Text = label,
                    FontSize = 11,
                    FontWeight = FontWeights.Medium,
                    Foreground = isDark ? Brushes.White : Brushes.Black,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                btnBorder.Child = btnText;

                btnBorder.MouseEnter += (s, e) =>
                {
                    bool currentDark = _owner != null ? _owner.IsDarkTheme : true;
                    btnBorder.Background = currentDark ? new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(40, 0, 0, 0));
                    btnBorder.BorderBrush = currentDark ? new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(60, 0, 0, 0));
                };
                btnBorder.MouseLeave += (s, e) =>
                {
                    bool currentDark = _owner != null ? _owner.IsDarkTheme : true;
                    btnBorder.Background = currentDark ? new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(20, 0, 0, 0));
                    btnBorder.BorderBrush = currentDark ? new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
                };

                btnBorder.MouseLeftButtonUp += (s, e) =>
                {
                    bool ok = false; // Sleep timer command
                    if (ok)
                    {
                        _statusFeedbackText.Text = string.Format("✓ Sleep timer set to {0}", mins == 0 ? "Off" : label);
                        _statusFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(40, 190, 90));
                    }
                    else
                    {
                        _statusFeedbackText.Text = "✕ Failed to set sleep timer";
                        _statusFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(230, 60, 60));
                    }
                    _statusFeedbackText.Visibility = Visibility.Visible;
                };

                _sleepButtonBorders.Add(btnBorder);
                _sleepButtonTexts.Add(btnText);
                sleepButtons.Children.Add(btnBorder);
            }
            _settingsView.Children.Add(sleepButtons);

            _statusFeedbackText = new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 0),
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                HorizontalAlignment = HorizontalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            _settingsView.Children.Add(_statusFeedbackText);
        }

        public void UpdateData(List<TaskbarDeviceState> devices)
        {
            _cardsStack.Children.Clear();
            bool isDark = _owner != null ? _owner.IsDarkTheme : true;

            if (devices == null || devices.Count == 0)
            {
                var emptyPanel = new StackPanel
                {
                    Margin = new Thickness(8),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                var emptyIcon = new TextBlock
                {
                    Text = "\uE772",
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    FontSize = 32,
                    Foreground = Brushes.Gray,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 6)
                };
                var emptyText = new TextBlock
                {
                    Text = "No supported devices connected",
                    FontSize = 12.5,
                    Foreground = isDark ? Brushes.LightGray : Brushes.DarkGray,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                emptyPanel.Children.Add(emptyIcon);
                emptyPanel.Children.Add(emptyText);
                _cardsStack.Children.Add(emptyPanel);
                return;
            }

            for (int i = 0; i < devices.Count; i++)
            {
                var dev = devices[i];
                if (i > 0)
                {
                    // Separator between device cards
                    var separator = new Separator
                    {
                        Margin = new Thickness(0, 8, 0, 8),
                        Height = 1,
                        Background = isDark ? new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(20, 0, 0, 0)),
                        BorderThickness = new Thickness(0)
                    };
                    _cardsStack.Children.Add(separator);
                }

                _cardsStack.Children.Add(BuildDeviceCard(dev, isDark));
            }
        }

        private FrameworkElement BuildDeviceCard(TaskbarDeviceState dev, bool isDark)
        {
            var cardGrid = new Grid
            {
                MinHeight = 64,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = dev.IsConnected ? 1.0 : 0.65
            };

            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (dev.SupportsSettings)
            {
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            }

            // Left icon
            var iconBlock = new TextBlock
            {
                Text = !string.IsNullOrEmpty(dev.IconGlyph) ? dev.IconGlyph : dev.GetDefaultIconGlyph(),
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 30,
                Foreground = isDark ? Brushes.White : Brushes.Black,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetColumn(iconBlock, 0);
            cardGrid.Children.Add(iconBlock);

            // Middle info panel
            var infoPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            };

            var titleBlock = new TextBlock
            {
                Text = dev.Name,
                Foreground = isDark ? Brushes.White : Brushes.Black,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 2)
            };
            infoPanel.Children.Add(titleBlock);

            var batteryRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 3)
            };

            var percentText = new TextBlock
            {
                Foreground = isDark ? Brushes.White : Brushes.Black,
                FontSize = 15.5,
                FontWeight = FontWeights.SemiBold,
                Text = dev.IsConnected && dev.BatteryPercent >= 0 ? (dev.BatteryPercent + "%") : "--%"
            };
            batteryRow.Children.Add(percentText);

            if (dev.IsConnected && dev.IsCharging)
            {
                var chargingPill = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 1, 6, 2),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = new SolidColorBrush(isDark ? Color.FromArgb(40, 30, 215, 96) : Color.FromArgb(30, 16, 124, 65)),
                    BorderBrush = new SolidColorBrush(isDark ? Color.FromArgb(120, 30, 215, 96) : Color.FromArgb(100, 16, 124, 65)),
                    BorderThickness = new Thickness(1),
                    Child = new TextBlock
                    {
                        Text = "⚡ Charging",
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(isDark ? Color.FromRgb(50, 230, 110) : Color.FromRgb(16, 124, 65))
                    }
                };
                batteryRow.Children.Add(chargingPill);
            }
            infoPanel.Children.Add(batteryRow);

            if (dev.IsConnected && dev.BatteryPercent >= 0)
            {
                // Progress bar
                var progressBar = new Grid
                {
                    Height = 4,
                    Margin = new Thickness(0, 0, 0, 3)
                };
                var barBg = new Border
                {
                    CornerRadius = new CornerRadius(2),
                    Background = isDark ? new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(20, 0, 0, 0))
                };
                double maxBarWidth = 190.0;
                double fillWidth = Math.Max(0, Math.Min(maxBarWidth, maxBarWidth * (dev.BatteryPercent / 100.0)));
                var barFill = new Border
                {
                    CornerRadius = new CornerRadius(2),
                    Background = dev.IsCharging ?
                        new SolidColorBrush(Color.FromRgb(30, 215, 96)) :
                        (dev.BatteryPercent <= 20 ? new SolidColorBrush(Color.FromRgb(225, 40, 40)) : (isDark ? Brushes.White : new SolidColorBrush(Color.FromRgb(26, 26, 26)))),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Width = fillWidth
                };
                progressBar.Children.Add(barBg);
                progressBar.Children.Add(barFill);
                infoPanel.Children.Add(progressBar);
            }

            // Subtitle / Time / Status
            string subText;
            if (!dev.IsConnected)
            {
                subText = "Disconnected or sleeping";
            }
            else if (dev.IsCharging)
            {
                subText = dev.TimeToFullMin > 0 ?
                    string.Format("Time to full: ~{0}h {1}m", dev.TimeToFullMin / 60, dev.TimeToFullMin % 60) :
                    "⚡ Charging via USB...";
            }
            else
            {
                if (dev.TimeToEmptyMin > 0)
                {
                    subText = string.Format("Approx. {0}h {1}m remaining", dev.TimeToEmptyMin / 60, dev.TimeToEmptyMin % 60);
                }
                else if (dev.Category == OmniHid.Core.Abstractions.DeviceCategory.Headset)
                {
                    double maxHours = GetModelMaxBatteryHours(dev.Name);
                    double estimatedHours = maxHours * (dev.BatteryPercent / 100.0);
                    int hours = (int)estimatedHours;
                    int minutes = (int)((estimatedHours - hours) * 60);
                    subText = string.Format("Approx. {0}h {1}m remaining", hours, minutes);
                }
                else
                {
                    subText = !string.IsNullOrEmpty(dev.StatusText) ? dev.StatusText : "Wireless";
                }
            }

            var timeBlock = new TextBlock
            {
                Text = subText,
                Foreground = isDark ? new SolidColorBrush(Color.FromRgb(165, 165, 165)) : Brushes.Gray,
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap
            };
            infoPanel.Children.Add(timeBlock);

            Grid.SetColumn(infoPanel, 1);
            cardGrid.Children.Add(infoPanel);

            // Settings gear button if device supports settings
            if (dev.SupportsSettings)
            {
                var gearBtn = new Border
                {
                    Width = 22,
                    Height = 22,
                    CornerRadius = new CornerRadius(4),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Cursor = Cursors.Hand,
                    ToolTip = "Sleep Timer Settings",
                    Background = Brushes.Transparent,
                    Child = new TextBlock
                    {
                        Text = "\uE713",
                        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                        FontSize = 13,
                        Foreground = isDark ? new SolidColorBrush(Color.FromRgb(150, 150, 150)) : Brushes.Gray,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };

                gearBtn.MouseEnter += (s, e) =>
                {
                    bool currentDark = _owner != null ? _owner.IsDarkTheme : true;
                    gearBtn.Background = currentDark ? new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
                };
                gearBtn.MouseLeave += (s, e) =>
                {
                    gearBtn.Background = Brushes.Transparent;
                };

                string devId = dev.Id;
                gearBtn.MouseLeftButtonUp += (s, e) =>
                {
                    _activeSettingsDeviceId = devId;
                    _cardsStack.Visibility = Visibility.Collapsed;
                    _settingsView.Visibility = Visibility.Visible;
                };

                Grid.SetColumn(gearBtn, 2);
                cardGrid.Children.Add(gearBtn);
            }

            return cardGrid;
        }

        private static double GetModelMaxBatteryHours(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return 50.0;
            string lower = deviceName.ToLowerInvariant();
            if (lower.Contains("pro x 2") || lower.Contains("0x0af7")) return 50.0;
            if (lower.Contains("alpha wireless")) return 300.0;
            if (lower.Contains("cloud 3") || lower.Contains("cloud iii")) return 120.0;
            if (lower.Contains("cloud 2") || lower.Contains("cloud ii") || lower.Contains("flight")) return 30.0;
            if (lower.Contains("nova 7")) return 38.0;
            if (lower.Contains("nova 5")) return 60.0;
            if (lower.Contains("nova pro")) return 22.0;
            if (lower.Contains("g733")) return 29.0;
            if (lower.Contains("g535")) return 33.0;
            return 50.0;
        }

        public void UpdateTheme(bool isDark)
        {
            try
            {
                _rootBorder.Background = new SolidColorBrush(isDark ? Color.FromArgb(245, 28, 28, 28) : Color.FromArgb(248, 250, 250, 250));
                _rootBorder.BorderBrush = new SolidColorBrush(isDark ? Color.FromArgb(80, 255, 255, 255) : Color.FromArgb(60, 0, 0, 0));

                var brush = isDark ? Brushes.White : Brushes.Black;
                _backIcon.Foreground = brush;
                _settingsTitle.Foreground = brush;

                for (int i = 0; i < _sleepButtonBorders.Count; i++)
                {
                    _sleepButtonBorders[i].Background = isDark ? new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(20, 0, 0, 0));
                    _sleepButtonBorders[i].BorderBrush = isDark ? new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
                    _sleepButtonTexts[i].Foreground = brush;
                }

                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    DwmHelper.SetDarkMode(hwnd, isDark);
                }

                if (_owner != null) UpdateData(_owner.LatestDevices);
            }
            catch { }
        }

        public void UpdateClampedPosition()
        {
            try
            {
                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                IntPtr ownerHwnd = _owner != null ? new System.Windows.Interop.WindowInteropHelper(_owner).Handle : IntPtr.Zero;

                var source = PresentationSource.FromVisual(this);
                double dpiX = source != null ? source.CompositionTarget.TransformToDevice.M11 : 1.0;
                double dpiY = source != null ? source.CompositionTarget.TransformToDevice.M22 : 1.0;

                IntPtr hMonitor = MonitorFromWindow(ownerHwnd != IntPtr.Zero ? ownerHwnd : hwnd, 2);
                OverlayWindow.MONITORINFO mi = new OverlayWindow.MONITORINFO();
                mi.cbSize = Marshal.SizeOf(mi);

                double workLeft, workTop, workRight, workBottom;
                if (hMonitor != IntPtr.Zero && GetMonitorInfo(hMonitor, ref mi))
                {
                    workLeft = mi.rcWork.Left / dpiX;
                    workTop = mi.rcWork.Top / dpiY;
                    workRight = mi.rcWork.Right / dpiX;
                    workBottom = mi.rcWork.Bottom / dpiY;
                }
                else
                {
                    workLeft = SystemParameters.WorkArea.Left;
                    workTop = SystemParameters.WorkArea.Top;
                    workRight = SystemParameters.WorkArea.Right;
                    workBottom = SystemParameters.WorkArea.Bottom;
                }

                double width = this.ActualWidth > 0 ? this.ActualWidth : this.Width;
                double height = this.ActualHeight > 0 ? this.ActualHeight : 140;

                double ownerCenterX = _owner != null ? (_owner.Left + (_owner.ActualWidth > 0 ? _owner.ActualWidth : _owner.Width) / 2.0) : (workRight - width / 2.0);
                double desiredLeft = ownerCenterX - (width / 2.0);

                double ownerTop = _owner != null ? _owner.Top : (workBottom - 36);
                double ownerHeight = _owner != null ? (_owner.ActualHeight > 0 ? _owner.ActualHeight : _owner.Height) : 36;

                double desiredTop = ownerTop - height - 8.0;
                if (desiredTop < workTop + 8.0)
                {
                    desiredTop = ownerTop + ownerHeight + 8.0;
                }

                const double margin = 8.0;
                double minX = workLeft + margin;
                double maxX = workRight - width - margin;
                double left = desiredLeft;
                if (maxX >= minX)
                {
                    if (left < minX) left = minX;
                    if (left > maxX) left = maxX;
                }

                double minY = workTop + margin;
                double maxY = workBottom - height - margin;
                double top = desiredTop;
                if (maxY >= minY)
                {
                    if (top < minY) top = minY;
                    if (top > maxY) top = maxY;
                }

                this.Left = left;
                this.Top = top;
            }
            catch { }
        }

        public void ShowFlyout(List<TaskbarDeviceState> devices)
        {
            try
            {
                UpdateData(devices);
                bool isDark = _owner != null ? _owner.IsDarkTheme : true;
                UpdateTheme(isDark);

                _settingsView.Visibility = Visibility.Collapsed;
                _statusFeedbackText.Visibility = Visibility.Collapsed;
                _cardsStack.Visibility = Visibility.Visible;

                DwmHelper.BoostCompositorClock(true);

                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    DwmHelper.EnableWindowTransitions(hwnd, false);
                    DwmHelper.CloakWindow(hwnd, true);
                }

                this.Visibility = Visibility.Visible;
                this.Show();

                this.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                {
                    UpdateClampedPosition();

                    var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                    if (h != IntPtr.Zero)
                    {
                        DwmHelper.CloakWindow(h, false);
                        DwmHelper.SetDarkMode(h, isDark);
                        SetForegroundWindow(h);
                    }
                    this.Activate();
                    this.Focus();

                    DwmHelper.BoostCompositorClock(false);
                }));
            }
            catch { }
        }

        public void HideFlyout()
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    DwmHelper.CloakWindow(hwnd, true);
                }
                this.Visibility = Visibility.Collapsed;
                this.Hide();
            }
            catch { }
        }
    }
}
