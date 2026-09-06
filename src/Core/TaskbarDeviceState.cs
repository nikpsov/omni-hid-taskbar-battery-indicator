using System;
using OmniHid.Core.Abstractions;

namespace OmniHidTaskbar.Core
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Taskbar Peripheral Telemetry State Model
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Thread-safe view-model snapshot representing an aggregated peripheral's connectivity,
    /// battery gauge, charging state, and iconography for the taskbar widget and flyout.
    /// </summary>
    public class TaskbarDeviceState
    {
        /// <summary>
        /// Gets or sets the unique internal device identifier (typically VID:PID or driver key).
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets the human-readable device model name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets an optional user-defined friendly alias name.
        /// </summary>
        public string CustomName { get; set; }

        /// <summary>
        /// Gets the effective display name for UI rendering, preferring <see cref="CustomName"/> over <see cref="Name"/>.
        /// </summary>
        public string DisplayName
        {
            get { return !string.IsNullOrWhiteSpace(CustomName) ? CustomName : Name; }
        }

        /// <summary>
        /// Gets or sets the categorized peripheral type (Mouse, Keyboard, Headset, Gamepad).
        /// </summary>
        public DeviceCategory Category { get; set; }

        /// <summary>
        /// Gets or sets the Segoe Fluent / MDL2 Assets icon font glyph code.
        /// </summary>
        public string IconGlyph { get; set; }

        /// <summary>
        /// Gets or sets whether the device is currently active, responsive, and transmitting telemetry.
        /// </summary>
        public bool IsConnected { get; set; }

        /// <summary>
        /// Gets or sets the current battery percentage (0..100), or -1 when offline/unknown.
        /// </summary>
        public int BatteryPercent { get; set; }

        /// <summary>
        /// Gets or sets whether the device battery is actively charging over USB or dock.
        /// </summary>
        public bool IsCharging { get; set; }

        /// <summary>
        /// Gets or sets the cell voltage in millivolts (e.g. 4180 mV), or 0 if unsupported.
        /// </summary>
        public int VoltageMv { get; set; }

        /// <summary>
        /// Gets or sets the estimated remaining time in minutes until full battery charge, or -1.
        /// </summary>
        public int TimeToFullMin { get; set; }

        /// <summary>
        /// Gets or sets the estimated remaining time in minutes until battery discharge, or -1.
        /// </summary>
        public int TimeToEmptyMin { get; set; }

        /// <summary>
        /// Gets or sets a descriptive textual representation of the battery and power state.
        /// </summary>
        public string StatusText { get; set; }

        /// <summary>
        /// Gets or sets whether the peripheral is currently operating via a direct USB cable link.
        /// </summary>
        public bool IsWired { get; set; }

        // ═══════════════════════════════════════════════════════════════════════
        // Factory & Conversion Methods
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Creates a UI snapshot from an active OmniHID core device instance.
        /// </summary>
        /// <param name="dev">The underlying OmniHID device interface.</param>
        /// <returns>A populated <see cref="TaskbarDeviceState"/> snapshot, or <c>null</c> if input is null.</returns>
        public static TaskbarDeviceState FromOmniDevice(IOmniDevice dev)
        {
            if (dev == null) return null;

            var tel = dev.Telemetry ?? BatteryTelemetry.Offline();
            bool isOnline = dev.IsConnected && tel.IsAvailable;
            int level = isOnline ? tel.LevelPercent : -1;

            string statusText = tel.IsCharging
                ? "Charging \u26A1"
                : (isOnline ? tel.StateDescription : (dev.IsWired ? "Wired (Disconnected)" : "Offline / Sleeping"));

            string customName = null;
            if (SettingsManager.Instance.Current.CustomDeviceNames != null)
            {
                SettingsManager.Instance.Current.CustomDeviceNames.TryGetValue(dev.Id, out customName);
            }

            return new TaskbarDeviceState
            {
                Id = dev.Id,
                Name = dev.Name,
                CustomName = customName,
                Category = dev.Category,
                IconGlyph = GetDefaultGlyph(dev.Category),
                IsConnected = isOnline,
                BatteryPercent = level,
                IsCharging = tel.IsCharging,
                VoltageMv = tel.VoltageMv,
                TimeToFullMin = tel.TimeToFullMinutes,
                TimeToEmptyMin = tel.TimeToEmptyMinutes,
                StatusText = statusText,
                IsWired = dev.IsWired
            };
        }

        /// <summary>
        /// Gets the default Segoe MDL2 icon glyph for this device's category.
        /// </summary>
        /// <returns>A unicode glyph string representing the peripheral category icon.</returns>
        public string GetDefaultIconGlyph()
        {
            return GetDefaultGlyph(Category);
        }

        /// <summary>
        /// Resolves the canonical Segoe Fluent Icons / Segoe MDL2 Assets unicode glyph for a given peripheral category.
        /// </summary>
        /// <param name="category">The peripheral category enum.</param>
        /// <returns>A unicode string representing the glyph.</returns>
        public static string GetDefaultGlyph(DeviceCategory category)
        {
            switch (category)
            {
                case DeviceCategory.Headset:
                    return "\uE7F6"; // Segoe MDL2 Headphones
                case DeviceCategory.Mouse:
                    return "\uE962"; // Segoe MDL2 Mouse
                case DeviceCategory.Keyboard:
                    return "\uE765"; // Segoe MDL2 Keyboard
                case DeviceCategory.Gamepad:
                    return "\uE7FC"; // Segoe MDL2 Game controller
                default:
                    return "\uE772"; // Segoe MDL2 Hardware
            }
        }
    }
}