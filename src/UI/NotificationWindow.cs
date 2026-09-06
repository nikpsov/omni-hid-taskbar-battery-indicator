using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace OmniHidTaskbar.UI
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Toast Notification Window
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Lightweight non-intrusive toast notification popup anchored above the system tray,
    /// used for warning when a peripheral's battery drops to low or critical levels.
    /// </summary>
    public class NotificationWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of <see cref="NotificationWindow"/> and positions it at the bottom-right work area.
        /// </summary>
        /// <param name="title">Header alert text (e.g. "Low Battery Alert").</param>
        /// <param name="message">Informative message describing the affected peripheral and percentage.</param>
        public NotificationWindow(string title, string message)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            Topmost = true;
            ShowInTaskbar = false;
            Width = 320;
            Height = 90;

            // Anchor toast 10 DIPs from the bottom-right screen work area
            this.Left = SystemParameters.WorkArea.Right - this.Width - 10;
            this.Top = SystemParameters.WorkArea.Bottom - this.Height - 10;

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(240, 30, 30, 30)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(100, 100, 100, 100)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16)
            };

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleText = new TextBlock
            {
                Text = title,
                Foreground = Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 4)
            };

            var msgText = new TextBlock
            {
                Text = message,
                Foreground = Brushes.LightGray,
                FontSize = 13
            };

            stack.Children.Add(titleText);
            stack.Children.Add(msgText);

            border.Child = stack;
            Content = border;

            this.SourceInitialized += (s, e) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    DwmHelper.SetDarkMode(hwnd, true);
                }
            };

            // Auto-dismiss after 5 seconds
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                this.Close();
            };
            timer.Start();

            this.MouseLeftButtonUp += (s, e) => this.Close();
        }
    }
}
