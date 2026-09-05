using System;
using OmniHid.Core.Abstractions;

namespace OmniHidTaskbar.Core
{
    public class TaskbarDeviceState
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public DeviceCategory Category { get; set; }
        public string IconGlyph { get; set; }
        public bool IsConnected { get; set; }
        public int BatteryPercent { get; set; }
        public bool IsCharging { get; set; }
        public int VoltageMv { get; set; }
        public int TimeToFullMin { get; set; }
        public int TimeToEmptyMin { get; set; }
        public string StatusText { get; set; }
        public bool IsWired { get; set; }
        public bool SupportsSettings { get; set; }

        public static TaskbarDeviceState FromOmniDevice(IOmniDevice dev)
        {
            if (dev == null) return null;

            var tel = dev.Telemetry ?? BatteryTelemetry.Offline();
            bool isOnline = dev.IsConnected && tel.IsAvailable;
            int level = isOnline ? tel.LevelPercent : -1;

            string statusText = tel.IsCharging
                ? "Charging вљЎ"
                : (isOnline ? tel.StateDescription : (dev.IsWired ? "Wired (Disconnected)" : "Offline / Sleeping"));

            return new TaskbarDeviceState
            {
                Id = dev.Id,
                Name = dev.Name,
                Category = dev.Category,
                IconGlyph = GetDefaultGlyph(dev.Category),
                IsConnected = isOnline,
                BatteryPercent = level,
                IsCharging = tel.IsCharging,
                VoltageMv = tel.VoltageMv,
                TimeToFullMin = tel.TimeToFullMinutes,
                TimeToEmptyMin = tel.TimeToEmptyMinutes,
                StatusText = statusText,
                IsWired = dev.IsWired,
                SupportsSettings = false
            };
        }

        public string GetDefaultIconGlyph()
        {
            return GetDefaultGlyph(Category);
        }

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