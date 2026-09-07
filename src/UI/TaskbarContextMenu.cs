using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OmniHidTaskbar.Core;

namespace OmniHidTaskbar.UI
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Fluent Taskbar Context Menu Builder & Styling
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Constructs and styles Windows 11 Fluent-themed context menus for the taskbar widget and tray icon.
    /// Provides customized templates with rounded corners, translucent backdrop, hover animations, and cascading submenus.
    /// </summary>
    internal static class TaskbarContextMenu
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Style & Template Factories
        // ═══════════════════════════════════════════════════════════════════════

        private static Style _darkContextMenuStyle;
        private static Style _lightContextMenuStyle;
        private static Style _darkMenuItemStyle;
        private static Style _lightMenuItemStyle;
        private static Style _darkSubmenuHeaderStyle;
        private static Style _lightSubmenuHeaderStyle;
        private static volatile bool _isContextMenuOpen;

        /// <summary>
        /// Gets a value indicating whether the taskbar or tray context menu is currently open.
        /// </summary>
        public static bool IsOpen
        {
            get { return _isContextMenuOpen; }
        }

        /// <summary>
        /// Retrieves a cached <see cref="ContextMenu"/> style for dark or light theme.
        /// </summary>
        public static Style GetContextMenuStyle(bool isDark)
        {
            if (isDark)
            {
                if (_darkContextMenuStyle == null) _darkContextMenuStyle = CreateContextMenuStyle(true);
                return _darkContextMenuStyle;
            }
            if (_lightContextMenuStyle == null) _lightContextMenuStyle = CreateContextMenuStyle(false);
            return _lightContextMenuStyle;
        }

        /// <summary>
        /// Retrieves a cached <see cref="MenuItem"/> style for dark or light theme.
        /// </summary>
        public static Style GetMenuItemStyle(bool isDark)
        {
            if (isDark)
            {
                if (_darkMenuItemStyle == null) _darkMenuItemStyle = CreateMenuItemStyle(true);
                return _darkMenuItemStyle;
            }
            if (_lightMenuItemStyle == null) _lightMenuItemStyle = CreateMenuItemStyle(false);
            return _lightMenuItemStyle;
        }

        /// <summary>
        /// Retrieves a cached submenu header <see cref="MenuItem"/> style for dark or light theme.
        /// </summary>
        public static Style GetSubmenuHeaderStyle(bool isDark)
        {
            if (isDark)
            {
                if (_darkSubmenuHeaderStyle == null) _darkSubmenuHeaderStyle = CreateSubmenuHeaderStyle(true);
                return _darkSubmenuHeaderStyle;
            }
            if (_lightSubmenuHeaderStyle == null) _lightSubmenuHeaderStyle = CreateSubmenuHeaderStyle(false);
            return _lightSubmenuHeaderStyle;
        }

        /// <summary>
        /// Creates a modern Fluent-styled <see cref="ContextMenu"/> style with rounded corners,
        /// translucent backdrop, and soft drop shadow matching Windows 11 aesthetics.
        /// </summary>
        /// <param name="isDark">Indicates whether to generate dark or light theme colors.</param>
        /// <returns>A styled <see cref="Style"/> applicable to <see cref="ContextMenu"/> controls.</returns>
        public static Style CreateContextMenuStyle(bool isDark)
        {
            var style = new Style(typeof(ContextMenu));
            var template = new ControlTemplate(typeof(ContextMenu));

            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, isDark ? new SolidColorBrush(Color.FromArgb(235, 32, 32, 32)) : new SolidColorBrush(Color.FromArgb(240, 252, 252, 252)));
            border.SetValue(Border.BorderBrushProperty, DwmHelper.GetSubtleBorderBrush(isDark));
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

        /// <summary>
        /// Creates a Fluent-styled <see cref="MenuItem"/> style featuring interactive hover/pressed states,
        /// modern checkmark glyph animations, and high-DPI font styling.
        /// </summary>
        /// <param name="isDark">Indicates whether to generate dark or light theme colors.</param>
        /// <returns>A styled <see cref="Style"/> applicable to standard <see cref="MenuItem"/> items.</returns>
        public static Style CreateMenuItemStyle(bool isDark)
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
            checkGlyph.SetValue(TextBlock.ForegroundProperty, DwmHelper.GetAccentBrush(isDark));
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
                DwmHelper.GetTaskbarButtonHoverBrush(isDark),
                "Bd"));
            template.Triggers.Add(highlightTrigger);

            var pressedTrigger = new Trigger { Property = MenuItem.IsPressedProperty, Value = true };
            pressedTrigger.Setters.Add(new Setter(
                Border.BackgroundProperty,
                DwmHelper.GetTaskbarButtonPressedBrush(isDark),
                "Bd"));
            template.Triggers.Add(pressedTrigger);

            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(Control.ForegroundProperty, DwmHelper.GetPrimaryTextBrush(isDark)));
            style.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif")));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));

            return style;
        }

        /// <summary>
        /// Creates a specialized <see cref="MenuItem"/> template for parent headers hosting cascading submenus,
        /// featuring an interactive disclosure chevron and drop shadow floating popup.
        /// </summary>
        /// <param name="isDark">Indicates whether to generate dark or light theme colors.</param>
        /// <returns>A styled <see cref="Style"/> applicable to cascading submenu header items.</returns>
        public static Style CreateSubmenuHeaderStyle(bool isDark)
        {
            var style = new Style(typeof(MenuItem));
            var template = new ControlTemplate(typeof(MenuItem));

            var border = new FrameworkElementFactory(typeof(Border), "Bd");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.PaddingProperty, new Thickness(8, 6, 8, 6));
            border.SetValue(Border.MarginProperty, new Thickness(0, 1, 0, 1));
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);

            var grid = new FrameworkElementFactory(typeof(Grid));

            var col0 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col0.SetValue(ColumnDefinition.WidthProperty, new GridLength(20));
            grid.AppendChild(col0);

            var col1 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col1.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            grid.AppendChild(col1);

            var col2 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col2.SetValue(ColumnDefinition.WidthProperty, new GridLength(16));
            grid.AppendChild(col2);

            var iconGlyph = new FrameworkElementFactory(typeof(TextBlock), "IconGlyph");
            iconGlyph.SetValue(Grid.ColumnProperty, 0);
            iconGlyph.SetValue(TextBlock.TextProperty, "\uE772");
            iconGlyph.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"));
            iconGlyph.SetValue(TextBlock.FontSizeProperty, 11.5);
            iconGlyph.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            iconGlyph.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            iconGlyph.SetValue(TextBlock.ForegroundProperty, DwmHelper.GetPrimaryTextBrush(isDark));
            grid.AppendChild(iconGlyph);

            var cp = new FrameworkElementFactory(typeof(ContentPresenter), "HeaderHost");
            cp.SetValue(Grid.ColumnProperty, 1);
            cp.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            cp.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            grid.AppendChild(cp);

            var arrow = new FrameworkElementFactory(typeof(TextBlock), "SubmenuArrow");
            arrow.SetValue(Grid.ColumnProperty, 2);
            arrow.SetValue(TextBlock.TextProperty, "\uE76C");
            arrow.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"));
            arrow.SetValue(TextBlock.FontSizeProperty, 9.0);
            arrow.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            arrow.SetValue(TextBlock.OpacityProperty, 0.6);
            arrow.SetValue(TextBlock.ForegroundProperty, DwmHelper.GetSecondaryTextBrush(isDark));
            grid.AppendChild(arrow);

            var popup = new FrameworkElementFactory(typeof(Popup), "PART_Popup");
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Right);
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.FocusableProperty, false);
            popup.SetValue(Popup.HorizontalOffsetProperty, -2.0);
            popup.SetValue(Popup.VerticalOffsetProperty, -4.0);
            popup.SetBinding(Popup.IsOpenProperty, new Binding("IsSubmenuOpen")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });

            var subBorder = new FrameworkElementFactory(typeof(Border));
            subBorder.SetValue(Border.BackgroundProperty, isDark ? new SolidColorBrush(Color.FromArgb(235, 32, 32, 32)) : new SolidColorBrush(Color.FromArgb(240, 252, 252, 252)));
            subBorder.SetValue(Border.BorderBrushProperty, DwmHelper.GetSubtleBorderBrush(isDark));
            subBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            subBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            subBorder.SetValue(Border.PaddingProperty, new Thickness(4));
            subBorder.SetValue(Border.SnapsToDevicePixelsProperty, true);

            var dropShadow = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 3,
                Direction = 270,
                Color = Colors.Black,
                Opacity = isDark ? 0.45 : 0.15
            };
            subBorder.SetValue(UIElement.EffectProperty, dropShadow);

            var itemsPresenter = new FrameworkElementFactory(typeof(ItemsPresenter));
            subBorder.AppendChild(itemsPresenter);
            popup.AppendChild(subBorder);

            grid.AppendChild(popup);
            border.AppendChild(grid);
            template.VisualTree = border;

            var highlightTrigger = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
            highlightTrigger.Setters.Add(new Setter(
                Border.BackgroundProperty,
                DwmHelper.GetTaskbarButtonHoverBrush(isDark),
                "Bd"));
            template.Triggers.Add(highlightTrigger);

            var submenuOpenTrigger = new Trigger { Property = MenuItem.IsSubmenuOpenProperty, Value = true };
            submenuOpenTrigger.Setters.Add(new Setter(
                Border.BackgroundProperty,
                DwmHelper.GetTaskbarButtonHoverBrush(isDark),
                "Bd"));
            template.Triggers.Add(submenuOpenTrigger);

            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(Control.ForegroundProperty, DwmHelper.GetPrimaryTextBrush(isDark)));
            style.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif")));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));

            return style;
        }

        /// <summary>
        /// Creates a subtle divider line with theme-aware opacity for separating menu sections.
        /// </summary>
        /// <param name="isDark">Indicates whether to generate dark or light theme separator colors.</param>
        /// <returns>A styled <see cref="Separator"/> instance.</returns>
        public static Separator CreateStyledSeparator(bool isDark)
        {
            return new Separator
            {
                Margin = new Thickness(4, 3, 4, 3),
                Height = 1,
                Background = DwmHelper.GetSubtleBorderBrush(isDark),
                BorderThickness = new Thickness(0)
            };
        }

        /// <summary>
        /// Convenience factory method for creating and initializing a <see cref="MenuItem"/> with text, style,
        /// and optional checkable state.
        /// </summary>
        public static MenuItem CreateStyledMenuItem(string text, Style style, bool isCheckable = false, bool isChecked = false)
        {
            return new MenuItem
            {
                Header = text,
                Style = style,
                IsCheckable = isCheckable,
                IsChecked = isChecked
            };
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Context Menu Builder
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Builds and opens a Fluent-styled context menu configured for either taskbar overlay widget clicks
        /// or system tray notification icon clicks.
        /// </summary>
        /// <param name="owner">The parent <see cref="OverlayWindow"/> instance.</param>
        /// <param name="fromTray">Flag indicating whether triggered from the tray notification icon.</param>
        /// <param name="isDark">Current Windows dark theme setting.</param>
        /// <param name="devicesList">List of all known devices for the visibility submenu.</param>
        /// <param name="onOpenFlyout">Callback to open the Fluent Flyout.</param>
        /// <param name="onRefresh">Callback to trigger an immediate hardware refresh.</param>
        /// <param name="onReloadProfiles">Callback to reload device profiles from external JSON files.</param>
        public static void Show(
            OverlayWindow owner,
            bool fromTray,
            bool isDark,
            List<TaskbarDeviceState> devicesList,
            Action onOpenFlyout,
            Action onRefresh,
            Action onReloadProfiles = null)
        {
            if (owner == null) return;

            var hwnd = new System.Windows.Interop.WindowInteropHelper(owner).Handle;
            if (hwnd != IntPtr.Zero)
            {
                DwmHelper.SetDarkMode(hwnd, isDark);
            }

            var itemStyle = GetMenuItemStyle(isDark);
            var menu = new ContextMenu
            {
                Style = GetContextMenuStyle(isDark)
            };

            if (fromTray)
            {
                menu.Placement = PlacementMode.MousePoint;
                menu.PlacementTarget = owner;

                var openPanelItem = CreateStyledMenuItem("Open Panel", itemStyle);
                openPanelItem.Click += (s, e) => onOpenFlyout();
                menu.Items.Add(openPanelItem);

                menu.Items.Add(CreateStyledSeparator(isDark));
            }
            else
            {
                menu.Placement = PlacementMode.Top;
                menu.PlacementTarget = owner;
                menu.VerticalOffset = -14.0; // Matches Fluent Flyout spacing above taskbar
            }

            // Display style toggle
            var settings = SettingsManager.Instance.Current;
            var styleItem = CreateStyledMenuItem("Display Style: " + (settings.DisplayStyle == 0 ? "Percentage (85%)" : "Battery Icon"), itemStyle);
            styleItem.Click += (s, e) =>
            {
                settings.DisplayStyle = settings.DisplayStyle == 0 ? 1 : 0;
                SettingsManager.Instance.Save();
                owner.RefreshWidgetState();
            };
            menu.Items.Add(styleItem);

            // Display mode toggle (Floating Taskbar Widget vs Native System Tray Only)
            var modeItem = CreateStyledMenuItem("Display Mode: " + (settings.DisplayMode == 0 ? "Taskbar Widget" : "System Tray Only"), itemStyle);
            modeItem.Click += (s, e) =>
            {
                settings.DisplayMode = settings.DisplayMode == 0 ? 1 : 0;
                SettingsManager.Instance.Save();
                owner.RefreshWidgetState();
            };
            menu.Items.Add(modeItem);

            // Hide when disconnected toggle
            var hideItem = CreateStyledMenuItem("Hide when disconnected", itemStyle, true, settings.HideWhenDisconnected);
            hideItem.Click += (s, e) =>
            {
                settings.HideWhenDisconnected = hideItem.IsChecked;
                SettingsManager.Instance.Save();
                owner.RefreshWidgetState();
            };
            menu.Items.Add(hideItem);

            // Devices visibility cascading submenu
            var devicesSubmenu = new MenuItem
            {
                Header = "Device Visibility",
                Style = GetSubmenuHeaderStyle(isDark),
                ToolTip = "Select which devices to show in the taskbar and flyout"
            };

            var allDevs = devicesList ?? new List<TaskbarDeviceState>();
            if (allDevs.Count == 0)
            {
                var emptyItem = CreateStyledMenuItem("(No devices found)", itemStyle);
                emptyItem.IsEnabled = false;
                devicesSubmenu.Items.Add(emptyItem);
            }
            else
            {
                var sorted = OverlayWindow.SortDevicesByCustomOrder(allDevs);
                var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var d in sorted)
                {
                    string key = d.DisplayName;
                    nameCounts[key] = (nameCounts.ContainsKey(key) ? nameCounts[key] : 0) + 1;
                }

                var seenCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var d in sorted)
                {
                    string devTargetKey = !string.IsNullOrEmpty(d.Id) ? d.Id : d.Name;
                    string label = d.DisplayName;
                    if (nameCounts[d.DisplayName] > 1)
                    {
                        int idx = (seenCounts.ContainsKey(d.DisplayName) ? seenCounts[d.DisplayName] : 0) + 1;
                        seenCounts[d.DisplayName] = idx;
                        label = string.Format("{0} (#{1})", label, idx);
                    }

                    if (!d.IsVerified)
                    {
                        label = label + " (unverified)";
                    }

                    bool isVisible = owner.IsDeviceVisible(d);
                    var devItem = CreateStyledMenuItem(label, itemStyle, true, isVisible);
                    devItem.StaysOpenOnClick = true;
                    devItem.Click += (s, e) =>
                    {
                        owner.SetDeviceVisibility(devTargetKey, devItem.IsChecked);
                    };
                    devicesSubmenu.Items.Add(devItem);
                }
            }
            menu.Items.Add(devicesSubmenu);

            // Startup registration toggle
            var startupItem = CreateStyledMenuItem("Run on startup", itemStyle, true, settings.RunOnStartup);
            startupItem.Click += (s, e) =>
            {
                settings.RunOnStartup = startupItem.IsChecked;
                SettingsManager.Instance.Save();
            };
            menu.Items.Add(startupItem);

            menu.Items.Add(CreateStyledSeparator(isDark));

            // Force refresh action
            var refreshItem = CreateStyledMenuItem("Refresh Device Info", itemStyle);
            refreshItem.Click += (s, e) => onRefresh();
            menu.Items.Add(refreshItem);

            // Update device profiles from GitHub (OTA)
            if (onReloadProfiles != null)
            {
                var updateProfilesItem = CreateStyledMenuItem("Update Profiles from GitHub", itemStyle);
                updateProfilesItem.Click += (s, e) => onReloadProfiles();
                menu.Items.Add(updateProfilesItem);
            }

            // Exit application
            var exitItem = CreateStyledMenuItem("Exit", itemStyle);
            exitItem.Click += (s, e) => Application.Current.Shutdown();
            menu.Items.Add(exitItem);

            menu.Opened += (s, e) => _isContextMenuOpen = true;
            menu.Closed += (s, e) =>
            {
                _isContextMenuOpen = false;
                var trimTimer = new DispatcherTimer(DispatcherPriority.Background, menu.Dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(150)
                };
                trimTimer.Tick += (ts, te) =>
                {
                    trimTimer.Stop();
                    menu.Items.Clear();
                    TaskbarHelper.TrimProcessMemory();
                };
                trimTimer.Start();
            };

            menu.IsOpen = true;
        }
    }
}
