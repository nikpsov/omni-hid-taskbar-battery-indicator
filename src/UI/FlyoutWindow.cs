using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
        private List<TaskbarDeviceState> _stashedDevices = null;

        // ═══════════════════════════════════════════════════════════════════════
        // Static Frozen Brushes & Typography Descriptors (Zero Heap Churn)
        // ═══════════════════════════════════════════════════════════════════════

        private static readonly FontFamily IconFontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        private static SolidColorBrush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static readonly SolidColorBrush FlyoutBackgroundDark = CreateFrozenBrush(Color.FromArgb(240, 28, 28, 28));
        private static readonly SolidColorBrush FlyoutBackgroundLight = CreateFrozenBrush(Color.FromArgb(246, 250, 250, 250));

        private static readonly SolidColorBrush CardSeparatorDark = CreateFrozenBrush(Color.FromArgb(35, 255, 255, 255));
        private static readonly SolidColorBrush CardSeparatorLight = CreateFrozenBrush(Color.FromArgb(20, 0, 0, 0));

        private static readonly SolidColorBrush RenameBoxBgDark = CreateFrozenBrush(Color.FromArgb(50, 255, 255, 255));
        private static readonly SolidColorBrush RenameBoxBgLight = CreateFrozenBrush(Color.FromArgb(20, 0, 0, 0));
        private static readonly SolidColorBrush RenameBoxBorderDark = CreateFrozenBrush(Color.FromArgb(90, 255, 255, 255));
        private static readonly SolidColorBrush RenameBoxBorderLight = CreateFrozenBrush(Color.FromArgb(60, 0, 0, 0));

        private static readonly SolidColorBrush ModelTextDark = CreateFrozenBrush(Color.FromRgb(140, 140, 140));
        private static readonly SolidColorBrush ModelTextLight = CreateFrozenBrush(Color.FromRgb(120, 120, 120));

        private static readonly SolidColorBrush ChargingPillBgDark = CreateFrozenBrush(Color.FromArgb(40, 30, 215, 96));
        private static readonly SolidColorBrush ChargingPillBgLight = CreateFrozenBrush(Color.FromArgb(30, 16, 124, 65));
        private static readonly SolidColorBrush ChargingPillBorderDark = CreateFrozenBrush(Color.FromArgb(120, 30, 215, 96));
        private static readonly SolidColorBrush ChargingPillBorderLight = CreateFrozenBrush(Color.FromArgb(100, 16, 124, 65));
        private static readonly SolidColorBrush ChargingPillFgDark = CreateFrozenBrush(Color.FromRgb(50, 230, 110));
        private static readonly SolidColorBrush ChargingPillFgLight = CreateFrozenBrush(Color.FromRgb(16, 124, 65));

        private static readonly SolidColorBrush WarningPillBgDark = CreateFrozenBrush(Color.FromArgb(40, 225, 40, 40));
        private static readonly SolidColorBrush WarningPillBgLight = CreateFrozenBrush(Color.FromArgb(25, 200, 30, 30));
        private static readonly SolidColorBrush WarningPillBorderDark = CreateFrozenBrush(Color.FromArgb(120, 225, 40, 40));
        private static readonly SolidColorBrush WarningPillBorderLight = CreateFrozenBrush(Color.FromArgb(90, 200, 30, 30));
        private static readonly SolidColorBrush WarningPillFgDark = CreateFrozenBrush(Color.FromRgb(255, 100, 100));
        private static readonly SolidColorBrush WarningPillFgLight = CreateFrozenBrush(Color.FromRgb(190, 30, 30));

        private static readonly SolidColorBrush WiredPillBgDark = CreateFrozenBrush(Color.FromArgb(40, 0, 120, 215));
        private static readonly SolidColorBrush WiredPillBgLight = CreateFrozenBrush(Color.FromArgb(25, 0, 100, 190));
        private static readonly SolidColorBrush WiredPillBorderDark = CreateFrozenBrush(Color.FromArgb(110, 0, 120, 215));
        private static readonly SolidColorBrush WiredPillBorderLight = CreateFrozenBrush(Color.FromArgb(80, 0, 100, 190));
        private static readonly SolidColorBrush WiredPillFgDark = CreateFrozenBrush(Color.FromRgb(70, 175, 255));
        private static readonly SolidColorBrush WiredPillFgLight = CreateFrozenBrush(Color.FromRgb(0, 110, 200));

        private static readonly SolidColorBrush NeutralPillBgDark = CreateFrozenBrush(Color.FromArgb(25, 255, 255, 255));
        private static readonly SolidColorBrush NeutralPillBgLight = CreateFrozenBrush(Color.FromArgb(15, 0, 0, 0));
        private static readonly SolidColorBrush NeutralPillBorderDark = CreateFrozenBrush(Color.FromArgb(45, 255, 255, 255));
        private static readonly SolidColorBrush NeutralPillBorderLight = CreateFrozenBrush(Color.FromArgb(30, 0, 0, 0));
        private static readonly SolidColorBrush NeutralPillFgDark = CreateFrozenBrush(Color.FromRgb(210, 210, 210));
        private static readonly SolidColorBrush NeutralPillFgLight = CreateFrozenBrush(Color.FromRgb(90, 90, 90));

        private static readonly SolidColorBrush OfflinePillBgDark = CreateFrozenBrush(Color.FromArgb(18, 255, 255, 255));
        private static readonly SolidColorBrush OfflinePillBgLight = CreateFrozenBrush(Color.FromArgb(10, 0, 0, 0));
        private static readonly SolidColorBrush OfflinePillBorderDark = CreateFrozenBrush(Color.FromArgb(35, 255, 255, 255));
        private static readonly SolidColorBrush OfflinePillBorderLight = CreateFrozenBrush(Color.FromArgb(25, 0, 0, 0));
        private static readonly SolidColorBrush OfflinePillFgDark = CreateFrozenBrush(Color.FromRgb(150, 150, 150));
        private static readonly SolidColorBrush OfflinePillFgLight = CreateFrozenBrush(Color.FromRgb(130, 130, 130));

        private static readonly SolidColorBrush ProgressBarBgDark = CreateFrozenBrush(Color.FromArgb(35, 255, 255, 255));
        private static readonly SolidColorBrush ProgressBarBgLight = CreateFrozenBrush(Color.FromArgb(20, 0, 0, 0));
        private static readonly SolidColorBrush ChargingGreenBrush = CreateFrozenBrush(Color.FromRgb(30, 215, 96));
        private static readonly SolidColorBrush LowBatteryRedBrush = CreateFrozenBrush(Color.FromRgb(225, 40, 40));
        private static readonly SolidColorBrush WiredBarBrushDark = CreateFrozenBrush(Color.FromRgb(0, 120, 215));
        private static readonly SolidColorBrush WiredBarBrushLight = CreateFrozenBrush(Color.FromRgb(0, 114, 206));

        private static readonly SolidColorBrush ActionButtonDark = CreateFrozenBrush(Color.FromRgb(140, 140, 140));
        private static readonly SolidColorBrush ActionButtonLight = CreateFrozenBrush(Color.FromRgb(150, 150, 150));
        private static readonly SolidColorBrush ActionButtonMidDark = CreateFrozenBrush(Color.FromRgb(145, 145, 145));
        private static readonly SolidColorBrush ButtonHoverDark = CreateFrozenBrush(Color.FromArgb(40, 255, 255, 255));
        private static readonly SolidColorBrush ButtonHoverLight = CreateFrozenBrush(Color.FromArgb(30, 0, 0, 0));
        private static readonly SolidColorBrush WarningYellowDark = CreateFrozenBrush(Color.FromRgb(255, 200, 60));
        private static readonly SolidColorBrush WarningYellowLight = CreateFrozenBrush(Color.FromRgb(180, 90, 0));
        private static readonly SolidColorBrush WarningYellowFill = CreateFrozenBrush(Color.FromRgb(255, 185, 0));
        private static readonly SolidColorBrush WarningBadgeFg = CreateFrozenBrush(Color.FromRgb(20, 20, 20));

        private static readonly bool HasWarningSolidGlyph = CheckHasGlyph(IconFontFamily, 0xF736);

        private static bool CheckHasGlyph(FontFamily family, int codepoint)
        {
            try
            {
                foreach (var typeface in family.GetTypefaces())
                {
                    GlyphTypeface glyphTypeface;
                    if (typeface.TryGetGlyphTypeface(out glyphTypeface))
                    {
                        if (glyphTypeface.CharacterToGlyphMap.ContainsKey(codepoint))
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Drag-and-Drop Reordering State & Animations
        // ═══════════════════════════════════════════════════════════════════════

        private Border _draggingCardBorder = null;
        private int _dragSourceIndex = -1;
        private int _currentDropIndex = -1;
        private Point _dragStartPos;
        private bool _isDraggingActive = false;
        private DateTime _lastDragEndTime = DateTime.MinValue;
        private double[] _dragInitialTops = null;
        private double[] _dragCardHeights = null;

        private static TranslateTransform GetOrCreateTranslateTransform(UIElement element)
        {
            var transform = element.RenderTransform as TranslateTransform;
            if (transform == null)
            {
                transform = new TranslateTransform();
                element.RenderTransform = transform;
            }
            return transform;
        }

        private static void AnimateTranslateY(UIElement element, double toY, int durationMs = 180)
        {
            var transform = GetOrCreateTranslateTransform(element);
            var anim = new DoubleAnimation
            {
                To = toY,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            transform.BeginAnimation(TranslateTransform.YProperty, anim);
        }


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
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            Width = 320;
            SizeToContent = SizeToContent.Height;

            bool isDark = _owner != null ? _owner.IsDarkTheme : true;

            _rootBorder = new Border
            {
                Background = isDark ? FlyoutBackgroundDark : FlyoutBackgroundLight,
                BorderBrush = DwmHelper.GetSubtleBorderBrush(isDark),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0)
            };

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
                UpdateData(devices, force: true);
            }

            this.Deactivated += (s, e) => HideFlyout();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Telemetry Data Binding & Card Composition
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Updates the list of displayed devices, re-rendering cards and triggering dynamic height recalculation.
        /// If the flyout is currently hidden, stashes device states to eliminate background WPF allocations.
        /// </summary>
        /// <param name="devices">Current snapshot of peripheral telemetry states.</param>
        /// <param name="force">If true, forces re-rendering even if the window is not currently visible.</param>
        public void UpdateData(List<TaskbarDeviceState> devices, bool force = false)
        {
            if (devices == null && _owner != null)
            {
                devices = _owner.VisibleDevices;
            }
            _stashedDevices = devices;
            if (!this.IsVisible && !force)
            {
                return;
            }

            if (_isDraggingActive || _draggingCardBorder != null)
            {
                return;
            }

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
                    FontFamily = IconFontFamily,
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
                Padding = new Thickness(0, 11, 0, 11),
                BorderThickness = isLast ? new Thickness(0) : new Thickness(0, 0, 0, 1),
                BorderBrush = isDark ? CardSeparatorDark : CardSeparatorLight,
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(6)
            };

            var cardGrid = new Grid
            {
                MinHeight = 56,
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
                FontFamily = IconFontFamily,
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

            // Title block with wrapping, rename click, and optional unverified warning icon
            var titleBlock = new TextBlock
            {
                Foreground = DwmHelper.GetPrimaryTextBrush(isDark),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = "Click to rename device",
                Margin = new Thickness(0, 0, 0, 1)
            };

            if (!dev.IsVerified)
            {
                var warningBadge = new Grid
                {
                    Width = 14,
                    Height = 14,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 5, 0),
                    ToolTip = "Experimental/Unverified profile loaded from unverified/",
                    Cursor = Cursors.Help
                };

                if (HasWarningSolidGlyph)
                {
                    // Solid yellow background triangle
                    var solidFill = new TextBlock
                    {
                        Text = "\uF736", // WarningSolid glyph
                        FontFamily = IconFontFamily,
                        FontSize = 12.5,
                        Foreground = WarningYellowFill,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    warningBadge.Children.Add(solidFill);

                    // Crisp dark outline and exclamation mark overlay
                    var outlineOverlay = new TextBlock
                    {
                        Text = "\uE7BA", // Warning outline + exclamation glyph
                        FontFamily = IconFontFamily,
                        FontSize = 12.5,
                        Foreground = WarningBadgeFg,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    warningBadge.Children.Add(outlineOverlay);
                }
                else
                {
                    // Fallback for environments lacking Segoe Fluent Icons: E7BA outline in yellow
                    var fallbackIcon = new TextBlock
                    {
                        Text = "\uE7BA",
                        FontFamily = IconFontFamily,
                        FontSize = 12.5,
                        Foreground = isDark ? WarningYellowDark : WarningYellowLight,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    warningBadge.Children.Add(fallbackIcon);
                }

                warningBadge.MouseLeftButtonUp += (s, e) => { e.Handled = true; };
                titleBlock.Inlines.Add(new InlineUIContainer(warningBadge) { BaselineAlignment = BaselineAlignment.Center });
            }

            titleBlock.Inlines.Add(new Run(dev.DisplayName));
            infoPanel.Children.Add(titleBlock);

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
                Background = isDark ? RenameBoxBgDark : RenameBoxBgLight,
                Foreground = isDark ? Brushes.White : Brushes.Black,
                BorderBrush = isDark ? RenameBoxBorderDark : RenameBoxBorderLight,
                BorderThickness = new Thickness(1)
            };
            infoPanel.Children.Add(renameBox);

            // If a custom nickname is active, show original model name as a secondary label without left indent
            if (!string.IsNullOrWhiteSpace(dev.CustomName))
            {
                var originalModelBlock = new TextBlock
                {
                    Text = dev.Name,
                    Foreground = isDark ? ModelTextDark : ModelTextLight,
                    FontSize = 10,
                    Margin = new Thickness(0, 0, 0, 2),
                    TextWrapping = TextWrapping.Wrap
                };
                infoPanel.Children.Add(originalModelBlock);
            }

            Action startRename = () =>
            {
                titleBlock.Visibility = Visibility.Collapsed;
                renameBox.Visibility = Visibility.Visible;
                renameBox.Text = dev.DisplayName;
                renameBox.SelectAll();
                renameBox.Focus();
            };

            titleBlock.MouseLeftButtonUp += (s, e) =>
            {
                if (_isDraggingActive || (DateTime.UtcNow - _lastDragEndTime).TotalMilliseconds < 350)
                {
                    e.Handled = true;
                    return;
                }
                startRename();
            };

            Action finishRename = () =>
            {
                if (renameBox.Visibility != Visibility.Visible) return;
                renameBox.Visibility = Visibility.Collapsed;
                titleBlock.Visibility = Visibility.Visible;

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
                    titleBlock.Visibility = Visibility.Visible;
                    e.Handled = true;
                }
            };

            renameBox.LostFocus += (s, e) =>
            {
                finishRename();
            };

            // Status pill badge built for the device (aligned to the right above the charging scale)
            var statusPill = BuildStatusPill(dev, isDark);
            statusPill.Margin = new Thickness(6, 0, 0, 0);

            // Battery percentage and status row (percent on left, status pill on right opposite to percent)
            var batteryRow = new Grid
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 4)
            };
            batteryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            batteryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var percentText = new TextBlock
            {
                Foreground = DwmHelper.GetPrimaryTextBrush(isDark),
                FontSize = 15.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Text = dev.IsConnected && dev.BatteryPercent >= 0 ? (dev.BatteryPercent + "%") : "--%"
            };
            Grid.SetColumn(percentText, 0);
            batteryRow.Children.Add(percentText);

            Grid.SetColumn(statusPill, 1);
            batteryRow.Children.Add(statusPill);

            infoPanel.Children.Add(batteryRow);

            if (dev.IsConnected && dev.BatteryPercent >= 0)
            {
                // Dynamic progress bar
                var progressBar = new Grid
                {
                    Height = 4,
                    Margin = new Thickness(0, 0, 0, 2)
                };

                int pct = Math.Max(0, Math.Min(100, dev.BatteryPercent));
                progressBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pct, GridUnitType.Star) });
                progressBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - pct, GridUnitType.Star) });

                var barBg = new Border
                {
                    CornerRadius = new CornerRadius(2),
                    Background = isDark ? ProgressBarBgDark : ProgressBarBgLight
                };
                Grid.SetColumnSpan(barBg, 2);
                progressBar.Children.Add(barBg);

                if (pct > 0)
                {
                    Brush barFillBrush;
                    if (dev.IsCharging)
                    {
                        barFillBrush = ChargingGreenBrush;
                    }
                    else if (dev.BatteryPercent <= 20)
                    {
                        barFillBrush = LowBatteryRedBrush;
                    }
                    else if (dev.IsWired)
                    {
                        barFillBrush = isDark ? WiredBarBrushDark : WiredBarBrushLight;
                    }
                    else
                    {
                        barFillBrush = DwmHelper.GetPrimaryTextBrush(isDark);
                    }

                    var barFill = new Border
                    {
                        CornerRadius = new CornerRadius(2),
                        Background = barFillBrush,
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    Grid.SetColumn(barFill, 0);
                    progressBar.Children.Add(barFill);
                }

                infoPanel.Children.Add(progressBar);
            }

            Grid.SetColumn(infoPanel, 1);
            cardGrid.Children.Add(infoPanel);

            // Right-side actions: Drag handle centered vertically (hide button removed to context menu)
            var rightColumn = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch,
                Width = 22
            };

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

            var dotBrush = isDark ? ActionButtonDark : ActionButtonLight;
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
                dragHandle.Background = currentDark ? ButtonHoverDark : ButtonHoverLight;
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
                    var idleBrush = currentDark ? ActionButtonDark : ActionButtonLight;
                    for (int i = 0; i < dotEllipses.Count; i++)
                    {
                        dotEllipses[i].Fill = idleBrush;
                    }
                }
            };

            dragHandle.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (_cardsStack.Children.Count <= 1) return;
                _dragSourceIndex = _cardsStack.Children.IndexOf(cardContainer);
                if (_dragSourceIndex < 0) return;

                _dragStartPos = e.GetPosition(_cardsStack);
                _draggingCardBorder = cardContainer;
                _isDraggingActive = false;
                _currentDropIndex = _dragSourceIndex;

                int count = _cardsStack.Children.Count;
                _dragInitialTops = new double[count];
                _dragCardHeights = new double[count];
                for (int j = 0; j < count; j++)
                {
                    var child = _cardsStack.Children[j] as FrameworkElement;
                    if (child != null)
                    {
                        Point pt = child.TranslatePoint(new Point(0, 0), _cardsStack);
                        _dragInitialTops[j] = pt.Y;
                        _dragCardHeights[j] = child.ActualHeight;
                    }
                }

                dragHandle.CaptureMouse();
                e.Handled = true;
            };

            dragHandle.PreviewMouseMove += (s, e) =>
            {
                if (!dragHandle.IsMouseCaptured || _draggingCardBorder == null || _dragSourceIndex < 0 || _dragInitialTops == null) return;

                Point cur = e.GetPosition(_cardsStack);
                double deltaY = cur.Y - _dragStartPos.Y;

                if (!_isDraggingActive && Math.Abs(deltaY) > 4)
                {
                    _isDraggingActive = true;
                    Panel.SetZIndex(_draggingCardBorder, 100);
                    _draggingCardBorder.Opacity = 0.88;
                }

                if (_isDraggingActive)
                {
                    var dragTransform = GetOrCreateTranslateTransform(_draggingCardBorder);
                    dragTransform.BeginAnimation(TranslateTransform.YProperty, null);
                    dragTransform.Y = deltaY;

                    double draggedCenterY = _dragInitialTops[_dragSourceIndex] + deltaY + (_dragCardHeights[_dragSourceIndex] / 2.0);

                    int count = _cardsStack.Children.Count;
                    int targetIdx = _dragSourceIndex;
                    for (int j = 0; j < count; j++)
                    {
                        double slotTop = _dragInitialTops[j];
                        double slotBottom = slotTop + _dragCardHeights[j];
                        if (draggedCenterY >= slotTop && draggedCenterY <= slotBottom)
                        {
                            targetIdx = j;
                            break;
                        }
                        if (draggedCenterY < slotTop && j == 0)
                        {
                            targetIdx = 0;
                            break;
                        }
                        if (draggedCenterY > slotBottom && j == count - 1)
                        {
                            targetIdx = count - 1;
                            break;
                        }
                    }

                    targetIdx = Math.Max(0, Math.Min(count - 1, targetIdx));

                    if (targetIdx != _currentDropIndex)
                    {
                        _currentDropIndex = targetIdx;

                        for (int j = 0; j < count; j++)
                        {
                            if (j == _dragSourceIndex) continue;

                            var otherCard = _cardsStack.Children[j] as UIElement;
                            if (otherCard == null) continue;

                            double shiftY = 0;
                            if (_currentDropIndex > _dragSourceIndex)
                            {
                                if (j > _dragSourceIndex && j <= _currentDropIndex)
                                {
                                    shiftY = -_dragCardHeights[_dragSourceIndex];
                                }
                            }
                            else if (_currentDropIndex < _dragSourceIndex)
                            {
                                if (j >= _currentDropIndex && j < _dragSourceIndex)
                                {
                                    shiftY = _dragCardHeights[_dragSourceIndex];
                                }
                            }

                            AnimateTranslateY(otherCard, shiftY, 180);
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

                _lastDragEndTime = DateTime.UtcNow;
                e.Handled = true;

                if (!_isDraggingActive || _draggingCardBorder == null || _dragSourceIndex < 0 || _dragInitialTops == null)
                {
                    if (_draggingCardBorder != null)
                    {
                        _draggingCardBorder.Opacity = 1.0;
                        Panel.SetZIndex(_draggingCardBorder, 0);
                        var trans = _draggingCardBorder.RenderTransform as TranslateTransform;
                        if (trans != null) trans.Y = 0;
                        _draggingCardBorder = null;
                    }
                    _isDraggingActive = false;
                    _dragSourceIndex = -1;
                    _currentDropIndex = -1;
                    _dragInitialTops = null;
                    _dragCardHeights = null;
                    return;
                }

                int fromIdx = _dragSourceIndex;
                int toIdx = _currentDropIndex;
                var cardToDrop = _draggingCardBorder;

                double finalTargetY = 0;
                if (toIdx > fromIdx)
                {
                    finalTargetY = (_dragInitialTops[toIdx] + _dragCardHeights[toIdx]) - (_dragInitialTops[fromIdx] + _dragCardHeights[fromIdx]);
                }
                else if (toIdx < fromIdx)
                {
                    finalTargetY = _dragInitialTops[toIdx] - _dragInitialTops[fromIdx];
                }
                else
                {
                    finalTargetY = 0;
                }

                var dragTrans = GetOrCreateTranslateTransform(cardToDrop);
                var dropAnim = new DoubleAnimation
                {
                    To = finalTargetY,
                    Duration = TimeSpan.FromMilliseconds(160),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                dropAnim.Completed += (animSender, animArgs) =>
                {
                    _lastDragEndTime = DateTime.UtcNow;
                    cardToDrop.Opacity = 1.0;
                    Panel.SetZIndex(cardToDrop, 0);

                    if (toIdx != fromIdx && toIdx >= 0 && toIdx < _cardsStack.Children.Count && toIdx < _currentDevices.Count)
                    {
                        _cardsStack.Children.Remove(cardToDrop);
                        _cardsStack.Children.Insert(toIdx, cardToDrop);

                        var movedDev = _currentDevices[fromIdx];
                        _currentDevices.RemoveAt(fromIdx);
                        _currentDevices.Insert(toIdx, movedDev);

                        RefreshCardSeparators(isDark);

                        if (_owner != null)
                        {
                            _owner.UpdateDeviceOrder(_currentDevices.Select(d => d.Id).ToList());
                        }
                    }

                    for (int j = 0; j < _cardsStack.Children.Count; j++)
                    {
                        var el = _cardsStack.Children[j] as UIElement;
                        if (el != null)
                        {
                            var tt = el.RenderTransform as TranslateTransform;
                            if (tt != null)
                            {
                                tt.BeginAnimation(TranslateTransform.YProperty, null);
                                tt.Y = 0;
                            }
                        }
                    }

                    _isDraggingActive = false;
                    _draggingCardBorder = null;
                    _dragSourceIndex = -1;
                    _currentDropIndex = -1;
                    _dragInitialTops = null;
                    _dragCardHeights = null;
                };

                dragTrans.BeginAnimation(TranslateTransform.YProperty, dropAnim);
            };

            rightColumn.Children.Add(dragHandle);

            Grid.SetColumn(rightColumn, 2);
            cardGrid.Children.Add(rightColumn);

            cardContainer.Child = cardGrid;
            return cardContainer;
        }

        /// <summary>
        /// Composes a Fluent-styled pill badge representing peripheral power, telemetry status,
        /// and estimated charge/discharge duration for the bottom card row.
        /// </summary>
        /// <param name="dev">Peripheral state model.</param>
        /// <param name="isDark"><c>true</c> if dark theme is currently active.</param>
        /// <returns>A configured WPF <see cref="Border"/> containing the colored status badge.</returns>
        private static Border BuildStatusPill(TaskbarDeviceState dev, bool isDark)
        {
            string pillText;
            SolidColorBrush bgBrush;
            SolidColorBrush borderBrush;
            SolidColorBrush fgBrush;

            if (!dev.IsConnected)
            {
                pillText = "Disconnected or sleeping";
                bgBrush = isDark ? OfflinePillBgDark : OfflinePillBgLight;
                borderBrush = isDark ? OfflinePillBorderDark : OfflinePillBorderLight;
                fgBrush = isDark ? OfflinePillFgDark : OfflinePillFgLight;
            }
            else if (dev.IsCharging)
            {
                // Format charging status: include estimated time to full if available
                if (dev.TimeToFullMin > 0)
                {
                    int chH = dev.TimeToFullMin / 60;
                    int chM = dev.TimeToFullMin % 60;
                    string chTime = chH > 0
                        ? (chM > 0 ? string.Format("{0}h {1}m", chH, chM) : string.Format("{0}h", chH))
                        : string.Format("{0}m", chM);
                    pillText = string.Format("⚡ Charging · ~{0} to full", chTime);
                }
                else
                {
                    pillText = "⚡ Charging";
                }
                bgBrush = isDark ? ChargingPillBgDark : ChargingPillBgLight;
                borderBrush = isDark ? ChargingPillBorderDark : ChargingPillBorderLight;
                fgBrush = isDark ? ChargingPillFgDark : ChargingPillFgLight;
            }
            else if (dev.IsWired && (dev.BatteryPercent >= 99 || dev.BatteryPercent == 100))
            {
                pillText = "Full (Wired)";
                bgBrush = isDark ? WiredPillBgDark : WiredPillBgLight;
                borderBrush = isDark ? WiredPillBorderDark : WiredPillBorderLight;
                fgBrush = isDark ? WiredPillFgDark : WiredPillFgLight;
            }
            else if (dev.IsWired)
            {
                pillText = "Wired";
                bgBrush = isDark ? WiredPillBgDark : WiredPillBgLight;
                borderBrush = isDark ? WiredPillBorderDark : WiredPillBorderLight;
                fgBrush = isDark ? WiredPillFgDark : WiredPillFgLight;
            }
            else if (dev.TimeToEmptyMin > 0)
            {
                int remH = dev.TimeToEmptyMin / 60;
                int remM = dev.TimeToEmptyMin % 60;
                string remTime = remH > 0
                    ? (remM > 0 ? string.Format("{0}h {1}m", remH, remM) : string.Format("{0}h", remH))
                    : string.Format("{0}m", remM);
                pillText = string.Format("~{0} left", remTime);
                if (dev.BatteryPercent <= 20)
                {
                    bgBrush = isDark ? WarningPillBgDark : WarningPillBgLight;
                    borderBrush = isDark ? WarningPillBorderDark : WarningPillBorderLight;
                    fgBrush = isDark ? WarningPillFgDark : WarningPillFgLight;
                }
                else
                {
                    bgBrush = isDark ? NeutralPillBgDark : NeutralPillBgLight;
                    borderBrush = isDark ? NeutralPillBorderDark : NeutralPillBorderLight;
                    fgBrush = isDark ? NeutralPillFgDark : NeutralPillFgLight;
                }
            }
            else if (dev.BatteryPercent >= 99 || dev.BatteryPercent == 100)
            {
                // Fully charged wireless device without remaining time: normal neutral styling
                pillText = "Fully charged";
                bgBrush = isDark ? NeutralPillBgDark : NeutralPillBgLight;
                borderBrush = isDark ? NeutralPillBorderDark : NeutralPillBorderLight;
                fgBrush = isDark ? NeutralPillFgDark : NeutralPillFgLight;
            }
            else
            {
                pillText = !string.IsNullOrEmpty(dev.StatusText) ? dev.StatusText : "Wireless";
                bgBrush = isDark ? NeutralPillBgDark : NeutralPillBgLight;
                borderBrush = isDark ? NeutralPillBorderDark : NeutralPillBorderLight;
                fgBrush = isDark ? NeutralPillFgDark : NeutralPillFgLight;
            }

            return new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1.5, 6, 2.5),
                Margin = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Background = bgBrush,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = pillText,
                    FontSize = 10,
                    FontWeight = FontWeights.Medium,
                    Foreground = fgBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };
        }

        /// <summary>
        /// Re-applies bottom separator borders to child card containers based on their current visual stack order.
        /// </summary>
        /// <param name="isDark"><c>true</c> if dark theme is currently active.</param>
        private void RefreshCardSeparators(bool isDark)
        {
            var borderBrush = isDark ? CardSeparatorDark : CardSeparatorLight;
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
                _rootBorder.Background = isDark ? FlyoutBackgroundDark : FlyoutBackgroundLight;
                _rootBorder.BorderBrush = DwmHelper.GetSubtleBorderBrush(isDark);

                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    DwmHelper.SetDarkMode(hwnd, isDark);
                }

                if (_owner != null)
                {
                    UpdateData(_owner.VisibleDevices, force: this.IsVisible);
                }
                else if (_currentDevices != null)
                {
                    UpdateData(_currentDevices, force: this.IsVisible);
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
                mi.cbSize = 40;

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
                    UpdateData(devices, force: true);
                }
                else if (_stashedDevices != null)
                {
                    UpdateData(_stashedDevices, force: true);
                }
                else if (_owner != null)
                {
                    UpdateData(_owner.VisibleDevices, force: true);
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
                if (Mouse.Captured != null)
                {
                    Mouse.Capture(null);
                }
                if (_draggingCardBorder != null)
                {
                    _draggingCardBorder.Opacity = 1.0;
                    Panel.SetZIndex(_draggingCardBorder, 0);
                    _draggingCardBorder = null;
                }
                if (_cardsStack != null)
                {
                    for (int j = 0; j < _cardsStack.Children.Count; j++)
                    {
                        var el = _cardsStack.Children[j] as UIElement;
                        if (el != null)
                        {
                            var tt = el.RenderTransform as TranslateTransform;
                            if (tt != null)
                            {
                                tt.BeginAnimation(TranslateTransform.YProperty, null);
                                tt.Y = 0;
                            }
                        }
                    }
                }
                _isDraggingActive = false;
                _dragSourceIndex = -1;
                _currentDropIndex = -1;
                _dragInitialTops = null;
                _dragCardHeights = null;

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
