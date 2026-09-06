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

namespace OmniHidTaskbar.UI
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Windows 11 Fluent Flyout Popup Window
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Interactive Windows 11-styled Fluent Flyout detailing connected peripherals,
    /// charging telemetry, estimated battery runtime, battery bars, and device controls.
    /// </summary>
    /// <remarks>
    /// Anchored dynamically above the taskbar widget with a 14 DIP spacing gap.
    /// Employs a cloaked layout render pass and DirectComposition compositor clock boost
    /// to eliminate visual pop-in, window flicker, or misplaced frames.
    /// </remarks>
    public class FlyoutWindow : Window
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Win32 Interop & Monitor Geometry
        // ═══════════════════════════════════════════════════════════════════════

        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        static extern bool GetMonitorInfo(IntPtr hMonitor, ref TaskbarHelper.MONITORINFO lpmi);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        private readonly OverlayWindow _owner;
        private readonly Border _rootBorder;
        private readonly StackPanel _cardsStack;
        private List<TaskbarDeviceState> _currentDevices = new List<TaskbarDeviceState>();

        // ═══════════════════════════════════════════════════════════════════════
        // Drag-and-Drop Reordering State
        // ═══════════════════════════════════════════════════════════════════════

        private Border _draggingCardBorder = null;
        private int _dragSourceIndex = -1;
        private Point _dragStartPos;
        private bool _isDraggingActive = false;


        // ═══════════════════════════════════════════════════════════════════════
        // Constructor & Initialization
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Initializes a new instance of <see cref="FlyoutWindow"/> with acrylic styling and device cards.
        /// </summary>
        /// <param name="owner">The parent taskbar overlay window instance.</param>
        /// <param name="devices">Initial snapshot of connected peripheral telemetry states.</param>
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
                Background = isDark ? new SolidColorBrush(Color.FromArgb(240, 28, 28, 28)) : new SolidColorBrush(Color.FromArgb(246, 250, 250, 250)),
                BorderBrush = DwmHelper.GetSubtleBorderBrush(isDark),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                Margin = new Thickness(6) // 6 DIP window border margin allows drop shadow without clipping
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

            _cardsStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Center,
                MinHeight = 78
            };

            _rootBorder.Child = _cardsStack;
            Content = _rootBorder;

            if (devices != null)
            {
                UpdateData(devices);
            }

            this.Deactivated += (s, e) => HideFlyout();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Telemetry Data Binding & Card Composition
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Updates the list of displayed devices, re-rendering cards and triggering dynamic height recalculation.
        /// </summary>
        /// <param name="devices">Current snapshot of peripheral telemetry states.</param>
        public void UpdateData(List<TaskbarDeviceState> devices)
        {
            var visible = _owner != null ? _owner.GetVisibleDevices(devices) : devices;
            _currentDevices = visible != null ? new List<TaskbarDeviceState>(visible) : new List<TaskbarDeviceState>();

            _cardsStack.Children.Clear();
            bool isDark = _owner != null ? _owner.IsDarkTheme : true;

            if (_currentDevices.Count == 0)
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

            for (int i = 0; i < _currentDevices.Count; i++)
            {
                var dev = _currentDevices[i];
                var cardContainer = BuildDeviceCard(dev, isDark, i == _currentDevices.Count - 1);
                _cardsStack.Children.Add(cardContainer);
            }

            if (this.IsVisible)
            {
                this.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateClampedPosition));
            }
        }

        /// <summary>
        /// Composes an individual Fluent interactive card displaying peripheral icon, name,
        /// live battery gauge, charging pill, dynamic progress bar, estimated runtime, inline renaming, and drag handle.
        /// </summary>
        /// <param name="dev">Peripheral state model.</param>
        /// <param name="isDark"><c>true</c> if dark theme is active.</param>
        /// <param name="isLast"><c>true</c> if card is the last child in the list.</param>
        /// <returns>A populated WPF element tree representing the device card container.</returns>
        private FrameworkElement BuildDeviceCard(TaskbarDeviceState dev, bool isDark, bool isLast)
        {
            var cardContainer = new Border
            {
                Padding = new Thickness(0, 4, 0, 8),
                BorderThickness = isLast ? new Thickness(0) : new Thickness(0, 0, 0, 1),
                BorderBrush = isDark
                    ? new SolidColorBrush(Color.FromArgb(35, 255, 255, 255))
                    : new SolidColorBrush(Color.FromArgb(20, 0, 0, 0)),
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(6)
            };

            var cardGrid = new Grid
            {
                MinHeight = 64,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = dev.IsConnected ? 1.0 : 0.65
            };

            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });

            // Left icon
            var iconBlock = new TextBlock
            {
                Text = !string.IsNullOrEmpty(dev.IconGlyph) ? dev.IconGlyph : dev.GetDefaultIconGlyph(),
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 30,
                Foreground = DwmHelper.GetPrimaryTextBrush(isDark),
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

            // Title row with inline rename support
            var titleRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 1)
            };

            var titleBlock = new TextBlock
            {
                Text = dev.DisplayName,
                Foreground = DwmHelper.GetPrimaryTextBrush(isDark),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 160,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = "Click to rename device"
            };
            titleRow.Children.Add(titleBlock);

            var editBtn = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = "Rename device",
                Background = Brushes.Transparent,
                Child = new TextBlock
                {
                    Text = "\uE70F", // Segoe MDL2 Edit pencil
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    FontSize = 10.5,
                    Foreground = isDark ? new SolidColorBrush(Color.FromRgb(145, 145, 145)) : Brushes.Gray,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            editBtn.MouseEnter += (s, e) =>
            {
                bool curDark = _owner != null ? _owner.IsDarkTheme : true;
                editBtn.Background = curDark ? new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
            };
            editBtn.MouseLeave += (s, e) =>
            {
                editBtn.Background = Brushes.Transparent;
            };
            titleRow.Children.Add(editBtn);


            infoPanel.Children.Add(titleRow);

            // Inline rename editor TextBox (hidden by default)
            var renameBox = new TextBox
            {
                Text = dev.DisplayName,
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Margin = new Thickness(0, 0, 0, 3),
                Visibility = Visibility.Collapsed,
                Height = 22,
                Padding = new Thickness(3, 1, 3, 1),
                Background = isDark ? new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(20, 0, 0, 0)),
                Foreground = isDark ? Brushes.White : Brushes.Black,
                BorderBrush = isDark ? new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
                BorderThickness = new Thickness(1)
            };
            infoPanel.Children.Add(renameBox);

            // If a custom nickname is active, show original model name as a subtle secondary label with small indent
            if (!string.IsNullOrWhiteSpace(dev.CustomName))
            {
                var originalModelBlock = new TextBlock
                {
                    Text = dev.Name,
                    Foreground = isDark ? new SolidColorBrush(Color.FromRgb(140, 140, 140)) : new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                    FontSize = 10,
                    Margin = new Thickness(8, 0, 0, 2),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 175
                };
                infoPanel.Children.Add(originalModelBlock);
            }

            Action startRename = () =>
            {
                titleRow.Visibility = Visibility.Collapsed;
                renameBox.Visibility = Visibility.Visible;
                renameBox.Text = dev.DisplayName;
                renameBox.SelectAll();
                renameBox.Focus();
            };

            titleBlock.MouseLeftButtonUp += (s, e) => startRename();
            editBtn.MouseLeftButtonUp += (s, e) => startRename();

            Action finishRename = () =>
            {
                if (renameBox.Visibility != Visibility.Visible) return;
                renameBox.Visibility = Visibility.Collapsed;
                titleRow.Visibility = Visibility.Visible;

                string newName = renameBox.Text.Trim();
                if (string.Equals(newName, dev.Name, StringComparison.OrdinalIgnoreCase))
                {
                    newName = null; // Revert to factory default
                }

                if (_owner != null)
                {
                    _owner.SetDeviceCustomName(dev.Id, newName);
                    UpdateData(_owner.VisibleDevices);
                }
            };

            renameBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    finishRename();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    renameBox.Visibility = Visibility.Collapsed;
                    titleRow.Visibility = Visibility.Visible;
                    e.Handled = true;
                }
            };

            renameBox.LostFocus += (s, e) =>
            {
                finishRename();
            };

            var batteryRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 3)
            };

            var percentText = new TextBlock
            {
                Foreground = DwmHelper.GetPrimaryTextBrush(isDark),
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

                int pct = Math.Max(0, Math.Min(100, dev.BatteryPercent));
                progressBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pct, GridUnitType.Star) });
                progressBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - pct, GridUnitType.Star) });

                var barBg = new Border
                {
                    CornerRadius = new CornerRadius(2),
                    Background = isDark ? new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(20, 0, 0, 0))
                };
                Grid.SetColumnSpan(barBg, 2);
                progressBar.Children.Add(barBg);

                if (pct > 0)
                {
                    var barFill = new Border
                    {
                        CornerRadius = new CornerRadius(2),
                        Background = dev.IsCharging ?
                            new SolidColorBrush(Color.FromRgb(30, 215, 96)) :
                            (dev.BatteryPercent <= 20 ? new SolidColorBrush(Color.FromRgb(225, 40, 40)) : DwmHelper.GetAccentBrush(isDark)),
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    Grid.SetColumn(barFill, 0);
                    progressBar.Children.Add(barFill);
                }

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
                Foreground = DwmHelper.GetSecondaryTextBrush(isDark),
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap
            };
            infoPanel.Children.Add(timeBlock);

            Grid.SetColumn(infoPanel, 1);
            cardGrid.Children.Add(infoPanel);

            // Right-side actions grid: Hide eye button in top-right corner, Drag handle in center
            var rightColumn = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch,
                Width = 22
            };
            rightColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rightColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Hide from taskbar button (crossed eye) in top-right corner, on same level as title
            var hideBtn = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(3),
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Right,
                Cursor = Cursors.Hand,
                ToolTip = "Hide device from taskbar",
                Background = Brushes.Transparent,
                Child = new TextBlock
                {
                    Text = "\uED1A", // Segoe MDL2 Hide glyph
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    FontSize = 11.5,
                    Foreground = isDark ? new SolidColorBrush(Color.FromRgb(140, 140, 140)) : Brushes.Gray,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            hideBtn.MouseEnter += (s, e) =>
            {
                bool currentDark = _owner != null ? _owner.IsDarkTheme : true;
                hideBtn.Background = currentDark ? new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
            };
            hideBtn.MouseLeave += (s, e) =>
            {
                hideBtn.Background = Brushes.Transparent;
            };
            string targetHideKey = !string.IsNullOrEmpty(dev.Id) ? dev.Id : dev.Name;
            hideBtn.MouseLeftButtonUp += (s, e) =>
            {
                if (_owner != null)
                {
                    _owner.SetDeviceVisibility(targetHideKey, false);
                }
            };
            Grid.SetRow(hideBtn, 0);
            rightColumn.Children.Add(hideBtn);

            // Drag handle with 6 vector dots (2 columns x 3 rows) centered vertically
            var dragHandle = new Border
            {
                Width = 20,
                Height = 26,
                CornerRadius = new CornerRadius(3),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Cursor = Cursors.SizeAll,
                ToolTip = "Drag to reorder",
                Background = Brushes.Transparent
            };

            var dotBrush = isDark ? new SolidColorBrush(Color.FromRgb(140, 140, 140)) : new SolidColorBrush(Color.FromRgb(150, 150, 150));
            var gripperCanvas = new Canvas
            {
                Width = 7,
                Height = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };

            var dotEllipses = new List<System.Windows.Shapes.Ellipse>();
            double[] dotY = new double[] { 0, 5, 10 };
            for (int r = 0; r < 3; r++)
            {
                var dot1 = new System.Windows.Shapes.Ellipse
                {
                    Width = 2.5,
                    Height = 2.5,
                    Fill = dotBrush
                };
                Canvas.SetLeft(dot1, 0);
                Canvas.SetTop(dot1, dotY[r]);
                gripperCanvas.Children.Add(dot1);
                dotEllipses.Add(dot1);

                var dot2 = new System.Windows.Shapes.Ellipse
                {
                    Width = 2.5,
                    Height = 2.5,
                    Fill = dotBrush
                };
                Canvas.SetLeft(dot2, 4.5);
                Canvas.SetTop(dot2, dotY[r]);
                gripperCanvas.Children.Add(dot2);
                dotEllipses.Add(dot2);
            }
            dragHandle.Child = gripperCanvas;

            dragHandle.MouseEnter += (s, e) =>
            {
                bool currentDark = _owner != null ? _owner.IsDarkTheme : true;
                dragHandle.Background = currentDark ? new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
                var activeBrush = currentDark ? Brushes.White : Brushes.Black;
                for (int i = 0; i < dotEllipses.Count; i++)
                {
                    dotEllipses[i].Fill = activeBrush;
                }
            };
            dragHandle.MouseLeave += (s, e) =>
            {
                if (!dragHandle.IsMouseCaptured)
                {
                    dragHandle.Background = Brushes.Transparent;
                    bool currentDark = _owner != null ? _owner.IsDarkTheme : true;
                    var idleBrush = currentDark ? new SolidColorBrush(Color.FromRgb(140, 140, 140)) : new SolidColorBrush(Color.FromRgb(150, 150, 150));
                    for (int i = 0; i < dotEllipses.Count; i++)
                    {
                        dotEllipses[i].Fill = idleBrush;
                    }
                }
            };

            dragHandle.PreviewMouseLeftButtonDown += (s, e) =>
            {
                _dragSourceIndex = _cardsStack.Children.IndexOf(cardContainer);
                if (_dragSourceIndex < 0) return;
                _dragStartPos = e.GetPosition(_cardsStack);
                _draggingCardBorder = cardContainer;
                _isDraggingActive = false;
                dragHandle.CaptureMouse();
                e.Handled = true;
            };

            dragHandle.PreviewMouseMove += (s, e) =>
            {
                if (!dragHandle.IsMouseCaptured || _draggingCardBorder == null || _dragSourceIndex < 0) return;

                Point cur = e.GetPosition(_cardsStack);
                double deltaY = cur.Y - _dragStartPos.Y;

                if (!_isDraggingActive && Math.Abs(deltaY) > 4)
                {
                    _isDraggingActive = true;
                    _draggingCardBorder.Opacity = 0.55;
                }

                if (_isDraggingActive)
                {
                    int targetIdx = -1;
                    for (int j = 0; j < _cardsStack.Children.Count; j++)
                    {
                        var child = _cardsStack.Children[j] as FrameworkElement;
                        if (child == null) continue;
                        Point pt = child.TranslatePoint(new Point(0, 0), _cardsStack);
                        if (cur.Y >= pt.Y && cur.Y <= pt.Y + child.ActualHeight)
                        {
                            targetIdx = j;
                            break;
                        }
                    }

                    if (targetIdx >= 0 && targetIdx != _dragSourceIndex && targetIdx < _currentDevices.Count)
                    {
                        _cardsStack.Children.Remove(_draggingCardBorder);
                        _cardsStack.Children.Insert(targetIdx, _draggingCardBorder);

                        var movedDev = _currentDevices[_dragSourceIndex];
                        _currentDevices.RemoveAt(_dragSourceIndex);
                        _currentDevices.Insert(targetIdx, movedDev);

                        _dragSourceIndex = targetIdx;
                        RefreshCardSeparators(isDark);

                        if (_owner != null)
                        {
                            _owner.UpdateDeviceOrder(_currentDevices.Select(d => d.Id).ToList());
                        }
                    }
                }
            };

            dragHandle.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (dragHandle.IsMouseCaptured)
                {
                    dragHandle.ReleaseMouseCapture();
                }

                if (_draggingCardBorder != null)
                {
                    _draggingCardBorder.Opacity = 1.0;
                    _draggingCardBorder = null;
                }

                if (_isDraggingActive)
                {
                    _isDraggingActive = false;
                    _dragSourceIndex = -1;
                    RefreshCardSeparators(isDark);

                    if (_owner != null)
                    {
                        _owner.UpdateDeviceOrder(_currentDevices.Select(d => d.Id).ToList());
                    }
                }
            };

            Grid.SetRow(dragHandle, 1);
            rightColumn.Children.Add(dragHandle);

            Grid.SetColumn(rightColumn, 2);
            cardGrid.Children.Add(rightColumn);

            cardContainer.Child = cardGrid;
            return cardContainer;
        }

        /// <summary>
        /// Re-applies bottom separator borders to child card containers based on their current visual stack order.
        /// </summary>
        /// <param name="isDark"><c>true</c> if dark theme is currently active.</param>
        private void RefreshCardSeparators(bool isDark)
        {
            var borderBrush = DwmHelper.GetSubtleBorderBrush(isDark);
            for (int i = 0; i < _cardsStack.Children.Count; i++)
            {
                var border = _cardsStack.Children[i] as Border;
                if (border != null)
                {
                    bool isLast = (i == _cardsStack.Children.Count - 1);
                    border.BorderThickness = isLast ? new Thickness(0) : new Thickness(0, 0, 0, 1);
                    border.BorderBrush = borderBrush;
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Hardware Battery Runtime Estimation
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Retrieves manufacturer rated battery endurance hours for known wireless headsets
        /// when firmware protocol does not report exact minutes remaining.
        /// </summary>
        /// <param name="deviceName">Peripheral model name.</param>
        /// <returns>Rated battery endurance in hours.</returns>
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

        // ═══════════════════════════════════════════════════════════════════════
        // Theme & Visual Styling
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Updates foreground brushes, acrylic card background, and DWM window dark mode attributes.
        /// </summary>
        /// <param name="isDark"><c>true</c> for dark mode; <c>false</c> for light mode.</param>
        public void UpdateTheme(bool isDark)
        {
            try
            {
                _rootBorder.Background = isDark ? new SolidColorBrush(Color.FromArgb(240, 28, 28, 28)) : new SolidColorBrush(Color.FromArgb(246, 250, 250, 250));
                _rootBorder.BorderBrush = DwmHelper.GetSubtleBorderBrush(isDark);

                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    DwmHelper.SetDarkMode(hwnd, isDark);
                }

                if (_owner != null)
                {
                    UpdateData(_owner.VisibleDevices);
                }
                else if (_currentDevices != null)
                {
                    UpdateData(_currentDevices);
                }
            }
            catch { }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Positioning & Screen Geometry Clamping
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Re-computes DPI-aware screen coordinates, clamping the flyout within the active monitor's
        /// work area and maintaining a consistent 14 DIP spacing gap above the taskbar widget.
        /// </summary>
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
                TaskbarHelper.MONITORINFO mi = new TaskbarHelper.MONITORINFO();
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

        // ═══════════════════════════════════════════════════════════════════════
        // Window Presentation & Cloaking Lifecycle
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Displays the flyout using a two-phase cloaked render pass to guarantee pixel-perfect positioning
        /// and zero animation stutter or misplaced initial frames.
        /// </summary>
        /// <param name="devices">Current peripheral states to populate.</param>
        public void ShowFlyout(List<TaskbarDeviceState> devices)
        {
            try
            {
                bool isDark = _owner != null ? _owner.IsDarkTheme : true;
                UpdateTheme(isDark);
                if (devices != null)
                {
                    UpdateData(devices);
                }

                DwmHelper.BoostCompositorClock(true);

                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    DwmHelper.EnableWindowTransitions(hwnd, false);
                    DwmHelper.CloakWindow(hwnd, true); // Keep hidden during layout calculation
                }

                this.Visibility = Visibility.Visible;
                this.Show();

                // Uncloak and activate once the render tree has completed layout pass
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

        /// <summary>
        /// Cloaks and hides the flyout window, resetting active dragging and input states.
        /// </summary>
        public void HideFlyout()
        {
            try
            {
                if (_draggingCardBorder != null)
                {
                    _draggingCardBorder.Opacity = 1.0;
                    _draggingCardBorder = null;
                }
                _isDraggingActive = false;
                _dragSourceIndex = -1;

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
